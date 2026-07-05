using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Orderly.Contracts.Auth;
using Orderly.Contracts.Commerce;
using Orderly.Contracts.Permissions;
using Orderly.Core.Commerce;
using Orderly.Server.Data;
using ProductType = Orderly.Core.Commerce.ProductType;
using Orderly.Server.Models;
using Orderly.Server.Services;
using Xunit;

namespace Orderly.Tests.Server.Integration;

[Collection("PostgresIntegration")]
public sealed class ArchiveIntegrationTests : IDisposable
{
    private readonly PostgresFixture _fixture;
    private readonly ServerWebApplicationFactory _factory;

    public ArchiveIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, "Docker is not available; skipping PostgreSQL integration tests.");
        _factory = new ServerWebApplicationFactory(fixture);
    }

    public void Dispose() => _factory.Dispose();

    [SkippableFact]
    public async Task Admin_can_archive_and_recover_a_product()
    {
        var admin = await CreateAdminAsync("archive-admin", "Pwd!");
        var productId = await InsertProductAsync(admin.WorkspaceId, admin.UserId, "Archive Test Product");
        var client = CreateAuthenticatedClient(admin.Token);

        var archiveResponse = await client.PostAsJsonAsync(
            $"api/workspaces/{admin.WorkspaceId:N}/archive/product/{productId:N}",
            new ArchiveCommand { ExpectedRevision = 0L, ArchiveReason = "Test archive" });
        archiveResponse.EnsureSuccessStatusCode();

        var archived = await GetProductLifecycleAsync(productId);
        Assert.Equal(EntityLifecycleStatus.Archived, archived);

        var recoverResponse = await client.PostAsJsonAsync(
            $"api/workspaces/{admin.WorkspaceId:N}/archive/product/{productId:N}/recover",
            new RecoverCommand { ExpectedRevision = 1L });
        recoverResponse.EnsureSuccessStatusCode();

        var recovered = await GetProductLifecycleAsync(productId);
        Assert.Equal(EntityLifecycleStatus.Active, recovered);
    }

    [SkippableFact]
    public async Task Employee_cannot_archive_others_product()
    {
        var admin = await CreateAdminAsync("archive-admin2", "Pwd!");
        var other = await CreateEmployeeAsync(admin.Token, admin.WorkspaceId, "other-product-owner");
        var productId = await InsertProductAsync(admin.WorkspaceId, other.UserId, "Owned By Employee");
        var employee = await CreateEmployeeAsync(admin.Token, admin.WorkspaceId, "archive-employee2");
        var client = CreateAuthenticatedClient(employee.Token);

        var response = await client.PostAsJsonAsync(
            $"api/workspaces/{admin.WorkspaceId:N}/archive/product/{productId:N}",
            new ArchiveCommand { ExpectedRevision = 0L, ArchiveReason = "Hacked" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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

        var jwt = _factory.Services.GetRequiredService<IJwtService>();
        var token = jwt.GenerateAccessToken(userId, username, username, 1);
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
        var token = jwt.GenerateAccessToken(user.Id, username, username, fullUser!.TokenVersion);
        return (token, user.Id);
    }

    private async Task<Guid> InsertProductAsync(Guid workspaceId, Guid createdByUserId, string name)
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
                100.00, 50.00, @active, @createdBy, @now, @now, 0);",
            new { id, workspaceId, name, code = id.ToString("N")[..8], productType = (int)ProductType.Physical, active = (int)EntityLifecycleStatus.Active, createdBy = createdByUserId, now });
        return id;
    }

    private async Task<EntityLifecycleStatus> GetProductLifecycleAsync(Guid productId)
    {
        await using var connection = (System.Data.Common.DbConnection)await _factory.Services.GetRequiredService<PostgresConnectionFactory>().OpenConnectionAsync();
        var lifecycle = await connection.ExecuteScalarAsync<int>(
            @"SELECT ""Lifecycle"" FROM ""CommerceProducts"" WHERE ""Id"" = @productId;",
            new { productId });
        return (EntityLifecycleStatus)lifecycle;
    }
}
