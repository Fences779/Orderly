using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Orderly.Contracts.Auth;
using Orderly.Contracts.Commerce;
using Orderly.Contracts.Permissions;
using Orderly.Core.Commerce;
using Orderly.Server.Data;
using Orderly.Server.Models;
using Orderly.Server.Services;
using Xunit;

namespace Orderly.Tests.Server.Integration;

[Collection("PostgresIntegration")]
public sealed class PriceChangeIntegrationTests : IDisposable
{
    private readonly PostgresFixture _fixture;
    private readonly ServerWebApplicationFactory _factory;

    public PriceChangeIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, "Docker is not available; skipping PostgreSQL integration tests.");
        _factory = new ServerWebApplicationFactory(fixture);
    }

    public void Dispose() => _factory.Dispose();

    [SkippableFact]
    public async Task Employee_submits_price_change_request_admin_approves_it()
    {
        var admin = await CreateAdminAsync("pc-admin", "Pwd!");
        var employee = await CreateEmployeeAsync(admin.Token, admin.WorkspaceId, "pc-employee");
        var productId = await InsertProductAsync(admin.WorkspaceId, admin.UserId, "PC Product", 100.00m);

        var employeeClient = CreateAuthenticatedClient(employee.Token);
        var createResponse = await employeeClient.PostAsJsonAsync(
            $"api/workspaces/{admin.WorkspaceId:N}/price-change-requests",
            new PriceChangeRequestCommand
            {
                ProductId = productId,
                ProposedPrice = 150.00m,
                ChangeReason = "促销"
            });
        createResponse.EnsureSuccessStatusCode();
        var requestDto = await createResponse.Content.ReadFromJsonAsync<CloudPriceChangeRequestDto>();

        Assert.Equal("Pending", requestDto!.Status);
        Assert.Equal(150.00m, requestDto.ProposedPrice);

        var adminClient = CreateAuthenticatedClient(admin.Token);
        var approveResponse = await adminClient.PostAsJsonAsync(
            $"api/workspaces/{admin.WorkspaceId:N}/price-change-requests/{requestDto.Id:N}/approve",
            new ReviewPriceChangeCommand { ExpectedRevision = 0L, ReviewNote = "同意" });
        approveResponse.EnsureSuccessStatusCode();

        var approved = await approveResponse.Content.ReadFromJsonAsync<CloudPriceChangeRequestDto>();
        Assert.Equal("Approved", approved!.Status);

        var price = await GetProductPriceAsync(productId);
        Assert.Equal(150.00m, price);
    }

    [SkippableFact]
    public async Task Employee_lists_only_own_price_change_requests()
    {
        var admin = await CreateAdminAsync("pc-admin2", "Pwd!");
        var employee1 = await CreateEmployeeAsync(admin.Token, admin.WorkspaceId, "pc-emp1");
        var employee2 = await CreateEmployeeAsync(admin.Token, admin.WorkspaceId, "pc-emp2");
        var productId = await InsertProductAsync(admin.WorkspaceId, admin.UserId, "PC Product 2", 100.00m);

        await SubmitRequestAsync(employee1.Token, admin.WorkspaceId, productId, 120.00m);
        await SubmitRequestAsync(employee2.Token, admin.WorkspaceId, productId, 130.00m);

        var client1 = CreateAuthenticatedClient(employee1.Token);
        var response = await client1.GetAsync($"api/workspaces/{admin.WorkspaceId:N}/price-change-requests");
        response.EnsureSuccessStatusCode();
        var list = await response.Content.ReadFromJsonAsync<PagedList<CloudPriceChangeRequestDto>>();

        Assert.Single(list!.Items);
        Assert.Equal(120.00m, list.Items[0].ProposedPrice);
    }

    private async Task SubmitRequestAsync(string token, Guid workspaceId, Guid productId, decimal price)
    {
        var client = CreateAuthenticatedClient(token);
        var response = await client.PostAsJsonAsync(
            $"api/workspaces/{workspaceId:N}/price-change-requests",
            new PriceChangeRequestCommand { ProductId = productId, ProposedPrice = price, ChangeReason = "test" });
        response.EnsureSuccessStatusCode();
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
        var response = await client.PostAsJsonAsync(
            "api/users",
            new CreateUserRequest
            {
                Username = username,
                DisplayName = username,
                CloudRole = CloudRole.Employee,
                BusinessLabel = BusinessLabel.Staff,
                InitialPassword = $"{username}Pwd!"
            });
        response.EnsureSuccessStatusCode();

        var user = await response.Content.ReadFromJsonAsync<CloudUserDto>();
        var jwt = _factory.Services.GetRequiredService<IJwtService>();
        var authService = _factory.Services.GetRequiredService<ICloudAuthService>();
        var fullUser = await authService.GetUserAsync(user!.Id);
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
            new { id = Guid.NewGuid(), workspaceId, userId = user.Id, deviceId, deviceName = $"{username} PC", status = CloudDeviceStatus.Approved, now });
        var token = jwt.GenerateAccessToken(user.Id, username, username, fullUser!.TokenVersion, deviceId);
        return (token, user.Id);
    }

    private async Task<Guid> InsertProductAsync(Guid workspaceId, Guid createdByUserId, string name, decimal price)
    {
        await using var connection = (System.Data.Common.DbConnection)await _factory.Services.GetRequiredService<PostgresConnectionFactory>().OpenConnectionAsync();
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await connection.ExecuteAsync(
            @"INSERT INTO ""CommerceProducts"" (
                ""Id"", ""WorkspaceId"", ""Name"", ""Code"", ""ProductType"", ""DefaultUnitId"",
                ""DefaultPrice"", ""DefaultCost"", ""Lifecycle"", ""CreatedByUserId"", ""CreatedAt"", ""UpdatedAt"", ""Revision"")
            VALUES (
                @id, @workspaceId, @name, @code, @productType, NULL,
                @price, 50.00, @active, @createdBy, @now, @now, 0);",
            new { id, workspaceId, name, code = id.ToString("N")[..8], productType = (int)ProductType.Physical, active = (int)EntityLifecycleStatus.Active, createdBy = createdByUserId, now, price });
        return id;
    }

    private async Task<decimal> GetProductPriceAsync(Guid productId)
    {
        await using var connection = (System.Data.Common.DbConnection)await _factory.Services.GetRequiredService<PostgresConnectionFactory>().OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<decimal>(
            @"SELECT ""DefaultPrice"" FROM ""CommerceProducts"" WHERE ""Id"" = @productId;",
            new { productId });
    }
}
