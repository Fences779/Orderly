using Orderly.Contracts.Commerce;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Services;

namespace Orderly.Remote.Services;

public static class RemoteEntityMapper
{
    public static Order ToEntity(this CloudOrderDto dto)
    {
        var order = new Order
        {
            Id = dto.Id,
            WorkspaceId = dto.WorkspaceId,
            OrderNo = dto.OrderNo,
            CustomerId = dto.CustomerId,
            SalesStage = dto.SalesStage,
            PaymentStage = dto.PaymentStage,
            FulfillmentStage = dto.FulfillmentStage,
            Subtotal = CommerceMoney.From(dto.Subtotal),
            Total = CommerceMoney.From(dto.Total),
            Cost = dto.Cost.HasValue ? CommerceMoney.From(dto.Cost.Value) : CommerceMoney.Zero,
            GrossProfit = dto.GrossProfit.HasValue ? CommerceMoney.From(dto.GrossProfit.Value) : CommerceMoney.Zero,
            GrossMargin = dto.GrossMargin ?? 0m,
            PaidAmount = CommerceMoney.From(dto.PaidAmount),
            ReceivableAmount = CommerceMoney.From(dto.ReceivableAmount),
            OrderedAt = dto.OrderedAtUtc,
            Note = dto.Note
        };
        order.RestoreAuditState(dto.UpdatedAtUtc, GetDeletedAt(dto.Lifecycle), dto.Lifecycle);
        return order;
    }

    public static Product ToEntity(this CloudProductDto dto)
    {
        var product = new Product
        {
            Id = dto.Id,
            WorkspaceId = dto.WorkspaceId,
            Name = dto.Name,
            Code = dto.Code,
            ProductType = dto.ProductType,
            Description = dto.Description,
            DefaultUnitId = dto.DefaultUnitId,
            SupplierId = dto.SupplierId,
            DefaultPrice = CommerceMoney.From(dto.DefaultPrice),
            DefaultCost = dto.DefaultCost.HasValue ? CommerceMoney.From(dto.DefaultCost.Value) : CommerceMoney.Zero
        };
        product.RestoreAuditState(dto.UpdatedAtUtc, GetDeletedAt(dto.Lifecycle), dto.Lifecycle);
        return product;
    }

    public static InventoryItem ToEntity(this CloudInventoryItemDto dto)
    {
        var item = new InventoryItem
        {
            Id = dto.Id,
            WorkspaceId = dto.WorkspaceId,
            Name = dto.Name,
            Sku = dto.Sku,
            ProductId = dto.ProductId,
            ProductVariantId = dto.ProductVariantId,
            UnitId = dto.UnitId,
            QuantityAvailable = dto.QuantityAvailable,
            ReorderThreshold = dto.ReorderThreshold,
            UnitCost = dto.UnitCost.HasValue ? CommerceMoney.From(dto.UnitCost.Value) : CommerceMoney.Zero
        };
        item.RestoreAuditState(dto.UpdatedAtUtc, GetDeletedAt(dto.Lifecycle), dto.Lifecycle);
        return item;
    }

    public static Customer ToEntity(this CloudCustomerDto dto)
    {
        var customer = new Customer
        {
            Id = dto.Id,
            WorkspaceId = dto.WorkspaceId,
            Name = dto.Name,
            Phone = dto.Phone,
            WeChat = dto.WeChat,
            Email = dto.Email,
            LastOrderAt = dto.LastOrderAtUtc,
            CompletedOrderCount = dto.CompletedOrderCount,
            TotalSpend = CommerceMoney.From(dto.TotalSpend)
        };
        customer.RestoreAuditState(dto.UpdatedAtUtc, GetDeletedAt(dto.Lifecycle), dto.Lifecycle);
        return customer;
    }

    public static CashFlowEntry ToEntity(this CloudCashFlowEntryDto dto)
    {
        var entry = new CashFlowEntry
        {
            Id = dto.Id,
            WorkspaceId = dto.WorkspaceId,
            Direction = dto.Direction,
            Amount = CommerceMoney.From(dto.Amount),
            SettledAmount = CommerceMoney.From(dto.SettledAmount),
            SettlementStatus = dto.SettlementStatus,
            OccurredAt = dto.OccurredAtUtc,
            DueDate = dto.DueDateUtc,
            CategoryName = dto.CategoryName,
            OrderId = dto.OrderId,
            PaymentRecordId = dto.PaymentRecordId,
            BusinessKey = dto.BusinessKey
        };
        entry.RestoreAuditState(dto.UpdatedAtUtc, GetDeletedAt(dto.Lifecycle), dto.Lifecycle);
        return entry;
    }

