using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Npgsql;
using Orderly.Contracts.Commerce;
using Orderly.Contracts.Permissions;
using Orderly.Contracts.Realtime;
using Orderly.Core.Commerce;
using Orderly.Server.Data;
using Orderly.Server.Mapping;
using Orderly.Server.Models;

namespace Orderly.Server.Services;

public partial class CommerceCommandService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly PostgresConnectionFactory _connectionFactory;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICloudAuthService _authService;
    private readonly ICloudPermissionService _permissions;
    private readonly IWorkspaceSyncService _syncService;
    private readonly IIdempotencyService _idempotency;
    private readonly IAuditLogService _auditLog;
    private readonly ISignalRNotifier _notifier;

    public CommerceCommandService(
        PostgresConnectionFactory connectionFactory,
        ICurrentUserContext currentUser,
        ICloudAuthService authService,
        ICloudPermissionService permissions,
        IWorkspaceSyncService syncService,
        IIdempotencyService idempotency,
        IAuditLogService auditLog,
        ISignalRNotifier notifier)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
        _authService = authService;
        _permissions = permissions;
        _syncService = syncService;
        _idempotency = idempotency;
        _auditLog = auditLog;
        _notifier = notifier;
    }

    private static string ComputeRequestHash<T>(T request)
    {
        var json = JsonSerializer.Serialize(request, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private async Task<CloudWorkspaceMemberRecord> GetMembershipAsync(Guid userId)
    {
        var membership = await _authService.GetMembershipAsync(userId);
        if (membership == null || !membership.IsEnabled)
            throw new UnauthorizedAccessException("Membership is not valid.");
        return membership;
    }

    private static NpgsqlConnection GetNpgsqlConnection(System.Data.Common.DbConnection connection)
        => (NpgsqlConnection)connection;

    private static async Task<NpgsqlTransaction> BeginTransactionAsync(System.Data.Common.DbConnection connection, CancellationToken cancellationToken)
        => await GetNpgsqlConnection(connection).BeginTransactionAsync(cancellationToken);

    private async Task<long> AllocateSequenceAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid workspaceId)
        => await _syncService.AllocateSequenceAsync(connection, transaction, workspaceId);

    private async Task RecordChangeAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid workspaceId,
        long sequence,
        string entityType,
        Guid entityId,
        string action,
        long revision)
        => await _syncService.RecordChangeAsync(connection, transaction, workspaceId, sequence, entityType, entityId, action, revision, _currentUser.UserId, null);

    private async Task AuditAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid workspaceId,
        string action,
        string entityType,
        Guid entityId,
        string? beforeJson,
        string? afterJson,
        string? reason,
        string? clientRequestId,
        NotificationCollector? collector = null)
    {
        await _auditLog.LogAsync(connection, transaction, workspaceId, action, entityType, entityId, beforeJson, afterJson, reason, clientRequestId, null, null);

        collector?.Add(RealtimeEvent.AuditLogCreated, new RealtimeEventPayload
        {
            WorkspaceId = workspaceId,
            EntityType = entityType,
            EntityId = entityId,
            Sequence = null,
            ActorUserId = _currentUser.UserId,
            ActorDisplayName = _currentUser.DisplayName ?? string.Empty,
            OccurredAtUtc = DateTime.UtcNow,
            Action = action,
            HintJson = reason
        });
    }

    private async Task<CommandResult<TDto>> ExecuteWithIdempotencyAsync<TCommand, TDto>(
        Guid workspaceId,
        string action,
        TCommand command,
        Func<System.Data.Common.DbConnection, NpgsqlTransaction, long, CancellationToken, Task<(TDto Dto, string EntityType, Guid EntityId)>> execute,
        CancellationToken cancellationToken)
        where TDto : notnull
    {
        return await ExecuteWithIdempotencyAsync<TCommand, TDto>(
            workspaceId,
            action,
            command,
            (connection, transaction, sequence, collector, ct) => execute(connection, transaction, sequence, ct),
            cancellationToken);
    }

    private async Task<CommandResult<TDto>> ExecuteWithIdempotencyAsync<TCommand, TDto>(
        Guid workspaceId,
        string action,
        TCommand command,
        Func<System.Data.Common.DbConnection, NpgsqlTransaction, long, NotificationCollector, CancellationToken, Task<(TDto Dto, string EntityType, Guid EntityId)>> execute,
        CancellationToken cancellationToken)
        where TDto : notnull
    {
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");
        var requestHash = ComputeRequestHash(command);
        var clientRequestId = GetClientRequestId(command);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);
                await using var transaction = await BeginTransactionAsync(connection, cancellationToken);

                var beginResult = await _idempotency.TryBeginAsync(workspaceId, userId, action, clientRequestId, requestHash, connection, transaction, cancellationToken);
                if (!beginResult.ShouldExecute)
                {
                    await transaction.CommitAsync(cancellationToken);
                    if (string.IsNullOrEmpty(beginResult.ResponseBodyJson))
                        throw new InvalidOperationException("Idempotency replay missing response body.");

                    var replayDto = JsonSerializer.Deserialize<TDto>(beginResult.ResponseBodyJson!, JsonOptions)
                        ?? throw new InvalidOperationException("Idempotency replay could not be deserialized.");
                    return new CommandResult<TDto>(replayDto, true);
                }

                var sequence = await AllocateSequenceAsync(connection, transaction, workspaceId);
                var collector = new NotificationCollector();
                var (dto, entityType, entityId) = await execute(connection, transaction, sequence, collector, cancellationToken);
                var responseJson = JsonSerializer.Serialize(dto, JsonOptions);
                if (dto is CloudEntityDto entityDto)
                {
                    await CloudDataLifecycleService.RecordEntityVersionAsync(
                        connection,
                        transaction,
                        workspaceId,
                        entityType,
                        entityId,
                        entityDto.Revision,
                        action,
                        responseJson,
                        userId);
                }

                await _idempotency.CompleteAsync(workspaceId, userId, action, clientRequestId, 200, responseJson, entityType, entityId, connection, transaction, cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                foreach (var (eventName, payload) in collector.Payloads)
                {
                    await _notifier.NotifyAsync(payload.WorkspaceId, eventName, payload);
                }

                await BroadcastAsync(workspaceId, entityType, entityId, dto, sequence);
                return new CommandResult<TDto>(dto, false);
            }
            catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure)
            {
                if (attempt == 2) throw;
                await Task.Delay(TimeSpan.FromMilliseconds(50 * (attempt + 1)), cancellationToken);
            }
        }

        throw new InvalidOperationException("命令执行失败，已超出最大重试次数。");
    }

    private sealed class NotificationCollector
    {
        private readonly List<(string EventName, RealtimeEventPayload Payload)> _payloads = new();
        public IReadOnlyList<(string EventName, RealtimeEventPayload Payload)> Payloads => _payloads;
        public void Add(string eventName, RealtimeEventPayload payload) => _payloads.Add((eventName, payload));
    }

    private async Task BroadcastAsync<TDto>(Guid workspaceId, string entityType, Guid entityId, TDto dto, long sequence)
        where TDto : notnull
    {
        var action = dto switch
        {
            CloudOrderDto order => order.Lifecycle == EntityLifecycleStatus.Archived ? "archived" : "updated",
            CloudCustomerDto customer => customer.Lifecycle == EntityLifecycleStatus.Archived ? "archived" : "updated",
            CloudCashFlowEntryDto cashflow => cashflow.Lifecycle == EntityLifecycleStatus.Archived ? "archived" : "updated",
            CloudInventoryMovementDto => "created",
            CloudPriceChangeRequestDto request => request.Status switch
            {
                "Pending" => "priceChangeRequestCreated",
                "Approved" => "approved",
                "Rejected" => "rejected",
                _ => "updated"
            },
            _ => "updated"
        };

        var eventName = action switch
        {
            "archived" => RealtimeEvent.EntityArchived,
            "recovered" => RealtimeEvent.EntityRecovered,
            "created" => RealtimeEvent.EntityCreated,
            "priceChangeRequestCreated" => RealtimeEvent.PriceChangeRequestCreated,
            "approved" or "rejected" => RealtimeEvent.PriceChangeRequestReviewed,
            _ => RealtimeEvent.EntityUpdated
        };

        var revision = dto is CloudEntityDto entity ? entity.Revision : (long?)null;

        await _notifier.NotifyAsync(workspaceId, eventName, new RealtimeEventPayload
        {
            WorkspaceId = workspaceId,
            EntityType = entityType,
            EntityId = entityId,
            Revision = revision,
            Sequence = sequence,
            ActorUserId = _currentUser.UserId,
            ActorDisplayName = _currentUser.DisplayName ?? string.Empty,
            OccurredAtUtc = DateTime.UtcNow,
            Action = action
        });

        if (entityType == EntityType.InventoryItem || entityType == EntityType.Order)
        {
            await _notifier.NotifyAsync(workspaceId, RealtimeEvent.DashboardInvalidated, new RealtimeEventPayload
            {
                WorkspaceId = workspaceId,
                EntityType = "dashboard",
                ActorUserId = _currentUser.UserId,
                ActorDisplayName = _currentUser.DisplayName ?? string.Empty,
                OccurredAtUtc = DateTime.UtcNow,
                Action = "dashboardInvalidated",
                Sequence = sequence
            });
        }
    }

    private static string GetClientRequestId(object command)
        => command is WriteCommandBase write ? write.ClientRequestId : Guid.NewGuid().ToString("N");

    private static decimal RoundMoney(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    private static decimal RoundQuantity(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    private async Task ThrowIfRevisionMismatchAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        string tableName,
        Guid id,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        const string sql = @"SELECT ""Revision"", ""UpdatedAt"", ""UpdatedByUserId""
                            FROM ""{0}""
                            WHERE ""Id"" = @id FOR UPDATE";

        var row = await connection.QueryFirstOrDefaultAsync(
            string.Format(sql, tableName),
            new { id },
            transaction);

        if (row == null)
            throw new InvalidOperationException($"记录不存在: {tableName}/{id}");

        long currentRevision = (long)row.Revision;
        if (currentRevision != expectedRevision)
        {
            string? actorName = null;
            if (row.UpdatedByUserId is Guid updaterId)
            {
                actorName = await connection.ExecuteScalarAsync<string?>(
                    "SELECT \"DisplayName\" FROM \"CloudUsers\" WHERE \"Id\" = @id;",
                    new { id = updaterId },
                    transaction);
            }

            throw new ConflictException(
                $"Expected revision {expectedRevision} but found {currentRevision}.",
                actorName,
                (DateTime)row.UpdatedAt,
                currentRevision);
        }
    }

    private async Task<string> SnapshotJsonAsync<TDto>(TDto dto)
        => await Task.FromResult(JsonSerializer.Serialize(dto, JsonOptions));
}
