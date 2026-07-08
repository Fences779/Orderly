using System.Data;

namespace Orderly.Server.Services;

public interface IAuditLogService
{
    Task LogAsync(
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
        string? correlationId = null);

    Task LogAsync(
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
        string? correlationId = null);
}