    public static BusinessInsight ToEntity(this CloudBusinessInsightDto dto)
    {
        var insight = new BusinessInsight
        {
            Id = dto.Id,
            WorkspaceId = dto.WorkspaceId,
            Severity = dto.Severity,
            Title = dto.Title,
            Message = dto.Message,
            Category = dto.Category,
            IsAcknowledged = dto.IsAcknowledged,
            GeneratedAt = dto.GeneratedAtUtc,
            BusinessKey = dto.BusinessKey
        };
        insight.RestoreAuditState(dto.UpdatedAtUtc, GetDeletedAt(dto.Lifecycle), dto.Lifecycle);
        return insight;
    }

    public static CloudProductDto ToDto(this Product entity) => new()
    {
        Id = entity.Id,
        WorkspaceId = entity.WorkspaceId,
        Name = entity.Name,
        Code = entity.Code ?? string.Empty,
        ProductType = entity.ProductType,
        Description = entity.Description,
        DefaultUnitId = entity.DefaultUnitId,
        SupplierId = entity.SupplierId,
        DefaultPrice = entity.DefaultPrice.Amount,
        DefaultCost = entity.DefaultCost.Amount,
        Revision = 0L,
        CreatedAtUtc = entity.CreatedAt,
        UpdatedAtUtc = entity.UpdatedAt,
        CreatedByUserId = null,
        UpdatedByUserId = null,
        Lifecycle = entity.Lifecycle,
        CustomFieldsJson = entity.CustomFieldsJson
    };

    public static CloudInventoryItemDto ToDto(this InventoryItem entity) => new()
    {
        Id = entity.Id,
        WorkspaceId = entity.WorkspaceId,
        Name = entity.Name,
        Sku = entity.Sku,
        ProductId = entity.ProductId,
        ProductVariantId = entity.ProductVariantId,
        UnitId = entity.UnitId,
        QuantityAvailable = entity.QuantityAvailable,
        ReorderThreshold = entity.ReorderThreshold,
        UnitCost = entity.UnitCost.Amount,
        Revision = 0L,
        CreatedAtUtc = entity.CreatedAt,
        UpdatedAtUtc = entity.UpdatedAt,
        CreatedByUserId = null,
        UpdatedByUserId = null,
        Lifecycle = entity.Lifecycle,
        CustomFieldsJson = entity.CustomFieldsJson
    };

    public static CloudCashFlowEntryDto ToDto(this CashFlowEntry entity) => new()
    {
        Id = entity.Id,
        WorkspaceId = entity.WorkspaceId,
        Direction = entity.Direction,
        Amount = entity.Amount.Amount,
        SettledAmount = entity.SettledAmount.Amount,
        SettlementStatus = entity.SettlementStatus,
        OccurredAtUtc = entity.OccurredAt,
        DueDateUtc = entity.DueDate,
        CategoryName = entity.CategoryName ?? string.Empty,
        OrderId = entity.OrderId,
        PaymentRecordId = entity.PaymentRecordId,
        BusinessKey = entity.BusinessKey,
        Revision = 0L,
        CreatedAtUtc = entity.CreatedAt,
        UpdatedAtUtc = entity.UpdatedAt,
        CreatedByUserId = null,
        UpdatedByUserId = null,
        Lifecycle = entity.Lifecycle,
        CustomFieldsJson = entity.CustomFieldsJson
    };

    public static DashboardSnapshot ToSnapshot(this CloudDashboardDto dto)
    {
        return new DashboardSnapshot
        {
            AsOfUtc = dto.AsOfUtc,
            Metrics = new DashboardMetrics
            {
                TotalOrders = dto.TotalOrders,
                CompletedOrders = dto.CompletedOrders,
                TotalRevenue = CommerceMoney.From(dto.TotalRevenue),
                GrossProfit = CommerceMoney.From(dto.GrossProfit ?? 0m),
                OutstandingReceivable = CommerceMoney.From(dto.OutstandingReceivable),
                CashInflow = CommerceMoney.From(dto.CashInflow ?? 0m),
                CashOutflow = CommerceMoney.From(dto.CashOutflow ?? 0m),
                NetCashFlow = CommerceMoney.From(dto.NetCashFlow ?? 0m),
                CustomerCount = dto.CustomerCount,
                LowStockItemCount = dto.LowStockItemCount
            },
            Trend = dto.Trend.Select(t => new DashboardTrendPoint
            {
                Date = DateOnly.FromDateTime(t.DateUtc),
                CompletedOrderCount = t.CompletedOrderCount,
                Revenue = CommerceMoney.From(t.Revenue),
                CashInflow = CommerceMoney.From(t.CashInflow ?? 0m),
                CashOutflow = CommerceMoney.From(t.CashOutflow ?? 0m)
            }).ToList()
        };
    }

    private static DateTime? GetDeletedAt(EntityLifecycleStatus lifecycle) =>
        lifecycle != EntityLifecycleStatus.Active ? DateTime.UtcNow : (DateTime?)null;
}
