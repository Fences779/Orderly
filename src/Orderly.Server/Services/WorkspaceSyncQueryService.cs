using System.Text;
using System.Text.Json;
using Dapper;
using Orderly.Contracts.Commerce;
using Orderly.Contracts.Permissions;
using Orderly.Contracts.Sync;
using Orderly.Core.Commerce;
using Orderly.Server.Data;
using Orderly.Server.Models;

namespace Orderly.Server.Services;

public interface IWorkspaceSyncQueryService
{
    Task<SnapshotTokenResponse> CreateSnapshotAsync(Guid workspaceId, string? entityType, CancellationToken cancellationToken = default);
    Task<SnapshotPageResponse<object>> GetSnapshotPageAsync(string snapshotToken, string? entityType, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ChangesResponse> GetChangesAsync(Guid workspaceId, long afterSequence, int maxCount, CancellationToken cancellationToken = default);
}

public sealed class WorkspaceSyncQueryService : IWorkspaceSyncQueryService
{
    private readonly PostgresConnectionFactory _connectionFactory;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICloudAuthService _authService;
    private readonly ICloudPermissionService _permissions;
    private const int DefaultPageSize = 200;
    private const int ChangeLogRetentionDays = 30;
    private static readonly TimeSpan SnapshotTokenLifetime = TimeSpan.FromMinutes(30);

    public WorkspaceSyncQueryService(
        PostgresConnectionFactory connectionFactory,
        ICurrentUserContext currentUser,
        ICloudAuthService authService,
        ICloudPermissionService permissions)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
        _authService = authService;
        _permissions = permissions;
    }

    public async Task<SnapshotTokenResponse> CreateSnapshotAsync(Guid workspaceId, string? entityType, CancellationToken cancellationToken = default)
    {
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var sequence = await GetLatestSequenceAsync(connection, workspaceId);
        var now = DateTime.UtcNow;
        var normalizedEntityType = NormalizeEntityType(entityType);

        var tokenPayload = new SnapshotTokenPayload
        {
            WorkspaceId = workspaceId,
            EntityType = normalizedEntityType,
            Sequence = sequence,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(SnapshotTokenLifetime)
        };

        var json = JsonSerializer.Serialize(tokenPayload);
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        return new SnapshotTokenResponse
        {
            SnapshotToken = token,
            SnapshotSequence = sequence,
            ExpiresAtUtc = tokenPayload.ExpiresAtUtc
        };
    }

    public async Task<SnapshotPageResponse<object>> GetSnapshotPageAsync(string snapshotToken, string? entityType, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);

        var payload = DecodeToken(snapshotToken);
        var workspaceId = payload.WorkspaceId;
        if (payload.ExpiresAtUtc <= DateTime.UtcNow)
        {
            throw new InvalidOperationException("Snapshot token has expired.");
        }

        var requestedEntityType = NormalizeEntityType(entityType ?? payload.EntityType)
            ?? throw new InvalidOperationException("Snapshot entity type is required.");
        if (!string.IsNullOrWhiteSpace(payload.EntityType)
            && !string.Equals(payload.EntityType, requestedEntityType, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Snapshot entity type does not match token.");
        }

        var membership = await GetMembershipAsync(cancellationToken);
        if (membership == null || membership.WorkspaceId != workspaceId)
        {
            throw new UnauthorizedAccessException("Workspace access denied.");
        }

        var canViewCosts = _permissions.CanViewCosts(membership);
        if (!CanSyncEntityType(requestedEntityType, canViewCosts))
        {
            return new SnapshotPageResponse<object>
            {
                SnapshotToken = snapshotToken,
                EntityType = requestedEntityType,
                Page = page,
                PageSize = pageSize,
                TotalCount = 0,
                Items = Array.Empty<object>(),
                SnapshotSequence = payload.Sequence
            };
        }

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);

        var table = ResolveTableName(requestedEntityType);
        var where = $@"WHERE ""WorkspaceId"" = @workspaceId AND ""DeletedAt"" IS NULL AND ""Lifecycle"" = 0 AND ""LastChangeSequence"" <= @snapshotSequence";
        var countSql = $"SELECT COUNT(*) FROM \"{table}\" {where};";
        var itemsSql = $@"
            SELECT * FROM ""{table}""
            {where}
            ORDER BY ""LastChangeSequence"", ""Id""
            LIMIT @pageSize OFFSET @offset;";

        var total = await connection.ExecuteScalarAsync<long>(countSql, new { workspaceId, snapshotSequence = payload.Sequence });
        var offset = (page - 1) * pageSize;
        var rows = await connection.QueryAsync(itemsSql, new { workspaceId, snapshotSequence = payload.Sequence, pageSize, offset });

        var items = rows.Select(r => MapRow(requestedEntityType, r, canViewCosts)).Cast<object>().ToList();

