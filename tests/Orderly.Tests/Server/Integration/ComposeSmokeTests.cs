using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.SignalR.Client;
using Npgsql;
using Orderly.Contracts.Auth;
using Orderly.Contracts.Commerce;
using Orderly.Contracts.Permissions;
using Orderly.Contracts.Realtime;
using Orderly.Contracts.Sync;
using Orderly.Core.Commerce;
using Xunit;

namespace Orderly.Tests.Server.Integration;

public sealed class ComposeSmokeTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly Uri _baseUri;
    private readonly string _adminPassword;
    private readonly string _adminDeviceId;
    private readonly string _postgresConnectionString;

    public ComposeSmokeTests()
    {
        var baseUrl = Environment.GetEnvironmentVariable("ORDERLY_COMPOSE_SMOKE_BASE_URL");
        _adminPassword = Environment.GetEnvironmentVariable("ORDERLY_COMPOSE_SMOKE_ADMIN_PASSWORD") ?? string.Empty;
        _adminDeviceId = Environment.GetEnvironmentVariable("ORDERLY_COMPOSE_SMOKE_ADMIN_DEVICE_ID") ?? "t8-admin-device";
        _postgresConnectionString = Environment.GetEnvironmentVariable("ORDERLY_COMPOSE_SMOKE_PG_CONNECTION") ?? string.Empty;

        Skip.If(string.IsNullOrWhiteSpace(baseUrl), "ORDERLY_COMPOSE_SMOKE_BASE_URL is required.");
        Skip.If(string.IsNullOrWhiteSpace(_adminPassword), "ORDERLY_COMPOSE_SMOKE_ADMIN_PASSWORD is required.");
        Skip.If(string.IsNullOrWhiteSpace(_postgresConnectionString), "ORDERLY_COMPOSE_SMOKE_PG_CONNECTION is required.");

        _baseUri = new Uri(baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : baseUrl + "/");
    }

    [SkippableFact]
    public async Task Compose_api_sync_security_lifecycle_and_reliability_smoke()
    {
        using var anonymousClient = CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anonymousClient.GetAsync("health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymousClient.GetAsync("health/db")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymousClient.GetAsync("health/version")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.GetAsync("hubs/workspace/negotiate?negotiateVersion=1")).StatusCode);

        var suffix = NewSuffix();
        var adminLogin = await LoginAsync("admin", _adminPassword, _adminDeviceId, $"admin-login-{suffix}");
        using var adminClient = CreateAuthenticatedClient(adminLogin.AccessToken);
        var me = await ReadOkAsync<AuthMeResponse>(await adminClient.GetAsync("api/auth/me"));
        Assert.Equal(CloudRole.Admin, me.WorkspaceMembership.CloudRole);
        Assert.Equal(BusinessLabel.Operator, me.WorkspaceMembership.BusinessLabel);
        var workspaceId = me.WorkspaceMembership.WorkspaceId;

        var employee = await VerifyApplicationsDevicesPermissionsAndAuditAsync(
            adminClient,
            anonymousClient,
            workspaceId,
            suffix);

        await VerifySyncConflictsIdempotencyCursorAndSignalRAsync(
            adminClient,
            adminLogin.AccessToken,
            workspaceId,
            adminLogin.User.Id,
            suffix);

        await VerifyAttachmentsPermanentDeleteAndAuditAsync(
            adminClient,
            employee.Client,
            workspaceId,
            suffix);

        await VerifyAdminOpsAsync(adminClient, employee.Client, workspaceId, suffix);
    }

    private async Task<UserSession> VerifyApplicationsDevicesPermissionsAndAuditAsync(
        HttpClient adminClient,
        HttpClient anonymousClient,
        Guid workspaceId,
        string suffix)
    {
        var inviteCode = $"T8-{suffix}";
        var username = $"t8-user-{suffix}";
        const string password = "EmployeeT8Pwd!";

        var invitation = await ReadOkAsync<CloudInvitationDto>(await adminClient.PostAsJsonAsync(
            "api/users/invitations",
            new CreateInvitationRequest
            {
                Code = inviteCode,
                CloudRole = CloudRole.Employee,
                BusinessLabel = BusinessLabel.Staff,
                MaxUses = 1,
                IdempotencyKey = $"invite-{suffix}"
            }));
        Assert.Equal(inviteCode.ToUpperInvariant(), invitation.Code);

        var application = await ReadOkAsync<CloudUserApplicationDto>(await anonymousClient.PostAsJsonAsync(
            "api/auth/applications",
            new SubmitUserApplicationRequest
            {
                InviteCode = inviteCode,
                Username = username,
                DisplayName = "T8 Employee",
                InitialPassword = password,
                DeviceId = $"{username}-device-a",
                DeviceName = "T8 Device A",
                IdempotencyKey = $"apply-{suffix}"
            }));
        Assert.Equal(CloudUserApplicationStatus.Pending, application.Status);

        var approved = await ReadOkAsync<CloudUserApplicationDto>(await adminClient.PostAsJsonAsync(
            $"api/users/applications/{application.Id:N}/approve",
            new ReviewUserApplicationRequest { Reason = "verified", IdempotencyKey = $"approve-app-{suffix}" }));
        Assert.Equal(CloudUserApplicationStatus.Approved, approved.Status);

        var firstLogin = await LoginAsync(username, password, $"{username}-device-a", $"login-a-{suffix}");
        Assert.Equal(workspaceId, firstLogin.WorkspaceMembership.WorkspaceId);
        Assert.Equal(CloudRole.Employee, firstLogin.WorkspaceMembership.CloudRole);

        var employeeClient = CreateAuthenticatedClient(firstLogin.AccessToken);
        var employeeMe = await ReadOkAsync<AuthMeResponse>(await employeeClient.GetAsync("api/auth/me"));
        Assert.Equal(CloudRole.Employee, employeeMe.WorkspaceMembership.CloudRole);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.PostAsJsonAsync(
            "api/auth/login",
            new LoginRequest
            {
                Username = username,
                Password = password,
                DeviceId = $"{username}-device-b",
                DeviceName = "T8 Device B",
                ClientRequestId = $"login-b-{suffix}"
            })).StatusCode);

        var pendingSecond = await FindDeviceAsync(adminClient, username, $"{username}-device-b", CloudDeviceStatus.Pending);
        Assert.Equal(HttpStatusCode.NoContent, (await adminClient.PostAsJsonAsync(
            $"api/users/devices/{pendingSecond.Id:N}/approve",
            new ReviewUserApplicationRequest { IdempotencyKey = $"approve-device-b-{suffix}" })).StatusCode);

        var secondLogin = await LoginAsync(username, password, $"{username}-device-b", $"login-b-ok-{suffix}");

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.PostAsJsonAsync(
            "api/auth/login",
            new LoginRequest
            {
                Username = username,
                Password = password,
                DeviceId = $"{username}-device-c",
                DeviceName = "T8 Device C",
                ClientRequestId = $"login-c-{suffix}"
            })).StatusCode);

        var pendingThird = await FindDeviceAsync(adminClient, username, $"{username}-device-c", CloudDeviceStatus.Pending);
        Assert.Equal(HttpStatusCode.NoContent, (await adminClient.PostAsJsonAsync(
            $"api/users/devices/{pendingThird.Id:N}/reject",
            new ReviewUserApplicationRequest { IdempotencyKey = $"reject-device-c-{suffix}" })).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.PostAsJsonAsync(
            "api/auth/login",
            new LoginRequest
            {
                Username = username,
                Password = password,
                DeviceId = $"{username}-device-c",
                DeviceName = "T8 Device C",
                ClientRequestId = $"login-c-rejected-{suffix}"
            })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await adminClient.PostAsJsonAsync(
            $"api/users/devices/{pendingSecond.Id:N}/revoke",
            new ReviewUserApplicationRequest { IdempotencyKey = $"revoke-device-b-{suffix}" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await CreateAuthenticatedClient(secondLogin.AccessToken).GetAsync("api/auth/me")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await employeeClient.GetAsync("api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employeeClient.GetAsync($"api/workspaces/{workspaceId:N}/cashflow/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employeeClient.PostAsJsonAsync(
            $"api/workspaces/{workspaceId:N}/products",
            new CreateProductCommand
            {
                Name = "Forbidden Product",
                Code = $"FORB-{suffix}",
                ProductType = ProductType.Physical,
                DefaultPrice = 10,
                IdempotencyKey = $"forbidden-product-{suffix}"
            })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employeeClient.GetAsync($"api/workspaces/{Guid.NewGuid():N}/sync/changes?afterSequence=0")).StatusCode);

        var actions = await LoadAuditActionsAsync(workspaceId);
        Assert.Contains("InvitationCreated", actions);
        Assert.Contains("UserApplicationSubmitted", actions);
        Assert.Contains("UserApplicationApproved", actions);
        Assert.Contains("WorkspaceMemberAuthorized", actions);
        Assert.Contains("DeviceApproved", actions);
        Assert.Contains("DeviceApprovalRequired", actions);
        Assert.Contains("DeviceRejected", actions);
        Assert.Contains("DeviceRevoked", actions);
        Assert.Contains("LoginFailed", actions);

        return new UserSession(employeeClient, firstLogin.User.Id);
    }

    private async Task VerifySyncConflictsIdempotencyCursorAndSignalRAsync(
        HttpClient adminClient,
        string adminToken,
        Guid workspaceId,
        Guid adminUserId,
        string suffix)
    {
        using var clientB = CreateAuthenticatedClient(adminToken);
        using var clientC = CreateAuthenticatedClient(adminToken);

        var signalReceived = new TaskCompletionSource<RealtimeEventPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(_baseUri, "hubs/workspace"), options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(adminToken);
            })
            .Build();
        hub.On<RealtimeEventPayload>(RealtimeEvent.EntityUpdated, payload =>
        {
            if (payload.EntityType == EntityType.Product)
            {
                signalReceived.TrySetResult(payload);
            }
        });
        await hub.StartAsync();
        await hub.InvokeAsync("JoinWorkspace", workspaceId);

        var created = await CreateProductAsync(adminClient, workspaceId, $"Sync Product {suffix}", $"SYNC-{suffix}");
        var signalPayload = await signalReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(created.Id, signalPayload.EntityId);
        Assert.True(signalPayload.Sequence > 0);
        Assert.Null(signalPayload.HintJson);
        Assert.DoesNotContain(created.Name, JsonSerializer.Serialize(signalPayload), StringComparison.Ordinal);

        var initialChanges = await ReadOkAsync<ChangesResponse>(await clientB.GetAsync($"api/workspaces/{workspaceId:N}/sync/changes?afterSequence=0&maxCount=20"));
        Assert.Contains(initialChanges.Changes, change => change.EntityType == EntityType.Product && change.EntityId == created.Id);
        var offlineCursor = initialChanges.ToSequence;

        var updateOne = await UpdateProductAsync(
            adminClient,
            workspaceId,
            created.Id,
            new UpdateProductCommand
            {
                BaseVersion = created.Version,
                Name = $"Sync Product {suffix} A1",
                ChangedFields = new() { "Name" },
                IdempotencyKey = $"sync-update-one-{suffix}"
            });
        var updateTwo = await UpdateProductAsync(
            adminClient,
            workspaceId,
            created.Id,
            new UpdateProductCommand
            {
                BaseVersion = updateOne.Version,
                Description = "A2 description",
                ChangedFields = new() { "Description" },
                IdempotencyKey = $"sync-update-two-{suffix}"
            });

        var catchup = await ReadOkAsync<ChangesResponse>(await clientB.GetAsync($"api/workspaces/{workspaceId:N}/sync/changes?afterSequence={offlineCursor}&maxCount=20"));
        Assert.Contains(catchup.Changes, change => change.EntityId == created.Id && change.Sequence > offlineCursor);
        Assert.All(catchup.Changes, change => Assert.True(change.Sequence > offlineCursor));
        Assert.True(catchup.ToSequence >= updateTwo.Version);

        var conflictBase = await CreateProductAsync(adminClient, workspaceId, $"Conflict Product {suffix}", $"CONF-{suffix}");
        await UpdateProductAsync(
            adminClient,
            workspaceId,
            conflictBase.Id,
            new UpdateProductCommand
            {
                BaseVersion = conflictBase.Version,
                Name = $"Conflict Product {suffix} A",
                ChangedFields = new() { "Name" },
                IdempotencyKey = $"same-field-a-{suffix}"
            });
        Assert.Equal(HttpStatusCode.Conflict, (await clientB.PutAsJsonAsync(
            $"api/workspaces/{workspaceId:N}/products/{conflictBase.Id:N}",
            new UpdateProductCommand
            {
                BaseVersion = conflictBase.Version,
                Name = $"Conflict Product {suffix} B",
                ChangedFields = new() { "Name" },
                IdempotencyKey = $"same-field-b-{suffix}"
            })).StatusCode);

        var mergeBase = await CreateProductAsync(adminClient, workspaceId, $"Merge Product {suffix}", $"MERGE-{suffix}");
        var mergeA = await UpdateProductAsync(
            adminClient,
            workspaceId,
            mergeBase.Id,
            new UpdateProductCommand
            {
                BaseVersion = mergeBase.Version,
                Name = $"Merge Product {suffix} A",
                ChangedFields = new() { "Name" },
                IdempotencyKey = $"merge-a-{suffix}"
            });
        var mergeB = await UpdateProductAsync(
            clientB,
            workspaceId,
            mergeBase.Id,
            new UpdateProductCommand
            {
                BaseVersion = mergeBase.Version,
                Description = "B merged description",
                ChangedFields = new() { "Description" },
                IdempotencyKey = $"merge-b-{suffix}"
            });
        Assert.True(mergeB.Version > mergeA.Version);
        var merged = await ReadOkAsync<CloudProductDto>(await clientC.GetAsync($"api/workspaces/{workspaceId:N}/products/{mergeBase.Id:N}"));
        Assert.Equal($"Merge Product {suffix} A", merged.Name);
        Assert.Equal("B merged description", merged.Description);

        var idempotentCommand = new CreateProductCommand
        {
            Name = $"Idempotent Product {suffix}",
            Code = $"IDEM-{suffix}",
            ProductType = ProductType.Physical,
            DefaultPrice = 20,
            ChangedFields = new() { "Name", "Code", "ProductType", "DefaultPrice" },
            IdempotencyKey = $"idem-create-{suffix}"
        };
        var idemOne = await ReadOkAsync<CloudProductDto>(await adminClient.PostAsJsonAsync($"api/workspaces/{workspaceId:N}/products", idempotentCommand));
        var idemTwo = await ReadOkAsync<CloudProductDto>(await adminClient.PostAsJsonAsync($"api/workspaces/{workspaceId:N}/products", idempotentCommand));
        Assert.Equal(idemOne.Id, idemTwo.Id);
        Assert.Equal(1, await ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM ""CommerceProducts"" WHERE ""WorkspaceId"" = @workspaceId AND ""Code"" = @code;",
            new { workspaceId, code = $"IDEM-{suffix}" }));
        Assert.Equal(1, await ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM ""CloudIdempotencyKeys""
              WHERE ""WorkspaceId"" = @workspaceId AND ""UserId"" = @adminUserId AND ""Action"" = @action AND ""ClientRequestId"" = @clientRequestId;",
            new { workspaceId, adminUserId, action = "product:create", clientRequestId = $"idem-create-{suffix}" }));

        Assert.Equal(HttpStatusCode.Forbidden, (await clientC.GetAsync($"api/workspaces/{Guid.NewGuid():N}/sync/changes?afterSequence=0")).StatusCode);
    }

    private async Task VerifyAttachmentsPermanentDeleteAndAuditAsync(
        HttpClient adminClient,
        HttpClient employeeClient,
        Guid workspaceId,
        string suffix)
    {
        var adminCustomer = await CreateCustomerAsync(adminClient, workspaceId, $"Attachment Customer {suffix}", $"130{suffix[..6]}");
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("attachment payload"));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        using var form = new MultipartFormDataContent
        {
            { content, "file", "payload.txt" },
            { new StringContent($"attach-{suffix}"), "clientRequestId" }
        };

        var uploadResponse = await adminClient.PostAsync($"api/workspaces/{workspaceId:N}/lifecycle/attachments/customer/{adminCustomer.Id:N}", form);
        var attachmentJson = await uploadResponse.Content.ReadAsStringAsync();
        uploadResponse.EnsureSuccessStatusCode();
        Assert.DoesNotContain("blobKey", attachmentJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("url", attachmentJson, StringComparison.OrdinalIgnoreCase);
        var attachment = JsonSerializer.Deserialize<CloudAttachmentDto>(attachmentJson, JsonOptions)
            ?? throw new InvalidOperationException("Attachment response is empty.");

        Assert.Equal(HttpStatusCode.Forbidden, (await employeeClient.GetAsync($"api/workspaces/{workspaceId:N}/lifecycle/attachments/customer/{adminCustomer.Id:N}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employeeClient.GetAsync($"api/workspaces/{workspaceId:N}/lifecycle/attachments/{attachment.Id:N}/download")).StatusCode);

        var downloadResponse = await adminClient.GetAsync($"api/workspaces/{workspaceId:N}/lifecycle/attachments/{attachment.Id:N}/download");
        downloadResponse.EnsureSuccessStatusCode();
        Assert.Equal("attachment payload", await downloadResponse.Content.ReadAsStringAsync());

        Assert.Equal(0, await ExecuteScalarAsync<int>(
            @"SELECT COUNT(*)
              FROM information_schema.columns
              WHERE table_name = 'CloudAttachments' AND data_type = 'bytea';"));
        var metadata = await QuerySingleAsync<(string Sha256, long Version, long SizeBytes)>(
            @"SELECT ""Sha256"", ""Version"", ""SizeBytes"" FROM ""CloudAttachments"" WHERE ""Id"" = @id;",
            new { id = attachment.Id });
        Assert.False(string.IsNullOrWhiteSpace(metadata.Sha256));
        Assert.Equal(1, metadata.Version);
        Assert.Equal("attachment payload".Length, metadata.SizeBytes);

        var employeeCustomer = await CreateCustomerAsync(employeeClient, workspaceId, $"Employee Customer {suffix}", $"131{suffix[..6]}");
        var archiveEmployeeCustomer = await employeeClient.PostAsJsonAsync(
            $"api/workspaces/{workspaceId:N}/archive/customer/{employeeCustomer.Id:N}",
            new ArchiveCommand
            {
                BaseVersion = employeeCustomer.Version,
                ArchiveReason = "ordinary archive",
                IdempotencyKey = $"employee-archive-{suffix}"
            });
        archiveEmployeeCustomer.EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await employeeClient.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"api/workspaces/{workspaceId:N}/lifecycle/permanent/customer/{employeeCustomer.Id:N}")
        {
            Content = JsonContent.Create(new PermanentDeleteRequest { Confirm = true, Reason = "not allowed", ClientRequestId = $"employee-delete-{suffix}" })
        })).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await adminClient.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"api/workspaces/{workspaceId:N}/lifecycle/permanent/customer/{employeeCustomer.Id:N}")
        {
            Content = JsonContent.Create(new PermanentDeleteRequest { Confirm = true, Reason = "too early", ClientRequestId = $"too-early-delete-{suffix}" })
        })).StatusCode);

        await ExecuteAsync(
            @"UPDATE ""CommerceCustomers"" SET ""DeletedAt"" = @deletedAt WHERE ""Id"" = @id;",
            new { deletedAt = DateTime.UtcNow.AddDays(-31), id = employeeCustomer.Id });
        Assert.Equal(HttpStatusCode.NoContent, (await adminClient.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"api/workspaces/{workspaceId:N}/lifecycle/permanent/customer/{employeeCustomer.Id:N}")
        {
            Content = JsonContent.Create(new PermanentDeleteRequest { Confirm = true, Reason = "retention satisfied", ClientRequestId = $"admin-delete-{suffix}" })
        })).StatusCode);

        var actions = await LoadAuditActionsAsync(workspaceId);
        Assert.Contains("AttachmentUploaded", actions);
        Assert.Contains("AttachmentDownloaded", actions);
        Assert.Contains("Archived", actions);
        Assert.Contains("PermanentlyDeleted", actions);
    }

    private async Task VerifyAdminOpsAsync(HttpClient adminClient, HttpClient employeeClient, Guid workspaceId, string suffix)
    {
        Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync($"api/workspaces/{workspaceId:N}/admin/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync($"api/workspaces/{workspaceId:N}/admin/backups")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync($"api/workspaces/{workspaceId:N}/admin/sync-issues")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync($"api/workspaces/{workspaceId:N}/admin/audit-logs?limit=10")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employeeClient.GetAsync($"api/workspaces/{workspaceId:N}/admin/health")).StatusCode);

        var product = await CreateProductAsync(adminClient, workspaceId, $"Ops Product {suffix}", $"OPS-{suffix}");
        var history = await ReadOkAsync<IReadOnlyList<CloudEntityVersionDto>>(await adminClient.GetAsync($"api/workspaces/{workspaceId:N}/lifecycle/history/product/{product.Id:N}"));
        Assert.NotEmpty(history);

        var archive = await adminClient.PostAsJsonAsync(
            $"api/workspaces/{workspaceId:N}/archive/product/{product.Id:N}",
            new ArchiveCommand
            {
                BaseVersion = product.Version,
                ArchiveReason = "repair drill",
                IdempotencyKey = $"ops-archive-{suffix}"
            });
        archive.EnsureSuccessStatusCode();
        var archived = await archive.Content.ReadFromJsonAsync<CloudProductDto>(JsonOptions)
            ?? throw new InvalidOperationException("Archive response is empty.");

        var recover = await adminClient.PostAsJsonAsync(
            $"api/workspaces/{workspaceId:N}/archive/product/{product.Id:N}/recover",
            new RecoverCommand
            {
                BaseVersion = archived.Version,
                Reason = "repair drill",
                IdempotencyKey = $"ops-recover-{suffix}"
            });
        recover.EnsureSuccessStatusCode();

        var stillWorks = await CreateProductAsync(adminClient, workspaceId, $"Ops Product 2 {suffix}", $"OPS2-{suffix}");
        Assert.NotEqual(Guid.Empty, stillWorks.Id);

        var actions = await LoadAuditActionsAsync(workspaceId);
        Assert.Contains("EntityHistoryViewed", actions);
        Assert.Contains("Recovered", actions);
        Assert.Contains("ProductCreated", actions);
    }

    private HttpClient CreateClient() => new()
    {
        BaseAddress = _baseUri,
        Timeout = TimeSpan.FromSeconds(30)
    };

    private HttpClient CreateAuthenticatedClient(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<LoginResponse> LoginAsync(string username, string password, string deviceId, string clientRequestId)
    {
        using var client = CreateClient();
        return await ReadOkAsync<LoginResponse>(await client.PostAsJsonAsync(
            "api/auth/login",
            new LoginRequest
            {
                Username = username,
                Password = password,
                DeviceId = deviceId,
                DeviceName = deviceId,
                ClientRequestId = clientRequestId
            }));
    }

    private async Task<CloudDeviceDto> FindDeviceAsync(HttpClient client, string username, string deviceId, string status)
    {
        var devices = await ReadOkAsync<IReadOnlyList<CloudDeviceDto>>(await client.GetAsync("api/users/devices"));
        return devices.Single(device =>
            device.Username == username
            && device.DeviceId == deviceId
            && device.Status == status);
    }

    private async Task<CloudProductDto> CreateProductAsync(HttpClient client, Guid workspaceId, string name, string code)
    {
        return await ReadOkAsync<CloudProductDto>(await client.PostAsJsonAsync(
            $"api/workspaces/{workspaceId:N}/products",
            new CreateProductCommand
            {
                Name = name,
                Code = code,
                ProductType = ProductType.Physical,
                DefaultPrice = 100,
                DefaultCost = 50,
                ChangedFields = new() { "Name", "Code", "ProductType", "DefaultPrice", "DefaultCost" },
                IdempotencyKey = $"create-product-{code}"
            }));
    }

    private async Task<CloudProductDto> UpdateProductAsync(HttpClient client, Guid workspaceId, Guid productId, UpdateProductCommand command)
    {
        return await ReadOkAsync<CloudProductDto>(await client.PutAsJsonAsync(
            $"api/workspaces/{workspaceId:N}/products/{productId:N}",
            command));
    }

    private async Task<CloudCustomerDto> CreateCustomerAsync(HttpClient client, Guid workspaceId, string name, string phone)
    {
        return await ReadOkAsync<CloudCustomerDto>(await client.PostAsJsonAsync(
            $"api/workspaces/{workspaceId:N}/customers",
            new CreateCustomerCommand
            {
                Name = name,
                Phone = phone,
                ChangedFields = new() { "Name", "Phone" },
                IdempotencyKey = $"create-customer-{phone}"
            }));
    }

    private async Task<IReadOnlyList<string>> LoadAuditActionsAsync(Guid workspaceId)
    {
        await using var connection = new NpgsqlConnection(_postgresConnectionString);
        var actions = await connection.QueryAsync<string>(
            @"SELECT ""Action"" FROM ""CloudAuditLogs"" WHERE ""WorkspaceId"" = @workspaceId;",
            new { workspaceId });
        return actions.ToList();
    }

    private async Task ExecuteAsync(string sql, object? args = null)
    {
        await using var connection = new NpgsqlConnection(_postgresConnectionString);
        await connection.ExecuteAsync(sql, args);
    }

    private async Task<T> ExecuteScalarAsync<T>(string sql, object? args = null)
    {
        await using var connection = new NpgsqlConnection(_postgresConnectionString);
        return (await connection.ExecuteScalarAsync<T>(sql, args))!;
    }

    private async Task<T> QuerySingleAsync<T>(string sql, object? args = null)
    {
        await using var connection = new NpgsqlConnection(_postgresConnectionString);
        return await connection.QuerySingleAsync<T>(sql, args);
    }

    private static async Task<T> ReadOkAsync<T>(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Expected success, got {(int)response.StatusCode} {response.StatusCode}: {content}");
        }

        return JsonSerializer.Deserialize<T>(content, JsonOptions)
            ?? throw new InvalidOperationException("Response body is empty.");
    }

    private static string NewSuffix() => Guid.NewGuid().ToString("N")[..8];

    private sealed record UserSession(HttpClient Client, Guid UserId);
}
