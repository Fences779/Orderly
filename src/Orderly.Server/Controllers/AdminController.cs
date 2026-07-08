using Dapper;
using Microsoft.AspNetCore.Mvc;
using Orderly.Server.Data;
using Orderly.Server.Models;
using Orderly.Server.Services;

namespace Orderly.Server.Controllers;

[Route("api/workspaces/{workspaceId:guid}/admin")]
public sealed class AdminController : CloudControllerBase
{
    private readonly PostgresConnectionFactory _connectionFactory;
    private readonly ServerOptions _options;

    public AdminController(
        PostgresConnectionFactory connectionFactory,
        ServerOptions options,
        ICurrentUserContext currentUser,
        ICloudAuthService authService,
        ICloudPermissionService permissions)
        : base(currentUser, authService, permissions)
    {
        _connectionFactory = connectionFactory;
        _options = options;
    }

    [HttpGet("health")]
    public async Task<IActionResult> GetHealthAsync(Guid workspaceId)
    {
        var membership = await RequireCloudAdminAsync(workspaceId);
        if (membership == null) return Forbid();

        var dbHealthy = true;
        string? dbError = null;
        long lastSequence = 0;
        int pendingDrafts = 0;
        int failedDrafts = 0;

        try
        {
            await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
            lastSequence = await connection.ExecuteScalarAsync<long>(
                @"SELECT COALESCE(""LastSequence"", 0) FROM ""CloudWorkspaceSyncState"" WHERE ""WorkspaceId"" = @workspaceId;",
                new { workspaceId });
            pendingDrafts = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM ""CloudEmergencyDrafts"" WHERE ""WorkspaceId"" = @workspaceId AND ""Status"" = 'Pending';",
                new { workspaceId });
            failedDrafts = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM ""CloudEmergencyDrafts"" WHERE ""WorkspaceId"" = @workspaceId AND ""Status"" = 'Failed';",
                new { workspaceId });
        }
        catch (Exception ex)
        {
            dbHealthy = false;
            dbError = ex.Message;
        }

        var backup = BuildBackupSnapshot();
        return Ok(new
        {
            Status = dbHealthy && backup.Status == "Healthy" ? "Healthy" : "AttentionRequired",
            Database = new { Healthy = dbHealthy, Error = dbError },
            Sync = new { LastSequence = lastSequence, PendingDrafts = pendingDrafts, FailedDrafts = failedDrafts },
            Backup = backup.Payload,
            TimeUtc = DateTime.UtcNow
        });
    }

    [HttpGet("backups")]
    public async Task<IActionResult> GetBackupsAsync(Guid workspaceId)
    {
        var membership = await RequireCloudAdminAsync(workspaceId);
        if (membership == null) return Forbid();

        return Ok(BuildBackupSnapshot().Payload);
    }

    [HttpGet("audit-logs")]
    public async Task<IActionResult> ListAuditLogsAsync(
        Guid workspaceId,
        [FromQuery] string? action,
        [FromQuery] string? entityType,
        [FromQuery] Guid? entityId,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] int limit = 100)
    {
        var membership = await RequireCloudAdminAsync(workspaceId);
        if (membership == null) return Forbid();

        limit = Math.Clamp(limit, 1, 500);
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        var rows = await connection.QueryAsync(
            @"SELECT ""Id"", ""WorkspaceId"", ""ActorUserId"", ""ActorDisplayName"", ""ActorRole"",
                     ""Action"", ""EntityType"", ""EntityId"", ""Reason"", ""ClientRequestId"",
                     ""OccurredAt"" AS ""OccurredAtUtc"", ""IpAddress"", ""UserAgent""
              FROM ""CloudAuditLogs""
              WHERE ""WorkspaceId"" = @workspaceId
                AND (@action IS NULL OR ""Action"" = @action)
                AND (@entityType IS NULL OR ""EntityType"" = @entityType)
                AND (@entityId IS NULL OR ""EntityId"" = @entityId)
                AND (@fromUtc IS NULL OR ""OccurredAt"" >= @fromUtc)
                AND (@toUtc IS NULL OR ""OccurredAt"" <= @toUtc)
              ORDER BY ""OccurredAt"" DESC
              LIMIT @limit;",
            new { workspaceId, action, entityType, entityId, fromUtc, toUtc, limit });
        return Ok(rows);
    }

    [HttpGet("sync-issues")]
    public async Task<IActionResult> ListSyncIssuesAsync(Guid workspaceId, [FromQuery] int limit = 100)
    {
        var membership = await RequireCloudAdminAsync(workspaceId);
        if (membership == null) return Forbid();

        limit = Math.Clamp(limit, 1, 500);
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        var failedDrafts = await connection.QueryAsync(
            @"SELECT ""Id"", ""EntityType"", ""EntityId"", ""OperationType"", ""BaseRevision"",
                     ""Status"", ""LastSubmitError"", ""CreatedAt"" AS ""CreatedAtUtc"", ""SubmittedAt"" AS ""SubmittedAtUtc""
              FROM ""CloudEmergencyDrafts""
              WHERE ""WorkspaceId"" = @workspaceId AND ""Status"" IN ('Pending', 'Failed')
              ORDER BY ""CreatedAt"" DESC
              LIMIT @limit;",
            new { workspaceId, limit });
        var recentRejections = await connection.QueryAsync(
            @"SELECT ""Id"", ""Username"", ""Reason"", ""ClientRequestId"", ""OccurredAt"" AS ""OccurredAtUtc""
              FROM ""CloudLoginFailures""
              WHERE ""WorkspaceId"" = @workspaceId OR ""WorkspaceId"" IS NULL
              ORDER BY ""OccurredAt"" DESC
              LIMIT @limit;",
            new { workspaceId, limit });
        var sequence = await connection.QueryFirstOrDefaultAsync(
            @"SELECT s.""LastSequence"",
                     (SELECT MAX(""Sequence"") FROM ""CloudChangeLog"" c WHERE c.""WorkspaceId"" = @workspaceId) AS ""LatestChangeSequence""
              FROM ""CloudWorkspaceSyncState"" s
              WHERE s.""WorkspaceId"" = @workspaceId;",
            new { workspaceId });

        return Ok(new
        {
            Sequence = sequence,
            PendingOrFailedDrafts = failedDrafts,
            RecentLoginRejections = recentRejections
        });
    }

    private async Task<CloudWorkspaceMemberRecord?> RequireCloudAdminAsync(Guid workspaceId)
    {
        var membership = await GetMembershipAsync();
        if (membership.WorkspaceId != workspaceId) return null;
        return Permissions.CanManageUsers(membership) ? membership : null;
    }

    private (string Status, object Payload) BuildBackupSnapshot()
    {
        var health = BackupHealthState.Load(_options);
        var latestDump = Directory.Exists(_options.LocalBackupDirectory)
            ? Directory.EnumerateFiles(_options.LocalBackupDirectory, "*.dump", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault()
            : null;

        var status = latestDump is null
            ? "NoLocalBackup"
            : health.LastRestoreDrillStatus == "Failed"
                ? "RestoreDrillFailed"
                : "Healthy";

        var payload = new
        {
            Status = status,
            LatestLocalBackup = latestDump is null ? null : new
            {
                latestDump.Name,
                latestDump.FullName,
                latestDump.Length,
                LastWriteTimeUtc = latestDump.LastWriteTimeUtc
            },
            RestoreDrill = new
            {
                Enabled = _options.RestoreDrillEnabled,
                IntervalHours = _options.RestoreDrillIntervalHours,
                health.LastRestoreDrillAtUtc,
                health.LastRestoreDrillStatus,
                health.LastRestoreDrillError
            },
            Health = health
        };
        return (status, payload);
    }
}
