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
        string? userAgent = null);
}
