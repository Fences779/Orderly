using System.Data;
using System.Diagnostics;
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
        string? userAgent = null,
        string? result = null,
        string? correlationId = null)
    {
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        await LogAsync(connection, null, workspaceId, action, entityType, entityId, beforeJson, afterJson, reason, clientRequestId, ipAddress, userAgent, result, correlationId);
    }

    public async Task LogAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        Guid workspaceId,
        string action,
        string entityType,
        Guid? entityId,
        string? beforeJson,
        string? afterJson,
        string? reason = null,
        string? clientRequestId = null,
        string? ipAddress = null,
        string? userAgent = null,
        string? result = null,
        string? correlationId = null)
    {
        var actorId = _currentUser.UserId;
        var actorName = _currentUser.DisplayName ?? "system";
        var actorRole = _currentUser.Role ?? "system";
        var deviceId = _currentUser.DeviceId;
        var auditResult = string.IsNullOrWhiteSpace(result) ? "Succeeded" : result.Trim();
        var auditCorrelationId = string.IsNullOrWhiteSpace(correlationId)
            ? Activity.Current?.TraceId.ToString() ?? clientRequestId ?? Guid.NewGuid().ToString("N")
            : correlationId.Trim();

        const string sql = @"
            INSERT INTO ""CloudAuditLogs"" (
                ""Id"", ""WorkspaceId"", ""ActorUserId"", ""ActorDisplayName"", ""ActorRole"",
                ""Action"", ""EntityType"", ""EntityId"", ""BeforeJson"", ""AfterJson"",
                ""Reason"", ""ClientRequestId"", ""OccurredAt"", ""IpAddress"", ""UserAgent"",
                ""DeviceId"", ""Result"", ""CorrelationId"")
            VALUES (
                @id, @workspaceId, @actorId, @actorName, @actorRole,
                @action, @entityType, @entityId, @beforeJson, @afterJson,
                @reason, @clientRequestId, @occurredAt, @ipAddress, @userAgent,
                @deviceId, @auditResult, @auditCorrelationId);";

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
            userAgent,
            deviceId,
            auditResult,
            auditCorrelationId
        }, transaction);
    }
}
