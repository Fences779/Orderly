using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orderly.Contracts.Auth;
using Orderly.Contracts.Commerce;
using Orderly.Contracts.Permissions;
using Orderly.Contracts.Realtime;
using Orderly.Contracts.Sync;
using Orderly.Core.Commerce;
using Orderly.Server.Data;
using Orderly.Server.Models;
using Orderly.Server.Services;
using Xunit;

namespace Orderly.Tests.Server.Integration;

[Collection("PostgresIntegration")]
public sealed class LocalIntegrationSmokeTests : IDisposable
{
    private readonly PostgresFixture _fixture;
    private readonly TestBlobStorage _blobStorage = new();
    private readonly ServerWebApplicationFactory _factory;

    public LocalIntegrationSmokeTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, "Docker is not available; skipping PostgreSQL integration smoke tests.");
        _factory = new ServerWebApplicationFactory(
            fixture,
            services =>
            {
                services.RemoveAll<IBlobStorage>();
                services.AddSingleton<IBlobStorage>(_blobStorage);
            });
    }

    public void Dispose() => _factory.Dispose();

    [SkippableFact]
    public async Task Api_smoke_covers_invitation_application_devices_permissions_and_audit()
    {
        var suffix = NewSuffix();
        var admin = await CreateAdminAsync($"t7-admin-{suffix}");
        var adminClient = CreateAuthenticatedClient(admin.Token);
        var inviteCode = $"T7-{suffix}";
        var username = $"t7-user-{suffix}";
        var password = "EmployeeT7Pwd!";

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

        var application = await ReadOkAsync<CloudUserApplicationDto>(await _factory.CreateClient().PostAsJsonAsync(
            "api/auth/applications",
            new SubmitUserApplicationRequest
            {
                InviteCode = inviteCode,
                Username = username,
                DisplayName = "T7 Employee",
                InitialPassword = password,
                DeviceId = $"{username}-device-a",
                DeviceName = "T7 Device A",
                IdempotencyKey = $"apply-{suffix}"
            }));
        Assert.Equal(CloudUserApplicationStatus.Pending, application.Status);

        var approved = await ReadOkAsync<CloudUserApplicationDto>(await adminClient.PostAsJsonAsync(
            $"api/users/applications/{application.Id:N}/approve",
            new ReviewUserApplicationRequest { Reason = "verified", IdempotencyKey = $"approve-app-{suffix}" }));
        Assert.Equal(CloudUserApplicationStatus.Approved, approved.Status);

        var firstLogin = await ReadOkAsync<LoginResponse>(await _factory.CreateClient().PostAsJsonAsync(
            "api/auth/login",
            new LoginRequest
            {
                Username = username,
                Password = password,
                DeviceId = $"{username}-device-a",
                DeviceName = "T7 Device A",
                ClientRequestId = $"login-a-{suffix}"
            }));
        Assert.Equal(admin.WorkspaceId, firstLogin.WorkspaceMembership.WorkspaceId);
        Assert.Equal(CloudRole.Employee, firstLogin.WorkspaceMembership.CloudRole);

        var employeeClient = CreateAuthenticatedClient(firstLogin.AccessToken);
        var me = await ReadOkAsync<AuthMeResponse>(await employeeClient.GetAsync("api/auth/me"));
        Assert.Equal(CloudRole.Employee, me.WorkspaceMembership.CloudRole);

        var secondDevice = await _factory.CreateClient().PostAsJsonAsync(
            "api/auth/login",
            new LoginRequest
            {
                Username = username,
                Password = password,
                DeviceId = $"{username}-device-b",
                DeviceName = "T7 Device B",
                ClientRequestId = $"login-b-{suffix}"
            });
        Assert.Equal(HttpStatusCode.Unauthorized, secondDevice.StatusCode);

        var pendingSecond = await FindDeviceAsync(adminClient, username, $"{username}-device-b", CloudDeviceStatus.Pending);
        var approveSecond = await adminClient.PostAsJsonAsync(
            $"api/users/devices/{pendingSecond.Id:N}/approve",
            new ReviewUserApplicationRequest { IdempotencyKey = $"approve-device-b-{suffix}" });
        Assert.Equal(HttpStatusCode.NoContent, approveSecond.StatusCode);

        var secondLogin = await ReadOkAsync<LoginResponse>(await _factory.CreateClient().PostAsJsonAsync(
            "api/auth/login",
            new LoginRequest
            {
                Username = username,
                Password = password,
                DeviceId = $"{username}-device-b",
                DeviceName = "T7 Device B",
                ClientRequestId = $"login-b-ok-{suffix}"
            }));

        var thirdDevice = await _factory.CreateClient().PostAsJsonAsync(
            "api/auth/login",
            new LoginRequest
            {
                Username = username,
                Password = password,
                DeviceId = $"{username}-device-c",
                DeviceName = "T7 Device C",
                ClientRequestId = $"login-c-{suffix}"
            });
        Assert.Equal(HttpStatusCode.Unauthorized, thirdDevice.StatusCode);

        var pendingThird = await FindDeviceAsync(adminClient, username, $"{username}-device-c", CloudDeviceStatus.Pending);
        var rejectThird = await adminClient.PostAsJsonAsync(
            $"api/users/devices/{pendingThird.Id:N}/reject",
            new ReviewUserApplicationRequest { IdempotencyKey = $"reject-device-c-{suffix}" });
        Assert.Equal(HttpStatusCode.NoContent, rejectThird.StatusCode);

        var rejectedLogin = await _factory.CreateClient().PostAsJsonAsync(
            "api/auth/login",
            new LoginRequest
            {
                Username = username,
                Password = password,
                DeviceId = $"{username}-device-c",
                DeviceName = "T7 Device C",
                ClientRequestId = $"login-c-rejected-{suffix}"
            });
        Assert.Equal(HttpStatusCode.Unauthorized, rejectedLogin.StatusCode);

        var revokeSecond = await adminClient.PostAsJsonAsync(
            $"api/users/devices/{pendingSecond.Id:N}/revoke",
            new ReviewUserApplicationRequest { IdempotencyKey = $"revoke-device-b-{suffix}" });
        Assert.Equal(HttpStatusCode.NoContent, revokeSecond.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await CreateAuthenticatedClient(secondLogin.AccessToken).GetAsync("api/auth/me")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await employeeClient.GetAsync("api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employeeClient.GetAsync($"api/workspaces/{admin.WorkspaceId:N}/cashflow/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employeeClient.PostAsJsonAsync(
            $"api/workspaces/{admin.WorkspaceId:N}/products",
            new CreateProductCommand
            {
                Name = "Forbidden Product",
                Code = $"FORB-{suffix}",
                ProductType = ProductType.Physical,
                DefaultPrice = 10,
                IdempotencyKey = $"forbidden-product-{suffix}"
            })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employeeClient.GetAsync($"api/workspaces/{Guid.NewGuid():N}/sync/changes?afterSequence=0")).StatusCode);

        var actions = await LoadAuditActionsAsync(admin.WorkspaceId);
        Assert.Contains("InvitationCreated", actions);
        Assert.Contains("UserApplicationSubmitted", actions);
        Assert.Contains("UserApplicationApproved", actions);
        Assert.Contains("WorkspaceMemberAuthorized", actions);
        Assert.Contains("DeviceApproved", actions);
        Assert.Contains("DeviceApprovalRequired", actions);
        Assert.Contains("DeviceRejected", actions);
        Assert.Contains("DeviceRevoked", actions);
        Assert.Contains("LoginFailed", actions);
    }

    [SkippableFact]
    public async Task Sync_smoke_covers_abc_clients_conflicts_idempotency_cursor_and_signalr()
    {
        var suffix = NewSuffix();
        var admin = await CreateAdminAsync($"t7-sync-admin-{suffix}");
        var clientA = CreateAuthenticatedClient(admin.Token);
        var clientB = CreateAuthenticatedClient(admin.Token);
        var clientC = CreateAuthenticatedClient(admin.Token);

        var signalReceived = new TaskCompletionSource<RealtimeEventPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var hub = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/workspace", options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(admin.Token);
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
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
        await hub.InvokeAsync("JoinWorkspace", admin.WorkspaceId);

        var created = await CreateProductAsync(clientA, admin.WorkspaceId, $"Sync Product {suffix}", $"SYNC-{suffix}");
        var signalPayload = await signalReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(created.Id, signalPayload.EntityId);
        Assert.True(signalPayload.Sequence > 0);
        Assert.Null(signalPayload.HintJson);
        Assert.DoesNotContain(created.Name, JsonSerializer.Serialize(signalPayload), StringComparison.Ordinal);

        var initialChanges = await ReadOkAsync<ChangesResponse>(await clientB.GetAsync($"api/workspaces/{admin.WorkspaceId:N}/sync/changes?afterSequence=0&maxCount=20"));
        Assert.Contains(initialChanges.Changes, change => change.EntityType == EntityType.Product && change.EntityId == created.Id);
        var offlineCursor = initialChanges.ToSequence;

        var updateOne = await UpdateProductAsync(
            clientA,
            admin.WorkspaceId,
            created.Id,
            new UpdateProductCommand
            {
                BaseVersion = created.Version,
                Name = $"Sync Product {suffix} A1",
                ChangedFields = new() { "Name" },
                IdempotencyKey = $"sync-update-one-{suffix}"
            });
        var updateTwo = await UpdateProductAsync(
            clientA,
            admin.WorkspaceId,
            created.Id,
            new UpdateProductCommand
            {
                BaseVersion = updateOne.Version,
                Description = "A2 description",
                ChangedFields = new() { "Description" },
                IdempotencyKey = $"sync-update-two-{suffix}"
            });

        var catchup = await ReadOkAsync<ChangesResponse>(await clientB.GetAsync($"api/workspaces/{admin.WorkspaceId:N}/sync/changes?afterSequence={offlineCursor}&maxCount=20"));
        Assert.Contains(catchup.Changes, change => change.EntityId == created.Id && change.Sequence > offlineCursor);
        Assert.All(catchup.Changes, change => Assert.True(change.Sequence > offlineCursor));
        Assert.True(catchup.ToSequence >= updateTwo.Version);

        var conflictBase = await CreateProductAsync(clientA, admin.WorkspaceId, $"Conflict Product {suffix}", $"CONF-{suffix}");
        await UpdateProductAsync(
            clientA,
            admin.WorkspaceId,
            conflictBase.Id,
            new UpdateProductCommand
            {
                BaseVersion = conflictBase.Version,
                Name = $"Conflict Product {suffix} A",
                ChangedFields = new() { "Name" },
                IdempotencyKey = $"same-field-a-{suffix}"
            });
        var sameFieldConflict = await clientB.PutAsJsonAsync(
            $"api/workspaces/{admin.WorkspaceId:N}/products/{conflictBase.Id:N}",
            new UpdateProductCommand
            {
                BaseVersion = conflictBase.Version,
                Name = $"Conflict Product {suffix} B",
                ChangedFields = new() { "Name" },
                IdempotencyKey = $"same-field-b-{suffix}"
            });
        Assert.Equal(HttpStatusCode.Conflict, sameFieldConflict.StatusCode);

        var mergeBase = await CreateProductAsync(clientA, admin.WorkspaceId, $"Merge Product {suffix}", $"MERGE-{suffix}");
        var mergeA = await UpdateProductAsync(
            clientA,
            admin.WorkspaceId,
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
            admin.WorkspaceId,
            mergeBase.Id,
            new UpdateProductCommand
            {
                BaseVersion = mergeBase.Version,
                Description = "B merged description",
                ChangedFields = new() { "Description" },
                IdempotencyKey = $"merge-b-{suffix}"
            });
        Assert.True(mergeB.Version > mergeA.Version);
        var merged = await ReadOkAsync<CloudProductDto>(await clientC.GetAsync($"api/workspaces/{admin.WorkspaceId:N}/products/{mergeBase.Id:N}"));
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
        var idemOne = await ReadOkAsync<CloudProductDto>(await clientA.PostAsJsonAsync($"api/workspaces/{admin.WorkspaceId:N}/products", idempotentCommand));
        var idemTwo = await ReadOkAsync<CloudProductDto>(await clientA.PostAsJsonAsync($"api/workspaces/{admin.WorkspaceId:N}/products", idempotentCommand));
        Assert.Equal(idemOne.Id, idemTwo.Id);
        Assert.Equal(1, await CountProductsByCodeAsync(admin.WorkspaceId, $"IDEM-{suffix}"));
        Assert.Equal(1, await CountIdempotencyRowsAsync(admin.WorkspaceId, admin.UserId, "product:create", $"idem-create-{suffix}"));

        Assert.Equal(HttpStatusCode.Forbidden, (await clientC.GetAsync($"api/workspaces/{Guid.NewGuid():N}/sync/changes?afterSequence=0")).StatusCode);
    }

    [SkippableFact]
    public async Task Attachment_lifecycle_and_permanent_delete_smoke_covers_authorization_and_audit()
    {
        var suffix = NewSuffix();
        var admin = await CreateAdminAsync($"t7-life-admin-{suffix}");
        var adminClient = CreateAuthenticatedClient(admin.Token);
        var employee = await CreateApprovedEmployeeAsync(admin, $"t7-life-employee-{suffix}");
        var employeeClient = CreateAuthenticatedClient(employee.Token);

        var adminCustomer = await CreateCustomerAsync(adminClient, admin.WorkspaceId, $"Attachment Customer {suffix}", $"130{suffix[..6]}");
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("attachment payload"));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        using var form = new MultipartFormDataContent
        {
            { content, "file", "payload.txt" },
            { new StringContent($"attach-{suffix}"), "clientRequestId" }
        };
        var uploadResponse = await adminClient.PostAsync($"api/workspaces/{admin.WorkspaceId:N}/lifecycle/attachments/customer/{adminCustomer.Id:N}", form);
        var attachmentJson = await uploadResponse.Content.ReadAsStringAsync();
        uploadResponse.EnsureSuccessStatusCode();
        Assert.DoesNotContain("blobKey", attachmentJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("url", attachmentJson, StringComparison.OrdinalIgnoreCase);
        var attachment = JsonSerializer.Deserialize<CloudAttachmentDto>(attachmentJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Attachment response is empty.");

        Assert.Equal(HttpStatusCode.Forbidden, (await employeeClient.GetAsync($"api/workspaces/{admin.WorkspaceId:N}/lifecycle/attachments/customer/{adminCustomer.Id:N}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employeeClient.GetAsync($"api/workspaces/{admin.WorkspaceId:N}/lifecycle/attachments/{attachment.Id:N}/download")).StatusCode);

        var downloadResponse = await adminClient.GetAsync($"api/workspaces/{admin.WorkspaceId:N}/lifecycle/attachments/{attachment.Id:N}/download");
        downloadResponse.EnsureSuccessStatusCode();
        Assert.Equal("attachment payload", await downloadResponse.Content.ReadAsStringAsync());

        var byteaColumns = await ExecuteScalarAsync<int>(
            @"SELECT COUNT(*)
              FROM information_schema.columns
              WHERE table_name = 'CloudAttachments' AND data_type = 'bytea';");
        Assert.Equal(0, byteaColumns);
        var metadata = await QuerySingleAsync<(string Sha256, long Version, long SizeBytes)>(
            @"SELECT ""Sha256"", ""Version"", ""SizeBytes"" FROM ""CloudAttachments"" WHERE ""Id"" = @id;",
            new { id = attachment.Id });
        Assert.False(string.IsNullOrWhiteSpace(metadata.Sha256));
        Assert.Equal(1, metadata.Version);
        Assert.Equal("attachment payload".Length, metadata.SizeBytes);

        var employeeCustomer = await CreateCustomerAsync(employeeClient, admin.WorkspaceId, $"Employee Customer {suffix}", $"131{suffix[..6]}");
        var archiveEmployeeCustomer = await employeeClient.PostAsJsonAsync(
            $"api/workspaces/{admin.WorkspaceId:N}/archive/customer/{employeeCustomer.Id:N}",
            new ArchiveCommand
            {
                BaseVersion = employeeCustomer.Version,
                ArchiveReason = "ordinary archive",
                IdempotencyKey = $"employee-archive-{suffix}"
            });
        archiveEmployeeCustomer.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await employeeClient.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"api/workspaces/{admin.WorkspaceId:N}/lifecycle/permanent/customer/{employeeCustomer.Id:N}")
        {
            Content = JsonContent.Create(new PermanentDeleteRequest { Confirm = true, Reason = "not allowed", ClientRequestId = $"employee-delete-{suffix}" })
        })).StatusCode);

        var tooEarlyDelete = await adminClient.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"api/workspaces/{admin.WorkspaceId:N}/lifecycle/permanent/customer/{employeeCustomer.Id:N}")
        {
            Content = JsonContent.Create(new PermanentDeleteRequest { Confirm = true, Reason = "too early", ClientRequestId = $"too-early-delete-{suffix}" })
        });
        Assert.Equal(HttpStatusCode.BadRequest, tooEarlyDelete.StatusCode);

        await ExecuteAsync(
            @"UPDATE ""CommerceCustomers"" SET ""DeletedAt"" = @deletedAt WHERE ""Id"" = @id;",
            new { deletedAt = DateTime.UtcNow.AddDays(-31), id = employeeCustomer.Id });
        var permanentDelete = await adminClient.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"api/workspaces/{admin.WorkspaceId:N}/lifecycle/permanent/customer/{employeeCustomer.Id:N}")
        {
            Content = JsonContent.Create(new PermanentDeleteRequest { Confirm = true, Reason = "retention satisfied", ClientRequestId = $"admin-delete-{suffix}" })
        });
        Assert.Equal(HttpStatusCode.NoContent, permanentDelete.StatusCode);

        var actions = await LoadAuditActionsAsync(admin.WorkspaceId);
        Assert.Contains("AttachmentUploaded", actions);
        Assert.Contains("AttachmentDownloaded", actions);
        Assert.Contains("Archived", actions);
        Assert.Contains("PermanentlyDeleted", actions);
    }

    [SkippableFact]
    public async Task Admin_ops_smoke_covers_health_backup_sync_sensitive_access_and_repair_audit()
    {
        var suffix = NewSuffix();
        var admin = await CreateAdminAsync($"t7-ops-admin-{suffix}");
        var adminClient = CreateAuthenticatedClient(admin.Token);
        var employee = await CreateApprovedEmployeeAsync(admin, $"t7-ops-employee-{suffix}");
        var employeeClient = CreateAuthenticatedClient(employee.Token);

        Assert.Equal(HttpStatusCode.OK, (await _factory.CreateClient().GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _factory.CreateClient().GetAsync("/health/db")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync($"api/workspaces/{admin.WorkspaceId:N}/admin/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync($"api/workspaces/{admin.WorkspaceId:N}/admin/backups")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync($"api/workspaces/{admin.WorkspaceId:N}/admin/sync-issues")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync($"api/workspaces/{admin.WorkspaceId:N}/admin/audit-logs?limit=10")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employeeClient.GetAsync($"api/workspaces/{admin.WorkspaceId:N}/admin/health")).StatusCode);

        var product = await CreateProductAsync(adminClient, admin.WorkspaceId, $"Ops Product {suffix}", $"OPS-{suffix}");
        var history = await ReadOkAsync<IReadOnlyList<CloudEntityVersionDto>>(await adminClient.GetAsync($"api/workspaces/{admin.WorkspaceId:N}/lifecycle/history/product/{product.Id:N}"));
        Assert.NotEmpty(history);

        var archive = await adminClient.PostAsJsonAsync(
            $"api/workspaces/{admin.WorkspaceId:N}/archive/product/{product.Id:N}",
            new ArchiveCommand
            {
                BaseVersion = product.Version,
                ArchiveReason = "repair drill",
                IdempotencyKey = $"ops-archive-{suffix}"
            });
        archive.EnsureSuccessStatusCode();
        var archived = await archive.Content.ReadFromJsonAsync<CloudProductDto>() ?? throw new InvalidOperationException("Archive response is empty.");

        var recover = await adminClient.PostAsJsonAsync(
            $"api/workspaces/{admin.WorkspaceId:N}/archive/product/{product.Id:N}/recover",
            new RecoverCommand
            {
                BaseVersion = archived.Version,
                Reason = "repair drill",
                IdempotencyKey = $"ops-recover-{suffix}"
            });
        recover.EnsureSuccessStatusCode();

        var stillWorks = await CreateProductAsync(adminClient, admin.WorkspaceId, $"Ops Product 2 {suffix}", $"OPS2-{suffix}");
        Assert.NotEqual(Guid.Empty, stillWorks.Id);

        var actions = await LoadAuditActionsAsync(admin.WorkspaceId);
        Assert.Contains("EntityHistoryViewed", actions);
        Assert.Contains("Recovered", actions);
        Assert.Contains("ProductCreated", actions);
    }

    private HttpClient CreateAuthenticatedClient(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<AdminSession> CreateAdminAsync(string username)
    {
        var passwordHasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        await using var connection = (System.Data.Common.DbConnection)await _factory.Services.GetRequiredService<PostgresConnectionFactory>().OpenConnectionAsync();
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await connection.ExecuteAsync(
            @"INSERT INTO ""CloudWorkspaces"" (""Id"", ""Name"", ""CreatedAt"", ""UpdatedAt"") VALUES (@workspaceId, @name, @now, @now);",
            new { workspaceId, name = $"T7 Workspace {username}", now });
        await connection.ExecuteAsync(
            @"INSERT INTO ""CloudWorkspaceSyncState"" (""WorkspaceId"", ""LastSequence"", ""UpdatedAt"") VALUES (@workspaceId, 0, @now);",
            new { workspaceId, now });
        await connection.ExecuteAsync(
            @"INSERT INTO ""CloudUsers"" (""Id"", ""Username"", ""DisplayName"", ""PasswordHash"", ""IsEnabled"", ""TokenVersion"", ""CreatedAt"", ""UpdatedAt"")
              VALUES (@userId, @username, @username, @hash, TRUE, 1, @now, @now);",
            new { userId, username, hash = passwordHasher.HashPassword("AdminT7Pwd!"), now });
        await connection.ExecuteAsync(
            @"INSERT INTO ""CloudWorkspaceMembers"" (""Id"", ""WorkspaceId"", ""UserId"", ""CloudRole"", ""BusinessLabel"", ""IsEnabled"", ""CreatedAt"", ""UpdatedAt"")
              VALUES (@id, @workspaceId, @userId, @role, @label, TRUE, @now, @now);",
            new { id = Guid.NewGuid(), workspaceId, userId, role = CloudRole.Admin, label = BusinessLabel.Operator, now });
        var deviceId = $"{username}-device";
        await connection.ExecuteAsync(
            @"INSERT INTO ""CloudDevices"" (
                ""Id"", ""WorkspaceId"", ""UserId"", ""DeviceId"", ""DeviceName"", ""Status"",
                ""FirstSeenAt"", ""LastSeenAt"", ""ApprovedByUserId"", ""ApprovedAt"", ""CreatedAt"", ""UpdatedAt"")
              VALUES (
                @id, @workspaceId, @userId, @deviceId, @deviceName, @status,
                @now, @now, @userId, @now, @now, @now);",
            new { id = Guid.NewGuid(), workspaceId, userId, deviceId, deviceName = $"{username} PC", status = CloudDeviceStatus.Approved, now });

        var jwt = _factory.Services.GetRequiredService<IJwtService>();
        return new AdminSession(jwt.GenerateAccessToken(userId, username, username, 1, deviceId), userId, workspaceId, deviceId);
    }

    private async Task<UserSession> CreateApprovedEmployeeAsync(AdminSession admin, string username)
    {
        var adminClient = CreateAuthenticatedClient(admin.Token);
        var user = await ReadOkAsync<CloudUserDto>(await adminClient.PostAsJsonAsync(
            "api/users",
            new CreateUserRequest
            {
                Username = username,
                DisplayName = username,
                CloudRole = CloudRole.Employee,
                BusinessLabel = BusinessLabel.Staff,
                InitialPassword = "EmployeeT7Pwd!",
                IdempotencyKey = $"create-{username}"
            }));

        var now = DateTime.UtcNow;
        var deviceId = $"{username}-device";
        await ExecuteAsync(
            @"INSERT INTO ""CloudDevices"" (
                ""Id"", ""WorkspaceId"", ""UserId"", ""DeviceId"", ""DeviceName"", ""Status"",
                ""FirstSeenAt"", ""LastSeenAt"", ""ApprovedByUserId"", ""ApprovedAt"", ""CreatedAt"", ""UpdatedAt"")
              VALUES (
                @id, @workspaceId, @userId, @deviceId, @deviceName, @status,
                @now, @now, @adminUserId, @now, @now, @now);",
            new { id = Guid.NewGuid(), workspaceId = admin.WorkspaceId, userId = user.Id, deviceId, deviceName = $"{username} PC", status = CloudDeviceStatus.Approved, adminUserId = admin.UserId, now });

        var tokenVersion = await ExecuteScalarAsync<int>(@"SELECT ""TokenVersion"" FROM ""CloudUsers"" WHERE ""Id"" = @userId;", new { userId = user.Id });
        var jwt = _factory.Services.GetRequiredService<IJwtService>();
        return new UserSession(jwt.GenerateAccessToken(user.Id, username, username, tokenVersion, deviceId), user.Id, admin.WorkspaceId, deviceId);
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
        await using var connection = (System.Data.Common.DbConnection)await _factory.Services.GetRequiredService<PostgresConnectionFactory>().OpenConnectionAsync();
        var actions = await connection.QueryAsync<string>(
            @"SELECT ""Action"" FROM ""CloudAuditLogs"" WHERE ""WorkspaceId"" = @workspaceId;",
            new { workspaceId });
        return actions.ToList();
    }

    private async Task<int> CountProductsByCodeAsync(Guid workspaceId, string code)
    {
        return await ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM ""CommerceProducts"" WHERE ""WorkspaceId"" = @workspaceId AND ""Code"" = @code;",
            new { workspaceId, code });
    }

    private async Task<int> CountIdempotencyRowsAsync(Guid workspaceId, Guid userId, string action, string clientRequestId)
    {
        return await ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM ""CloudIdempotencyKeys""
              WHERE ""WorkspaceId"" = @workspaceId AND ""UserId"" = @userId AND ""Action"" = @action AND ""ClientRequestId"" = @clientRequestId;",
            new { workspaceId, userId, action, clientRequestId });
    }

    private async Task ExecuteAsync(string sql, object? args = null)
    {
        await using var connection = (System.Data.Common.DbConnection)await _factory.Services.GetRequiredService<PostgresConnectionFactory>().OpenConnectionAsync();
        await connection.ExecuteAsync(sql, args);
    }

    private async Task<T> ExecuteScalarAsync<T>(string sql, object? args = null)
    {
        await using var connection = (System.Data.Common.DbConnection)await _factory.Services.GetRequiredService<PostgresConnectionFactory>().OpenConnectionAsync();
        return (await connection.ExecuteScalarAsync<T>(sql, args))!;
    }

    private async Task<T> QuerySingleAsync<T>(string sql, object? args = null)
    {
        await using var connection = (System.Data.Common.DbConnection)await _factory.Services.GetRequiredService<PostgresConnectionFactory>().OpenConnectionAsync();
        return await connection.QuerySingleAsync<T>(sql, args);
    }

    private static async Task<T> ReadOkAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>() ?? throw new InvalidOperationException("Response body is empty.");
    }

    private static string NewSuffix() => Guid.NewGuid().ToString("N")[..8];

    private sealed record AdminSession(string Token, Guid UserId, Guid WorkspaceId, string DeviceId);

    private sealed record UserSession(string Token, Guid UserId, Guid WorkspaceId, string DeviceId);

    private sealed class TestBlobStorage : IBlobStorage
    {
        private readonly Dictionary<string, byte[]> _objects = new(StringComparer.Ordinal);

        public bool IsEnabled => true;

        public async Task UploadAsync(string key, Stream stream, CancellationToken cancellationToken = default)
        {
            await using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            _objects[key] = buffer.ToArray();
        }

        public Task<Stream?> DownloadAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Stream?>(_objects.TryGetValue(key, out var bytes) ? new MemoryStream(bytes) : null);
        }

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            _objects.Remove(key);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<string>>(_objects.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToList());
        }
    }
}
