using System.Net;
using System.Net.Http.Json;
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
public sealed class IdempotencyIntegrationTests : IDisposable
{
    private readonly PostgresFixture _fixture;
    private readonly ServerWebApplicationFactory _factory;

    public IdempotencyIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, "Docker is not available; skipping PostgreSQL integration tests.");
        _factory = new ServerWebApplicationFactory(fixture);
    }

    public void Dispose() => _factory.Dispose();

    [SkippableFact]
    public async Task Repeating_archive_with_same_client_request_id_replays_original_result()
    {
        var admin = await CreateAdminAsync("idem-admin", "Pwd!");
        var productId = await InsertProductAsync(admin.WorkspaceId, admin.UserId, "Idempotent Product");
        var client = CreateAuthenticatedClient(admin.Token);
        var clientRequestId = Guid.NewGuid().ToString("N");

        var command = new ArchiveCommand
        {
            ClientRequestId = clientRequestId,
            ExpectedRevision = 0L,
            ArchiveReason = "Test"
        };

        var first = await client.PostAsJsonAsync(
            $"api/workspaces/{admin.WorkspaceId:N}/archive/product/{productId:N}",
            command);
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync(
            $"api/workspaces/{admin.WorkspaceId:N}/archive/product/{productId:N}",
            command);
        second.EnsureSuccessStatusCode();

        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
    }

    [SkippableFact]
    public async Task Reusing_client_request_id_with_different_payload_returns_conflict()
    {
        var admin = await CreateAdminAsync("idem-admin2", "Pwd!");
        var productId = await InsertProductAsync(admin.WorkspaceId, admin.UserId, "Idempotent Product 2");
        var client = CreateAuthenticatedClient(admin.Token);
        var clientRequestId = Guid.NewGuid().ToString("N");

        var first = await client.PostAsJsonAsync(
            $"api/workspaces/{admin.WorkspaceId:N}/archive/product/{productId:N}",
            new ArchiveCommand { ClientRequestId = clientRequestId, ExpectedRevision = 0L, ArchiveReason = "First" });
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync(
            $"api/workspaces/{admin.WorkspaceId:N}/archive/product/{productId:N}",
            new ArchiveCommand { ClientRequestId = clientRequestId, ExpectedRevision = 0L, ArchiveReason = "Different" });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
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
}