        return new SnapshotPageResponse<object>
        {
            SnapshotToken = snapshotToken,
            EntityType = requestedEntityType,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            Items = items,
            SnapshotSequence = payload.Sequence
        };
    }

    public async Task<ChangesResponse> GetChangesAsync(Guid workspaceId, long afterSequence, int maxCount, CancellationToken cancellationToken = default)
    {
        maxCount = Math.Clamp(maxCount, 1, 1000);

        var membership = await GetMembershipAsync(cancellationToken);
        if (membership == null || membership.WorkspaceId != workspaceId)
        {
            throw new UnauthorizedAccessException("Workspace access denied.");
        }
        var canViewCosts = _permissions.CanViewCosts(membership);

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var lastSequence = await GetLatestSequenceAsync(connection, workspaceId);
        if (await RequiresFullResyncAsync(connection, workspaceId, afterSequence, lastSequence))
        {
            return new ChangesResponse
            {
                FromSequence = afterSequence,
                ToSequence = lastSequence,
                FullResyncRequired = true,
                Changes = Array.Empty<ChangeLogEntryDto>()
            };
        }

        var restrictedWhere = canViewCosts
            ? string.Empty
            : @" AND ""EntityType"" <> @cashFlowEntityType AND ""EntityType"" <> @businessInsightEntityType";
        var sql = $@"
            SELECT ""Sequence"", ""EntityType"", ""EntityId"", ""Action"", ""Revision"", ""ActorUserId"", ""OccurredAt"", ""PayloadHintJson""
            FROM ""CloudChangeLog""
            WHERE ""WorkspaceId"" = @workspaceId AND ""Sequence"" > @afterSequence
            {restrictedWhere}
            ORDER BY ""Sequence"" ASC
            LIMIT @maxCount;";

        var rows = await connection.QueryAsync(sql, new
        {
            workspaceId,
            afterSequence,
            maxCount,
            cashFlowEntityType = EntityType.CashFlowEntry,
            businessInsightEntityType = EntityType.BusinessInsight
        });
        List<ChangeLogEntryDto> entries = rows.Select(r => new ChangeLogEntryDto
        {
            Sequence = (long)r.Sequence,
            EntityType = r.EntityType,
            EntityId = (Guid?)r.EntityId,
            Action = r.Action,
            Revision = (long?)r.Revision,
            ActorUserId = (Guid?)r.ActorUserId,
            OccurredAtUtc = (DateTime)r.OccurredAt,
            PayloadHintJson = r.PayloadHintJson
        }).ToList();

        var toSequence = entries.Count > 0 ? entries[^1].Sequence : lastSequence;

        return new ChangesResponse
        {
            FromSequence = afterSequence,
            ToSequence = toSequence,
            FullResyncRequired = false,
            Changes = entries
        };
    }

    private async Task<CloudWorkspaceMemberRecord?> GetMembershipAsync(CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (userId == null)
        {
            return null;
        }

        return await _authService.GetMembershipAsync(userId.Value);
    }

    private static async Task<long> GetLatestSequenceAsync(System.Data.IDbConnection connection, Guid workspaceId)
    {
        var sequence = await connection.ExecuteScalarAsync<long?>(
            "SELECT \"LastSequence\" FROM \"CloudWorkspaceSyncState\" WHERE \"WorkspaceId\" = @workspaceId;",
            new { workspaceId });
        return sequence ?? 0;
    }

    private static SnapshotTokenPayload DecodeToken(string snapshotToken)
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(snapshotToken));
        return JsonSerializer.Deserialize<SnapshotTokenPayload>(json)
            ?? throw new InvalidOperationException("Invalid snapshot token.");
    }

    private static string? NormalizeEntityType(string? entityType)
    {
        if (string.IsNullOrWhiteSpace(entityType))
        {
            return null;
        }

        return entityType.Trim().ToLowerInvariant() switch
        {
            "order" or "orders" => "orders",
            "product" or "products" => "products",
            "inventoryitem" or "inventory" => "inventory",
            "customer" or "customers" => "customers",
            "cashflow" or "cashflowentry" => "cashflow",
            "businessinsight" or "insight" => "insight",
            "businesstask" or "task" => "task",
            _ => throw new InvalidOperationException($"Unsupported snapshot entity type: {entityType}")
        };
    }

    private static string ResolveTableName(string entityType)
    {
        return entityType switch
        {
            "orders" => "CommerceOrders",
            "products" => "CommerceProducts",
            "inventory" => "CommerceInventoryItems",
            "customers" => "CommerceCustomers",
            "cashflow" => "CommerceCashFlowEntries",
            "insight" => "CommerceBusinessInsights",
            "task" => "CommerceBusinessTasks",
            _ => throw new InvalidOperationException($"Unsupported snapshot entity type: {entityType}")
        };
    }

    private static bool CanSyncEntityType(string entityType, bool canViewCosts)
    {
        if (canViewCosts)
        {
            return true;
        }

        return entityType is not ("cashflow" or "insight");
    }

    private static async Task<bool> RequiresFullResyncAsync(System.Data.IDbConnection connection, Guid workspaceId, long afterSequence, long lastSequence)
    {
        if (lastSequence <= afterSequence)
        {
            return false;
        }

        var oldestSequence = await connection.ExecuteScalarAsync<long?>(
            "SELECT MIN(\"Sequence\") FROM \"CloudChangeLog\" WHERE \"WorkspaceId\" = @workspaceId;",
            new { workspaceId });
        if (!oldestSequence.HasValue)
        {
            return lastSequence > 0;
        }

        if (afterSequence < oldestSequence.Value - 1)
        {
            return true;
        }

        if (afterSequence <= 0)
        {
            return false;
        }

        var checkpointOccurredAt = await connection.ExecuteScalarAsync<DateTime?>(
            @"SELECT ""OccurredAt"" FROM ""CloudChangeLog""
              WHERE ""WorkspaceId"" = @workspaceId AND ""Sequence"" = @afterSequence;",
            new { workspaceId, afterSequence });
        if (!checkpointOccurredAt.HasValue)
        {
            return true;
        }

        return checkpointOccurredAt.Value < DateTime.UtcNow.AddDays(-ChangeLogRetentionDays);
    }

    private static object MapRow(string entityType, dynamic r, bool canViewCosts)
    {
        return entityType switch
        {
            "orders" => MapOrder(r, canViewCosts),
            "products" => MapProduct(r, canViewCosts),
            "inventory" => MapInventoryItem(r, canViewCosts),
            "customers" => MapCustomer(r),
            "cashflow" => MapCashFlowEntry(r),
            "insight" => MapInsight(r),
            "task" => MapBusinessTask(r),
            _ => throw new InvalidOperationException($"Unsupported entity type in snapshot: {entityType}")
        };
    }

    private static CloudOrderDto MapOrder(dynamic r, bool canViewCosts) => new()
    {
        Id = r.Id,
        Revision = r.Revision,
        CreatedAtUtc = r.CreatedAt,
        UpdatedAtUtc = r.UpdatedAt,
        CreatedByUserId = r.CreatedByUserId,
        UpdatedByUserId = r.UpdatedByUserId,
        Lifecycle = (EntityLifecycleStatus)(int)r.Lifecycle,
        CustomFieldsJson = r.CustomFieldsJson,
        WorkspaceId = r.WorkspaceId,
        OrderNo = r.OrderNo,
        CustomerId = r.CustomerId,
        SalesStage = (OrderSalesStage)(int)r.SalesStage,
        PaymentStage = (OrderPaymentStage)(int)r.PaymentStage,
        FulfillmentStage = (OrderFulfillmentStage)(int)r.FulfillmentStage,
        Subtotal = r.Subtotal,
        Total = r.Total,
        Cost = canViewCosts ? (decimal?)r.Cost : null,
        GrossProfit = canViewCosts ? (decimal?)r.GrossProfit : null,
        GrossMargin = canViewCosts ? (decimal?)r.GrossMargin : null,
        PaidAmount = r.PaidAmount,
        ReceivableAmount = r.ReceivableAmount,
        OrderedAtUtc = r.OrderedAt,
        Note = r.Note,
        AssignedToUserId = r.AssignedToUserId,
        ArchivedByUserId = r.ArchivedByUserId,
        ArchiveReason = r.ArchiveReason
    };

    private static CloudProductDto MapProduct(dynamic r, bool canViewCosts) => new()
    {
        Id = r.Id,
        Revision = r.Revision,
        CreatedAtUtc = r.CreatedAt,
        UpdatedAtUtc = r.UpdatedAt,
        CreatedByUserId = r.CreatedByUserId,
        UpdatedByUserId = r.UpdatedByUserId,
        Lifecycle = (EntityLifecycleStatus)(int)r.Lifecycle,
        CustomFieldsJson = r.CustomFieldsJson,
        WorkspaceId = r.WorkspaceId,
        Name = r.Name,
        Code = r.Code,
        ProductType = (ProductType)(int)r.ProductType,
        Description = r.Description,
        DefaultUnitId = r.DefaultUnitId,
        SupplierId = r.SupplierId,
        DefaultPrice = r.DefaultPrice,
        DefaultCost = canViewCosts ? (decimal?)r.DefaultCost : null
    };

    private static CloudInventoryItemDto MapInventoryItem(dynamic r, bool canViewCosts) => new()
    {
        Id = r.Id,
        Revision = r.Revision,
        CreatedAtUtc = r.CreatedAt,
        UpdatedAtUtc = r.UpdatedAt,
        CreatedByUserId = r.CreatedByUserId,
        UpdatedByUserId = r.UpdatedByUserId,
        Lifecycle = (EntityLifecycleStatus)(int)r.Lifecycle,
        CustomFieldsJson = r.CustomFieldsJson,
        WorkspaceId = r.WorkspaceId,
        Name = r.Name,
        Sku = r.Sku,
        ProductId = r.ProductId,
        ProductVariantId = r.ProductVariantId,
        UnitId = r.UnitId,
        QuantityAvailable = r.QuantityAvailable,
        ReorderThreshold = r.ReorderThreshold,
        UnitCost = canViewCosts ? (decimal?)r.UnitCost : null
    };

    private static CloudCustomerDto MapCustomer(dynamic r) => new()
    {
        Id = r.Id,
        Revision = r.Revision,
        CreatedAtUtc = r.CreatedAt,
        UpdatedAtUtc = r.UpdatedAt,
        CreatedByUserId = r.CreatedByUserId,
        UpdatedByUserId = r.UpdatedByUserId,
        Lifecycle = (EntityLifecycleStatus)(int)r.Lifecycle,
        CustomFieldsJson = r.CustomFieldsJson,
        WorkspaceId = r.WorkspaceId,
        Name = r.Name,
        Phone = r.Phone,
        WeChat = r.WeChat,
        Email = r.Email,
        LastOrderAtUtc = r.LastOrderAt,
        CompletedOrderCount = r.CompletedOrderCount,
        TotalSpend = r.TotalSpend,
        AssignedToUserId = r.AssignedToUserId
    };

    private static CloudCashFlowEntryDto MapCashFlowEntry(dynamic r) => new()
    {
        Id = r.Id,
        Revision = r.Revision,
        CreatedAtUtc = r.CreatedAt,
        UpdatedAtUtc = r.UpdatedAt,
        CreatedByUserId = r.CreatedByUserId,
        UpdatedByUserId = r.UpdatedByUserId,
        Lifecycle = (EntityLifecycleStatus)(int)r.Lifecycle,
        CustomFieldsJson = r.CustomFieldsJson,
        WorkspaceId = r.WorkspaceId,
        Direction = (CashFlowDirection)(int)r.Direction,
        Amount = r.Amount,
        SettledAmount = r.SettledAmount,
        SettlementStatus = (CashFlowSettlementStatus)(int)r.SettlementStatus,
        OccurredAtUtc = r.OccurredAt,
        DueDateUtc = r.DueDate,
        CategoryName = r.CategoryName,
        OrderId = r.OrderId,
        PaymentRecordId = r.PaymentRecordId,
        BusinessKey = r.BusinessKey
    };

    private static CloudBusinessInsightDto MapInsight(dynamic r) => new()
    {
        Id = r.Id,
        Revision = r.Revision,
        CreatedAtUtc = r.CreatedAt,
        UpdatedAtUtc = r.UpdatedAt,
        CreatedByUserId = r.CreatedByUserId,
        UpdatedByUserId = r.UpdatedByUserId,
        Lifecycle = (EntityLifecycleStatus)(int)r.Lifecycle,
        CustomFieldsJson = r.CustomFieldsJson,
        WorkspaceId = r.WorkspaceId,
        Severity = (InsightSeverity)(int)r.Severity,
        Title = r.Title,
        Message = r.Message,
        Category = r.Category,
        IsAcknowledged = r.IsAcknowledged,
        GeneratedAtUtc = r.GeneratedAt,
        BusinessKey = r.BusinessKey
    };

    private static CloudBusinessTaskDto MapBusinessTask(dynamic r) => new()
    {
        Id = r.Id,
        Revision = r.Revision,
        CreatedAtUtc = r.CreatedAt,
        UpdatedAtUtc = r.UpdatedAt,
        CreatedByUserId = r.CreatedByUserId,
        UpdatedByUserId = r.UpdatedByUserId,
        Lifecycle = (EntityLifecycleStatus)(int)r.Lifecycle,
        CustomFieldsJson = r.CustomFieldsJson,
        WorkspaceId = r.WorkspaceId,
        Title = r.Title,
        Description = r.Description,
        Status = (Orderly.Core.Commerce.TaskStatus)(int)r.Status,
        DueDateUtc = r.DueDate,
        CompletedAtUtc = r.CompletedAt,
        CustomerId = r.CustomerId,
        OrderId = r.OrderId,
        AssignedToUserId = r.AssignedToUserId
    };

    private sealed class SnapshotTokenPayload
    {
        public Guid WorkspaceId { get; set; }
        public string? EntityType { get; set; }
        public long Sequence { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
    }
}
