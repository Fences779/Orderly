using Dapper;
using Orderly.Server.Data;

namespace Orderly.Server.Services;

public sealed class AuditLogService : IAuditLogService
{
    private readonly PostgresConnectionFactory _connectionFactory;
    private readonly ICurrentUserContext _currentUser;

    public AuditLogService(PostgresConnectionFactory connectionFactory, ICurrentUserContext currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task LogAsync(
        Guid workspaceId,
        string action,
        string entityType,
        Guid? entityId,
        string? beforeJson,
        string? afterJson,
        string? reason = null,
        string? clientRequestId = null,
        string? ipAddress = null,
        string? userAgent = null)
    {
        var actorId = _currentUser.UserId;
        var actorName = _currentUser.DisplayName ?? "system";
        var actorRole = "system";

        const string sql = @"
            INSERT INTO ""CloudAuditLogs"" (
                ""Id"", ""WorkspaceId"", ""ActorUserId"", ""ActorDisplayName"", ""ActorRole"",
                ""Action"", ""EntityType"", ""EntityId"", ""BeforeJson"", ""AfterJson"",
                ""Reason"", ""ClientRequestId"", ""OccurredAt"", ""IpAddress"", ""UserAgent"")
            VALUES (
                @id, @workspaceId, @actorId, @actorName, @actorRole,
                @action, @entityType, @entityId, @beforeJson, @afterJson,
                @reason, @clientRequestId, @occurredAt, @ipAddress, @userAgent);";

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        await connection.ExecuteAsync(sql, new
        {
            id = Guid.NewGuid(),
            workspaceId,
            actorId,
            actorName,
            actorRole,
            action,
            entityType,
            entityId,
            beforeJson,
            afterJson,
            reason,
            clientRequestId,
            occurredAt = DateTime.UtcNow,
            ipAddress,
            userAgent
        });
    }
}
