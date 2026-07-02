using System.Data;
using Dapper;
using Orderly.Contracts.Commerce;
using Orderly.Contracts.Realtime;

namespace Orderly.Server.Services;

public partial class CommerceCommandService
{
    public async Task BeginEditingAsync(Guid workspaceId, EditingPresenceCommand command, string connectionId, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");
        var displayName = _currentUser.DisplayName ?? string.Empty;
        var now = DateTime.UtcNow;

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(
            @"INSERT INTO ""CloudEditPresences"" (
                ""WorkspaceId"", ""EntityType"", ""EntityId"", ""UserId"", ""DisplayName"",
                ""ConnectionId"", ""StartedAt"", ""LastHeartbeatAt"", ""ExpiresAt"")
            VALUES (
                @workspaceId, @entityType, @entityId, @userId, @displayName,
                @connectionId, @now, @now, @expiresAt)
            ON CONFLICT (""WorkspaceId"", ""EntityType"", ""EntityId"", ""UserId"")
            DO UPDATE SET
                ""ConnectionId"" = EXCLUDED.""ConnectionId"",
                ""LastHeartbeatAt"" = EXCLUDED.""LastHeartbeatAt"",
                ""ExpiresAt"" = EXCLUDED.""ExpiresAt"";",
            new
            {
                workspaceId,
                command.EntityType,
                command.EntityId,
                userId,
                displayName,
                connectionId,
                now,
                expiresAt = now.AddMinutes(5)
            });

        await NotifyPresenceChangedAsync(workspaceId, command.EntityType, command.EntityId, connectionId, "begin");
    }

    public async Task EndEditingAsync(Guid workspaceId, EditingPresenceCommand command, string connectionId, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(
            @"DELETE FROM ""CloudEditPresences""
             WHERE ""WorkspaceId"" = @workspaceId
               AND ""EntityType"" = @entityType
               AND ""EntityId"" = @entityId
               AND ""UserId"" = @userId;",
            new { workspaceId, command.EntityType, command.EntityId, userId });

        await NotifyPresenceChangedAsync(workspaceId, command.EntityType, command.EntityId, connectionId, "end");
    }

    private async Task NotifyPresenceChangedAsync(Guid workspaceId, string entityType, Guid entityId, string connectionId, string action)
    {
        await _notifier.NotifyAsync(workspaceId, RealtimeEvent.EditingPresenceChanged, new RealtimeEventPayload
        {
            WorkspaceId = workspaceId,
            EntityType = entityType,
            EntityId = entityId,
            ActorUserId = _currentUser.UserId,
            ActorDisplayName = _currentUser.DisplayName ?? string.Empty,
            OccurredAtUtc = DateTime.UtcNow,
            Action = action,
            HintJson = $"{{\"connectionId\":\"{connectionId}\"}}"
        });
    }
}
