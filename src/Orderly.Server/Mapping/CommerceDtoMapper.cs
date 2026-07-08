using Orderly.Contracts.Commerce;
using Orderly.Core.Commerce;

namespace Orderly.Server.Mapping;

public static class CommerceDtoMapper
{
    public static CloudOrderDto ToOrderDto(dynamic r, bool canViewCosts)
    {
        return new CloudOrderDto
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
    }

    public static CloudProductDto ToProductDto(dynamic r, bool canViewCosts)
    {
        return new CloudProductDto
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
    }

    public static CloudInventoryItemDto ToInventoryItemDto(dynamic r, bool canViewCosts)
    {
        return new CloudInventoryItemDto
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
    }

    public static CloudInventoryMovementDto ToInventoryMovementDto(dynamic r)
    {
        return new CloudInventoryMovementDto
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
            InventoryItemId = r.InventoryItemId,
            MovementType = (InventoryMovementType)(int)r.MovementType,
            Quantity = r.Quantity,
            SupplierId = r.SupplierId,
            OrderId = r.OrderId,
            OccurredAtUtc = r.OccurredAt,
            BusinessKey = r.BusinessKey,
            Note = r.Note
        };
    }

    public static CloudCustomerDto ToCustomerDto(dynamic r)
    {
        return new CloudCustomerDto
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
    }

    public static CloudCashFlowEntryDto ToCashFlowEntryDto(dynamic r)
    {
        return new CloudCashFlowEntryDto
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
    }

    public static CloudBusinessTaskDto ToBusinessTaskDto(dynamic r)
    {
        return new CloudBusinessTaskDto
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
    }

    public static CloudBusinessInsightDto ToInsightDto(dynamic r)
    {
        return new CloudBusinessInsightDto
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
    }
}
