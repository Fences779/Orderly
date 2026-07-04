using System.Text;
using System.Text.Json;
using Dapper;
using Orderly.Contracts.Commerce;
using Orderly.Contracts.Sync;
using Orderly.Core.Commerce;
using Orderly.Server.Data;
using Orderly.Server.Models;

namespace Orderly.Server.Services;

public interface IWorkspaceSyncQueryService
{
    Task<SnapshotTokenResponse> CreateSnapshotAsync(Guid workspaceId, string? entityType, CancellationToken cancellationToken = default);
    Task<SnapshotPageResponse<object>> GetSnapshotPageAsync(string snapshotToken, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ChangesResponse> GetChangesAsync(Guid workspaceId, long afterSequence, int maxCount, CancellationToken cancellationToken = default);
}

public sealed class WorkspaceSyncQueryService : IWorkspaceSyncQueryService
{
    private readonly PostgresConnectionFactory _connectionFactory;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICloudAuthService _authService;
    private readonly ICloudPermissionService _permissions;
    private const int DefaultPageSize = 200;

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

        var tokenPayload = new SnapshotTokenPayload
        {
            WorkspaceId = workspaceId,
            EntityType = entityType,
            Sequence = sequence,
            CreatedAtUtc = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(tokenPayload);
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        return new SnapshotTokenResponse
        {
            SnapshotToken = token,
            SnapshotSequence = sequence,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(24)
        };
    }

    public async Task<SnapshotPageResponse<object>> GetSnapshotPageAsync(string snapshotToken, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);

        var payload = DecodeToken(snapshotToken);
        var workspaceId = payload.WorkspaceId;
        var entityType = payload.EntityType;

        var membership = await GetMembershipAsync(cancellationToken);
        if (membership == null || membership.WorkspaceId != workspaceId)
        {
            throw new UnauthorizedAccessException("Workspace access denied.");
        }

        var canViewCosts = _permissions.CanViewCosts(membership);

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);

        var table = ResolveTableName(entityType);
        var where = $@"WHERE ""WorkspaceId"" = @workspaceId AND ""DeletedAt"" IS NULL AND ""Lifecycle"" = 0";
        var countSql = $"SELECT COUNT(*) FROM \"{table}\" {where};";
        var itemsSql = $@"
            SELECT * FROM ""{table}""
            {where}
            ORDER BY ""Id""
            LIMIT @pageSize OFFSET @offset;";

        var total = await connection.ExecuteScalarAsync<long>(countSql, new { workspaceId });
        var offset = (page - 1) * pageSize;
        var rows = await connection.QueryAsync(itemsSql, new { workspaceId, pageSize, offset });

        var items = rows.Select(r => MapRow(entityType, r, canViewCosts)).Cast<object>().ToList();

        return new SnapshotPageResponse<object>
        {
            SnapshotToken = snapshotToken,
            EntityType = entityType ?? "all",
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

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);
        const string sql = @"
            SELECT ""Sequence"", ""EntityType"", ""EntityId"", ""Action"", ""Revision"", ""ActorUserId"", ""OccurredAt"", ""PayloadHintJson""
            FROM ""CloudChangeLog""
            WHERE ""WorkspaceId"" = @workspaceId AND ""Sequence"" > @afterSequence
            ORDER BY ""Sequence"" ASC
            LIMIT @maxCount;";

        var rows = await connection.QueryAsync(sql, new { workspaceId, afterSequence, maxCount });
        var entries = rows.Select(r => new ChangeLogEntryDto
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

        var lastSequence = await GetLatestSequenceAsync(connection, workspaceId);
        var toSequence = entries.Count > 0 ? entries[^1].Sequence : afterSequence;

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

    private static string ResolveTableName(string? entityType)
    {
        if (string.IsNullOrWhiteSpace(entityType))
        {
            // Default to orders when no entity type specified.
            return "CommerceOrders";
        }

        return entityType.ToLowerInvariant() switch
        {
            "order" or "orders" => "CommerceOrders",
            "product" or "products" => "CommerceProducts",
            "inventoryitem" or "inventory" => "CommerceInventoryItems",
            "customer" or "customers" => "CommerceCustomers",
            "cashflow" or "cashflowentry" => "CommerceCashFlowEntries",
            "businessinsight" or "insight" => "CommerceBusinessInsights",
            "businesstask" or "task" => "CommerceBusinessTasks",
            _ => throw new InvalidOperationException($"Unsupported snapshot entity type: {entityType}")
        };
    }

    private static object MapRow(string? entityType, dynamic r, bool canViewCosts)
    {
        var key = entityType?.ToLowerInvariant() ?? "order";
        return key switch
        {
            "order" or "orders" => MapOrder(r, canViewCosts),
            "product" or "products" => MapProduct(r, canViewCosts),
            "inventoryitem" or "inventory" => MapInventoryItem(r, canViewCosts),
            "customer" or "customers" => MapCustomer(r),
            "cashflow" or "cashflowentry" => MapCashFlowEntry(r),
            "businessinsight" or "insight" => MapInsight(r),
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
        AssignedToUserId = r.AssignedByUserId,
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

    private sealed class SnapshotTokenPayload
    {
        public Guid WorkspaceId { get; set; }
        public string? EntityType { get; set; }
        public long Sequence { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
