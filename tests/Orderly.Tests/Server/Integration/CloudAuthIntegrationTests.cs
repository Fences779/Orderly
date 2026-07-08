using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Orderly.Contracts.Auth;
using Orderly.Contracts.Permissions;
using Orderly.Server.Data;
using Orderly.Server.Models;
using Orderly.Server.Services;
using Xunit;

namespace Orderly.Tests.Server.Integration;

[Collection("PostgresIntegration")]
public sealed class CloudAuthIntegrationTests : IDisposable
{
    private readonly PostgresFixture _fixture;
    private readonly ServerWebApplicationFactory _factory;

    public CloudAuthIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, "Docker is not available; skipping PostgreSQL integration tests.");
        _factory = new ServerWebApplicationFactory(fixture);
    }

    public void Dispose() => _factory.Dispose();

    [SkippableFact]
    public async Task Admin_can_create_employee_and_reset_employee_password()
    {
        var admin = await CreateAdminAsync("admin1", "Admin1Pwd!");
        var client = CreateAuthenticatedClient(admin.Token);

        var createResponse = await client.PostAsJsonAsync(
            "api/users",
            new CreateUserRequest
            {
                Username = "employee1",
                DisplayName = "Employee One",
                CloudRole = CloudRole.Employee,
                BusinessLabel = BusinessLabel.Staff,
                InitialPassword = "Employee1Pwd!"
            });
        createResponse.EnsureSuccessStatusCode();

        var resetResponse = await client.PostAsJsonAsync(
            "api/auth/reset-password",
            new ResetPasswordRequest
            {
                UserId = await ExtractUserIdAsync(client, "employee1"),
                NewPassword = "NewEmployee1Pwd!"
            });
        Assert.Equal(HttpStatusCode.NoContent, resetResponse.StatusCode);

        var loginResponse = await _factory.CreateClient().PostAsJsonAsync(
            "api/auth/login",
            new LoginRequest { Username = "employee1", Password = "NewEmployee1Pwd!", DeviceId = "employee1-device", DeviceName = "Employee One PC" });
        loginResponse.EnsureSuccessStatusCode();
    }

    [SkippableFact]
    public async Task Employee_cannot_access_export_endpoint()
    {
        var admin = await CreateAdminAsync("admin2", "Admin2Pwd!");
        var employee = await CreateEmployeeAsync(admin.Token, admin.WorkspaceId, "employee2");
        var client = CreateAuthenticatedClient(employee.Token);

        var response = await client.PostAsync(
            $"api/workspaces/{admin.WorkspaceId:N}/exports/business-package", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [SkippableFact]
    public async Task Employee_cannot_reset_other_users_password()
    {
        var admin = await CreateAdminAsync("admin3", "Admin3Pwd!");
        var employee = await CreateEmployeeAsync(admin.Token, admin.WorkspaceId, "employee3");
        var other = await CreateEmployeeAsync(admin.Token, admin.WorkspaceId, "other3");
        var client = CreateAuthenticatedClient(employee.Token);

        var response = await client.PostAsJsonAsync(
            "api/auth/reset-password",
            new ResetPasswordRequest
            {
                UserId = other.UserId,
                NewPassword = "HackedPwd!"
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [SkippableFact]
    public async Task Invitation_application_and_device_approval_gate_cloud_login()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var admin = await CreateAdminAsync($"invite-admin-{suffix}", "AdminInvitePwd!");
        var adminClient = CreateAuthenticatedClient(admin.Token);
        var inviteCode = $"INV-{suffix}";

        var invitationResponse = await adminClient.PostAsJsonAsync(
            "api/users/invitations",
            new CreateInvitationRequest
            {
                Code = inviteCode,
                CloudRole = CloudRole.Employee,
                BusinessLabel = BusinessLabel.Staff,
                MaxUses = 1
            });
        invitationResponse.EnsureSuccessStatusCode();

        var username = $"invite-employee-{suffix}";
        var password = "InviteEmployeePwd!";
        var applicationResponse = await _factory.CreateClient().PostAsJsonAsync(
            "api/auth/applications",
            new SubmitUserApplicationRequest
            {
                InviteCode = inviteCode,
                Username = username,
                DisplayName = "Invite Employee",
                InitialPassword = password,
                DeviceId = $"{username}-device-a",
                DeviceName = "Invite Employee PC A"
            });
        applicationResponse.EnsureSuccessStatusCode();
        var application = await applicationResponse.Content.ReadFromJsonAsync<CloudUserApplicationDto>();
        Assert.Equal(CloudUserApplicationStatus.Pending, application!.Status);

        var approveResponse = await adminClient.PostAsJsonAsync(
            $"api/users/applications/{application.Id:N}/approve",
            new ReviewUserApplicationRequest { Reason = "verified" });
        approveResponse.EnsureSuccessStatusCode();
        var approved = await approveResponse.Content.ReadFromJsonAsync<CloudUserApplicationDto>();
        Assert.Equal(CloudUserApplicationStatus.Approved, approved!.Status);

        var firstDeviceLogin = await _factory.CreateClient().PostAsJsonAsync(
            "api/auth/login",
            new LoginRequest
            {
                Username = username,
                Password = password,
                DeviceId = $"{username}-device-a",
                DeviceName = "Invite Employee PC A"
            });
        firstDeviceLogin.EnsureSuccessStatusCode();

        var secondDeviceLogin = await _factory.CreateClient().PostAsJsonAsync(
            "api/auth/login",
            new LoginRequest
            {
                Username = username,
                Password = password,
                DeviceId = $"{username}-device-b",
                DeviceName = "Invite Employee PC B"
            });
        Assert.Equal(HttpStatusCode.Unauthorized, secondDeviceLogin.StatusCode);

        var devicesResponse = await adminClient.GetAsync("api/users/devices");
        devicesResponse.EnsureSuccessStatusCode();
        var devices = await devicesResponse.Content.ReadFromJsonAsync<List<CloudDeviceDto>>();
        var pendingDevice = devices!.Single(device =>
            device.Username == username
            && device.DeviceId == $"{username}-device-b"
            && device.Status == CloudDeviceStatus.Pending);

        var approveDeviceResponse = await adminClient.PostAsJsonAsync(
            $"api/users/devices/{pendingDevice.Id:N}/approve",
            new ReviewUserApplicationRequest { Reason = "second device verified" });
        Assert.Equal(HttpStatusCode.NoContent, approveDeviceResponse.StatusCode);

        var secondDeviceLoginAfterApproval = await _factory.CreateClient().PostAsJsonAsync(
            "api/auth/login",
            new LoginRequest
            {
                Username = username,
                Password = password,
                DeviceId = $"{username}-device-b",
                DeviceName = "Invite Employee PC B"
            });
        secondDeviceLoginAfterApproval.EnsureSuccessStatusCode();
    }

    private HttpClient CreateAuthenticatedClient(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<(string Token, Guid UserId, Guid WorkspaceId)> CreateAdminAsync(string username, string password)
    {
        var passwordHasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        await using var connection = (System.Data.Common.DbConnection)await _factory.Services.GetRequiredService<PostgresConnectionFactory>().OpenConnectionAsync();
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await connection.ExecuteAsync(
            @"INSERT INTO ""CloudWorkspaces"" (""Id"", ""Name"", ""CreatedAt"", ""UpdatedAt"") VALUES (@workspaceId, 'Test', @now, @now);",
            new { workspaceId, now });
        await connection.ExecuteAsync(
            @"INSERT INTO ""CloudUsers"" (""Id"", ""Username"", ""DisplayName"", ""PasswordHash"", ""IsEnabled"", ""TokenVersion"", ""CreatedAt"", ""UpdatedAt"") 
            VALUES (@userId, @username, @username, @hash, TRUE, 1, @now, @now);",
            new { userId, username, hash = passwordHasher.HashPassword(password), now });
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
        var token = jwt.GenerateAccessToken(userId, username, username, 1, deviceId);
        return (token, userId, workspaceId);
    }

    private async Task<(string Token, Guid UserId)> CreateEmployeeAsync(string adminToken, Guid workspaceId, string username)
    {
        var client = CreateAuthenticatedClient(adminToken);
        var createResponse = await client.PostAsJsonAsync(
            "api/users",
            new CreateUserRequest
            {
                Username = username,
                DisplayName = username,
                CloudRole = CloudRole.Employee,
                BusinessLabel = BusinessLabel.Staff,
                InitialPassword = $"{username}Pwd!"
            });
        createResponse.EnsureSuccessStatusCode();

        var userId = await ExtractUserIdAsync(client, username);
        using var scope = _factory.Services.CreateScope();
        var authService = scope.ServiceProvider.GetRequiredService<ICloudAuthService>();
        var user = await authService.GetUserAsync(userId);
        var jwt = _factory.Services.GetRequiredService<IJwtService>();
        var deviceId = $"{username}-device";
        await using var connection = (System.Data.Common.DbConnection)await _factory.Services.GetRequiredService<PostgresConnectionFactory>().OpenConnectionAsync();
        var now = DateTime.UtcNow;
        await connection.ExecuteAsync(
            @"INSERT INTO ""CloudDevices"" (
                ""Id"", ""WorkspaceId"", ""UserId"", ""DeviceId"", ""DeviceName"", ""Status"",
                ""FirstSeenAt"", ""LastSeenAt"", ""ApprovedByUserId"", ""ApprovedAt"", ""CreatedAt"", ""UpdatedAt"")
              VALUES (
                @id, @workspaceId, @userId, @deviceId, @deviceName, @status,
                @now, @now, @userId, @now, @now, @now);",
            new { id = Guid.NewGuid(), workspaceId, userId, deviceId, deviceName = $"{username} PC", status = CloudDeviceStatus.Approved, now });
        var token = jwt.GenerateAccessToken(userId, username, username, user!.TokenVersion, deviceId);
        return (token, userId);
    }

    private async Task<Guid> ExtractUserIdAsync(HttpClient client, string username)
    {
        var response = await client.GetAsync("api/users");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            if (element.GetProperty("username").GetString() == username)
            {
                return Guid.Parse(element.GetProperty("id").GetString()!);
            }
        }

        throw new InvalidOperationException($"User {username} not found.");
    }
}
