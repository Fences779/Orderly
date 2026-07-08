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
        where TCommand : notnull
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
        where TCommand : notnull
        where TDto : notnull
    {
        return await ExecuteWithIdempotencyResultAsync(
            workspaceId,
            action,
            command,
            async (connection, transaction, sequence, collector, ct) =>
            {
                var (dto, entityType, entityId) = await execute(connection, transaction, sequence, collector, ct);
                return new CommandExecutionResult<TDto>(dto, entityType, entityId, sequence);
            },
            cancellationToken);
    }

    private async Task<CommandResult<TDto>> ExecuteWithIdempotencyResultAsync<TCommand, TDto>(
        Guid workspaceId,
        string action,
        TCommand command,
        Func<System.Data.Common.DbConnection, NpgsqlTransaction, long, NotificationCollector, CancellationToken, Task<CommandExecutionResult<TDto>>> execute,
        CancellationToken cancellationToken)
        where TCommand : notnull
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

                    var replayDto = DeserializeReplayDto<TDto>(beginResult.ResponseBodyJson!, beginResult.ResourceType);
                    return new CommandResult<TDto>(replayDto, true);
                }

                var sequence = await AllocateSequenceAsync(connection, transaction, workspaceId);
                var collector = new NotificationCollector();
                var result = await execute(connection, transaction, sequence, collector, cancellationToken);
                var responseJson = JsonSerializer.Serialize(result.Dto, result.Dto.GetType(), JsonOptions);
                if (result.Dto is CloudEntityDto entityDto)
                {
                    await CloudDataLifecycleService.RecordEntityVersionAsync(
                        connection,
                        transaction,
                        workspaceId,
                        result.EntityType,
                        result.EntityId,
                        entityDto.Revision,
                        action,
                        responseJson,
                        userId);
                }

                await _idempotency.CompleteAsync(workspaceId, userId, action, clientRequestId, 200, responseJson, result.EntityType, result.EntityId, connection, transaction, cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                foreach (var (eventName, payload) in collector.Payloads)
                {
                    await _notifier.NotifyAsync(payload.WorkspaceId, eventName, payload);
                }

                await BroadcastAsync(workspaceId, result.EntityType, result.EntityId, result.Dto, result.Sequence);
                return new CommandResult<TDto>(result.Dto, false);
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

    private readonly record struct CommandExecutionResult<TDto>(TDto Dto, string EntityType, Guid EntityId, long Sequence)
        where TDto : notnull;

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

    private static TDto DeserializeReplayDto<TDto>(string responseBodyJson, string? resourceType)
        where TDto : notnull
    {
        if (typeof(TDto) == typeof(CloudEntityDto))
        {
            CloudEntityDto? dto = resourceType switch
            {
                EntityType.Order => JsonSerializer.Deserialize<CloudOrderDto>(responseBodyJson, JsonOptions),
                EntityType.Product => JsonSerializer.Deserialize<CloudProductDto>(responseBodyJson, JsonOptions),
                EntityType.InventoryItem => JsonSerializer.Deserialize<CloudInventoryItemDto>(responseBodyJson, JsonOptions),
                EntityType.Customer => JsonSerializer.Deserialize<CloudCustomerDto>(responseBodyJson, JsonOptions),
                EntityType.CashFlowEntry => JsonSerializer.Deserialize<CloudCashFlowEntryDto>(responseBodyJson, JsonOptions),
                EntityType.BusinessTask => JsonSerializer.Deserialize<CloudBusinessTaskDto>(responseBodyJson, JsonOptions),
                EntityType.PriceChangeRequest => JsonSerializer.Deserialize<CloudPriceChangeRequestDto>(responseBodyJson, JsonOptions),
                EntityType.ExportJob => JsonSerializer.Deserialize<CloudExportJobDto>(responseBodyJson, JsonOptions),
                _ => throw new InvalidOperationException($"Idempotency replay has unsupported entity type: {resourceType}.")
            };

            return (TDto)(object)(dto ?? throw new InvalidOperationException("Idempotency replay could not be deserialized."));
        }

        return JsonSerializer.Deserialize<TDto>(responseBodyJson, JsonOptions)
            ?? throw new InvalidOperationException("Idempotency replay could not be deserialized.");
    }

    private static decimal RoundMoney(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    private static decimal RoundQuantity(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    private async Task ThrowIfRevisionMismatchAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        string tableName,
        Guid id,
        long expectedRevision,
        CancellationToken cancellationToken,
        WriteCommandBase? command = null,
        string? entityType = null)
    {
        const string sql = @"SELECT ""Revision"", ""UpdatedAt"", ""UpdatedByUserId""
                                   , ""WorkspaceId""
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
            if (command != null
                && !string.IsNullOrWhiteSpace(entityType)
                && await CanMergeDisjointFieldsAsync(
                    connection,
                    transaction,
                    (Guid)row.WorkspaceId,
                    entityType,
                    id,
                    expectedRevision,
                    currentRevision,
                    command))
            {
                return;
            }

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

    private static async Task<bool> CanMergeDisjointFieldsAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid workspaceId,
        string entityType,
        Guid entityId,
        long baseRevision,
        long currentRevision,
        WriteCommandBase command)
    {
        var requestedFields = GetRequestedChangedFields(command);
        if (requestedFields.Count == 0)
        {
            return false;
        }

        var rows = await connection.QueryAsync<VersionPayloadRow>(
            @"SELECT DISTINCT ON (""Revision"") ""Revision"", ""PayloadJson""::text AS ""PayloadJson""
              FROM ""CloudEntityVersions""
              WHERE ""WorkspaceId"" = @workspaceId
                AND ""EntityType"" = @entityType
                AND ""EntityId"" = @entityId
                AND ""Revision"" = ANY(@revisions)
              ORDER BY ""Revision"", ""CreatedAt"" DESC;",
            new { workspaceId, entityType, entityId, revisions = new[] { baseRevision, currentRevision } },
            transaction);

        var payloads = rows.ToDictionary(row => row.Revision, row => row.PayloadJson);
        if (!payloads.TryGetValue(baseRevision, out var basePayload)
            || !payloads.TryGetValue(currentRevision, out var currentPayload))
        {
            return false;
        }

        var serverChangedFields = ExtractChangedFields(basePayload, currentPayload);
        return !serverChangedFields.Overlaps(requestedFields);
    }

    private static HashSet<string> GetRequestedChangedFields(WriteCommandBase command)
    {
        var fields = command.ChangedFields
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Select(NormalizeFieldName)
            .Where(field => !IsSystemField(field))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (fields.Count > 0)
        {
            return fields;
        }

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            nameof(WriteCommandBase.ClientRequestId),
            nameof(WriteCommandBase.IdempotencyKey),
            nameof(WriteCommandBase.ExpectedRevision),
            nameof(WriteCommandBase.BaseVersion),
            nameof(WriteCommandBase.ChangedFields),
            nameof(WriteCommandBase.Reason)
        };

        foreach (var property in command.GetType().GetProperties())
        {
            if (excluded.Contains(property.Name) || property.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = property.GetValue(command);
            if (value == null)
            {
                continue;
            }

            if (value is string text && text == string.Empty)
            {
                continue;
            }

            fields.Add(NormalizeFieldName(MapCommandFieldName(property.Name)));
        }

        return fields;
    }

    private static string MapCommandFieldName(string propertyName)
        => propertyName switch
        {
            "TargetSalesStage" => "SalesStage",
            "TargetPaymentStage" => "PaymentStage",
            "TargetFulfillmentStage" => "FulfillmentStage",
            "NewStatus" => "Status",
            "CompletedAtUtc" => "CompletedAtUtc",
            _ => propertyName
        };

    private static HashSet<string> ExtractChangedFields(string basePayload, string currentPayload)
    {
        using var baseDocument = JsonDocument.Parse(basePayload);
        using var currentDocument = JsonDocument.Parse(currentPayload);
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in baseDocument.RootElement.EnumerateObject())
        {
            fieldNames.Add(property.Name);
        }

        foreach (var property in currentDocument.RootElement.EnumerateObject())
        {
            fieldNames.Add(property.Name);
        }

        foreach (var fieldName in fieldNames)
        {
            var normalized = NormalizeFieldName(fieldName);
            if (IsSystemField(normalized))
            {
                continue;
            }

            var baseValue = baseDocument.RootElement.TryGetProperty(fieldName, out var left) ? left.GetRawText() : "null";
            var currentValue = currentDocument.RootElement.TryGetProperty(fieldName, out var right) ? right.GetRawText() : "null";
            if (!string.Equals(baseValue, currentValue, StringComparison.Ordinal))
            {
                changed.Add(normalized);
            }
        }

        return changed;
    }

    private static string NormalizeFieldName(string fieldName)
    {
        var normalized = fieldName.Trim();
        if (normalized.EndsWith("Utc", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^3];
        }

        return normalized.ToLowerInvariant();
    }

    private static bool IsSystemField(string normalizedFieldName)
        => normalizedFieldName is "revision"
            or "version"
            or "createdat"
            or "createdbyuserid"
            or "updatedat"
            or "updatedbyuserid"
            or "lastchangesequence"
            or "lifecycle";

    private sealed class VersionPayloadRow
    {
        public long Revision { get; set; }
        public string PayloadJson { get; set; } = "{}";
    }

    private async Task<string> SnapshotJsonAsync<TDto>(TDto dto)
        => await Task.FromResult(JsonSerializer.Serialize(dto, dto?.GetType() ?? typeof(TDto), JsonOptions));
}
