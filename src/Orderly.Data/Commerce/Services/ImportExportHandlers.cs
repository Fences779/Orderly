using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Core.Commerce.Services;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Commerce.Services;

/// <summary>
/// Import/export handler for <see cref="Product"/>. Deterministic match key: <c>Code</c> → <c>Name</c>
/// (design Import section).
/// </summary>
internal sealed class ProductImportExportHandler : ImportExportHandler<Product>
{
    public ProductImportExportHandler(SqliteConnectionFactory connectionFactory, IProductRepository repository)
        : base(connectionFactory, repository)
    {
    }

    public override ImportExportDataType DataType => ImportExportDataType.Product;

    public override IReadOnlyList<string> Columns { get; } = new[]
    {
        "Id", "WorkspaceId", "Code", "Name", "ProductType", "Description",
        "DefaultUnitId", "SupplierId", "DefaultPrice", "DefaultCost", "CustomFieldsJson",
    };

    public override IReadOnlyList<string> RequiredHeaderColumns { get; } = new[] { "Code", "Name" };

    protected override IReadOnlyList<Func<RowValues, string?>> RowKeyExtractors { get; } = new Func<RowValues, string?>[]
    {
        values => values.GetTrimmed("Code"),
        values => values.GetTrimmed("Name"),
    };

    protected override IReadOnlyList<Func<Product, string?>> EntityKeyExtractors { get; } = new Func<Product, string?>[]
    {
        product => product.Code,
        product => product.Name,
    };

    protected override IReadOnlyList<string> ToCells(Product entity) => new[]
    {
        Text(entity.Id),
        Text(entity.WorkspaceId),
        entity.Code ?? string.Empty,
        entity.Name,
        Text(entity.ProductType),
        entity.Description ?? string.Empty,
        Text(entity.DefaultUnitId),
        Text(entity.SupplierId),
        Text(entity.DefaultPrice),
        Text(entity.DefaultCost),
        entity.CustomFieldsJson ?? string.Empty,
    };

    protected override string? ValidateRow(RowValues values)
    {
        if (values.GetTrimmed("Name") is null)
        {
            return "缺少商品名称（Name）。";
        }

        if (values.GetTrimmed("ProductType") is string productType && !TryParseEnum<ProductType>(productType, out _))
        {
            return $"商品类型（ProductType）的值无效：{productType}。";
        }

        if (values.GetTrimmed("DefaultPrice") is string price && !TryParseMoney(price, out _))
        {
            return $"默认售价（DefaultPrice）的值无效：{price}。";
        }

        if (values.GetTrimmed("DefaultCost") is string cost && !TryParseMoney(cost, out _))
        {
            return $"默认成本（DefaultCost）的值无效：{cost}。";
        }

        if (values.GetTrimmed("DefaultUnitId") is string unit && !TryParseGuidOptional(unit, out _))
        {
            return $"默认单位（DefaultUnitId）不是有效的标识：{unit}。";
        }

        if (values.GetTrimmed("SupplierId") is string supplier && !TryParseGuidOptional(supplier, out _))
        {
            return $"供应商（SupplierId）不是有效的标识：{supplier}。";
        }

        if (values.GetTrimmed("WorkspaceId") is string workspace && !TryParseGuid(workspace, out _))
        {
            return $"工作区（WorkspaceId）不是有效的标识：{workspace}。";
        }

        return null;
    }

    protected override Product BuildEntity(RowValues values)
    {
        var product = new Product { WorkspaceId = RequireWorkspaceId(values) };
        ApplyFields(product, values);
        return product;
    }

    protected override void ApplyUpdate(Product existing, RowValues values) => ApplyFields(existing, values);

    private static void ApplyFields(Product product, RowValues values)
    {
        product.Name = values.GetTrimmed("Name")!;

        if (values.GetTrimmed("Code") is string code) product.Code = code;
        if (values.GetTrimmed("ProductType") is string pt && TryParseEnum<ProductType>(pt, out ProductType type)) product.ProductType = type;
        if (values.GetTrimmed("Description") is string description) product.Description = description;
        if (values.GetTrimmed("DefaultUnitId") is string unit && TryParseGuidOptional(unit, out Guid? unitId) && unitId is Guid u) product.DefaultUnitId = u;
        if (values.GetTrimmed("SupplierId") is string supplier && TryParseGuidOptional(supplier, out Guid? supplierId) && supplierId is Guid s) product.SupplierId = s;
        if (values.GetTrimmed("DefaultPrice") is string price && TryParseMoney(price, out CommerceMoney p)) product.DefaultPrice = p;
        if (values.GetTrimmed("DefaultCost") is string cost && TryParseMoney(cost, out CommerceMoney c)) product.DefaultCost = c;
        if (values.Has("CustomFieldsJson")) product.CustomFieldsJson = values.GetTrimmed("CustomFieldsJson");
    }
}

/// <summary>
/// Import/export handler for <see cref="InventoryItem"/>. Deterministic match key: <c>Sku</c> → <c>Name</c>.
/// </summary>
internal sealed class InventoryItemImportExportHandler : ImportExportHandler<InventoryItem>
{
    public InventoryItemImportExportHandler(SqliteConnectionFactory connectionFactory, IInventoryItemRepository repository)
        : base(connectionFactory, repository)
    {
    }

    public override ImportExportDataType DataType => ImportExportDataType.InventoryItem;

    public override IReadOnlyList<string> Columns { get; } = new[]
    {
        "Id", "WorkspaceId", "Sku", "Name", "ProductId", "ProductVariantId", "UnitId",
        "QuantityAvailable", "ReorderThreshold", "UnitCost", "CustomFieldsJson",
    };

    public override IReadOnlyList<string> RequiredHeaderColumns { get; } = new[] { "Sku", "Name" };

    protected override IReadOnlyList<Func<RowValues, string?>> RowKeyExtractors { get; } = new Func<RowValues, string?>[]
    {
        values => values.GetTrimmed("Sku"),
        values => values.GetTrimmed("Name"),
    };

    protected override IReadOnlyList<Func<InventoryItem, string?>> EntityKeyExtractors { get; } = new Func<InventoryItem, string?>[]
    {
        item => item.Sku,
        item => item.Name,
    };

    protected override IReadOnlyList<string> ToCells(InventoryItem entity) => new[]
    {
        Text(entity.Id),
        Text(entity.WorkspaceId),
        entity.Sku ?? string.Empty,
        entity.Name,
        Text(entity.ProductId),
        Text(entity.ProductVariantId),
        Text(entity.UnitId),
        Text(entity.QuantityAvailable),
        Text(entity.ReorderThreshold),
        Text(entity.UnitCost),
        entity.CustomFieldsJson ?? string.Empty,
    };

    protected override string? ValidateRow(RowValues values)
    {
        if (values.GetTrimmed("Name") is null)
        {
            return "缺少库存项名称（Name）。";
        }

        if (values.GetTrimmed("QuantityAvailable") is string qty && !TryParseDecimal(qty, out _))
        {
            return $"可用数量（QuantityAvailable）的值无效：{qty}。";
        }

        if (values.GetTrimmed("ReorderThreshold") is string threshold && !TryParseDecimal(threshold, out _))
        {
            return $"补货阈值（ReorderThreshold）的值无效：{threshold}。";
        }

        if (values.GetTrimmed("UnitCost") is string unitCost && !TryParseMoney(unitCost, out _))
        {
            return $"单位成本（UnitCost）的值无效：{unitCost}。";
        }

        if (values.GetTrimmed("ProductId") is string product && !TryParseGuidOptional(product, out _))
        {
            return $"商品（ProductId）不是有效的标识：{product}。";
        }

        if (values.GetTrimmed("ProductVariantId") is string variant && !TryParseGuidOptional(variant, out _))
        {
            return $"商品规格（ProductVariantId）不是有效的标识：{variant}。";
        }

        if (values.GetTrimmed("UnitId") is string unit && !TryParseGuidOptional(unit, out _))
        {
            return $"单位（UnitId）不是有效的标识：{unit}。";
        }

        if (values.GetTrimmed("WorkspaceId") is string workspace && !TryParseGuid(workspace, out _))
        {
            return $"工作区（WorkspaceId）不是有效的标识：{workspace}。";
        }

        return null;
    }

    protected override InventoryItem BuildEntity(RowValues values)
    {
        var item = new InventoryItem { WorkspaceId = RequireWorkspaceId(values) };
        ApplyFields(item, values);
        return item;
    }

    protected override void ApplyUpdate(InventoryItem existing, RowValues values) => ApplyFields(existing, values);

    private static void ApplyFields(InventoryItem item, RowValues values)
    {
        item.Name = values.GetTrimmed("Name")!;

        if (values.GetTrimmed("Sku") is string sku) item.Sku = sku;
        if (values.GetTrimmed("ProductId") is string product && TryParseGuidOptional(product, out Guid? productId) && productId is Guid p) item.ProductId = p;
        if (values.GetTrimmed("ProductVariantId") is string variant && TryParseGuidOptional(variant, out Guid? variantId) && variantId is Guid v) item.ProductVariantId = v;
        if (values.GetTrimmed("UnitId") is string unit && TryParseGuidOptional(unit, out Guid? unitId) && unitId is Guid u) item.UnitId = u;
        if (values.GetTrimmed("QuantityAvailable") is string qty && TryParseDecimal(qty, out decimal quantity)) item.QuantityAvailable = quantity;
        if (values.GetTrimmed("ReorderThreshold") is string threshold && TryParseDecimal(threshold, out decimal reorder)) item.ReorderThreshold = reorder;
        if (values.GetTrimmed("UnitCost") is string unitCost && TryParseMoney(unitCost, out CommerceMoney cost)) item.UnitCost = cost;
        if (values.Has("CustomFieldsJson")) item.CustomFieldsJson = values.GetTrimmed("CustomFieldsJson");
    }
}

/// <summary>
/// Import/export handler for <see cref="Customer"/>. Deterministic match key: <c>Phone</c> →
/// <c>WeChat</c> → <c>Name</c>. The service-maintained RFM statistics are exported for completeness
/// but are never applied on import (they are owned by order completion).
/// </summary>
internal sealed class CustomerImportExportHandler : ImportExportHandler<Customer>
{
    public CustomerImportExportHandler(SqliteConnectionFactory connectionFactory, ICommerceCustomerRepository repository)
        : base(connectionFactory, repository)
    {
    }

    public override ImportExportDataType DataType => ImportExportDataType.Customer;

    public override IReadOnlyList<string> Columns { get; } = new[]
    {
        "Id", "WorkspaceId", "Name", "Phone", "WeChat", "Email",
        "LastOrderAt", "CompletedOrderCount", "TotalSpend", "CustomFieldsJson",
    };

    public override IReadOnlyList<string> RequiredHeaderColumns { get; } = new[] { "Phone", "WeChat", "Name" };

    protected override IReadOnlyList<Func<RowValues, string?>> RowKeyExtractors { get; } = new Func<RowValues, string?>[]
    {
        values => values.GetTrimmed("Phone"),
        values => values.GetTrimmed("WeChat"),
        values => values.GetTrimmed("Name"),
    };

    protected override IReadOnlyList<Func<Customer, string?>> EntityKeyExtractors { get; } = new Func<Customer, string?>[]
    {
        customer => customer.Phone,
        customer => customer.WeChat,
        customer => customer.Name,
    };

    protected override IReadOnlyList<string> ToCells(Customer entity) => new[]
    {
        Text(entity.Id),
        Text(entity.WorkspaceId),
        entity.Name,
        entity.Phone ?? string.Empty,
        entity.WeChat ?? string.Empty,
        entity.Email ?? string.Empty,
        Text(entity.LastOrderAt),
        entity.CompletedOrderCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Text(entity.TotalSpend),
        entity.CustomFieldsJson ?? string.Empty,
    };

    protected override string? ValidateRow(RowValues values)
    {
        if (values.GetTrimmed("Name") is null)
        {
            return "缺少客户名称（Name）。";
        }

        if (values.GetTrimmed("WorkspaceId") is string workspace && !TryParseGuid(workspace, out _))
        {
            return $"工作区（WorkspaceId）不是有效的标识：{workspace}。";
        }

        return null;
    }

    protected override Customer BuildEntity(RowValues values)
    {
        var customer = new Customer { WorkspaceId = RequireWorkspaceId(values) };
        ApplyFields(customer, values);
        return customer;
    }

    protected override void ApplyUpdate(Customer existing, RowValues values) => ApplyFields(existing, values);

    private static void ApplyFields(Customer customer, RowValues values)
    {
        customer.Name = values.GetTrimmed("Name")!;

        if (values.GetTrimmed("Phone") is string phone) customer.Phone = phone;
        if (values.GetTrimmed("WeChat") is string weChat) customer.WeChat = weChat;
        if (values.GetTrimmed("Email") is string email) customer.Email = email;
        if (values.Has("CustomFieldsJson")) customer.CustomFieldsJson = values.GetTrimmed("CustomFieldsJson");
    }
}

/// <summary>
/// Import/export handler for <see cref="Order"/>. Deterministic match key: <c>OrderNo</c>.
/// </summary>
internal sealed class OrderImportExportHandler : ImportExportHandler<Order>
{
    public OrderImportExportHandler(SqliteConnectionFactory connectionFactory, ICommerceOrderRepository repository)
        : base(connectionFactory, repository)
    {
    }

    public override ImportExportDataType DataType => ImportExportDataType.Order;

    public override IReadOnlyList<string> Columns { get; } = new[]
    {
        "Id", "WorkspaceId", "OrderNo", "CustomerId", "SalesStage", "PaymentStage", "FulfillmentStage",
        "Subtotal", "Total", "Cost", "GrossProfit", "GrossMargin", "PaidAmount", "ReceivableAmount",
        "OrderedAt", "Note", "CustomFieldsJson",
    };

    public override IReadOnlyList<string> RequiredHeaderColumns { get; } = new[] { "OrderNo" };

    protected override IReadOnlyList<Func<RowValues, string?>> RowKeyExtractors { get; } = new Func<RowValues, string?>[]
    {
        values => values.GetTrimmed("OrderNo"),
    };

    protected override IReadOnlyList<Func<Order, string?>> EntityKeyExtractors { get; } = new Func<Order, string?>[]
    {
        order => order.OrderNo,
    };

    protected override IReadOnlyList<string> ToCells(Order entity) => new[]
    {
        Text(entity.Id),
        Text(entity.WorkspaceId),
        entity.OrderNo ?? string.Empty,
        Text(entity.CustomerId),
        Text(entity.SalesStage),
        Text(entity.PaymentStage),
        Text(entity.FulfillmentStage),
        Text(entity.Subtotal),
        Text(entity.Total),
        Text(entity.Cost),
        Text(entity.GrossProfit),
        Text(entity.GrossMargin),
        Text(entity.PaidAmount),
        Text(entity.ReceivableAmount),
        Text(entity.OrderedAt),
        entity.Note ?? string.Empty,
        entity.CustomFieldsJson ?? string.Empty,
    };

    protected override string? ValidateRow(RowValues values)
    {
        if (values.GetTrimmed("OrderNo") is null)
        {
            return "缺少订单号（OrderNo）。";
        }

        if (values.GetTrimmed("SalesStage") is string sales && !TryParseEnum<OrderSalesStage>(sales, out _))
        {
            return $"销售阶段（SalesStage）的值无效：{sales}。";
        }

        if (values.GetTrimmed("PaymentStage") is string payment && !TryParseEnum<OrderPaymentStage>(payment, out _))
        {
            return $"付款阶段（PaymentStage）的值无效：{payment}。";
        }

        if (values.GetTrimmed("FulfillmentStage") is string fulfillment && !TryParseEnum<OrderFulfillmentStage>(fulfillment, out _))
        {
            return $"履约阶段（FulfillmentStage）的值无效：{fulfillment}。";
        }

        foreach (string moneyColumn in new[] { "Subtotal", "Total", "Cost", "GrossProfit", "PaidAmount", "ReceivableAmount" })
        {
            if (values.GetTrimmed(moneyColumn) is string money && !TryParseMoney(money, out _))
            {
                return $"金额列（{moneyColumn}）的值无效：{money}。";
            }
        }

        if (values.GetTrimmed("GrossMargin") is string grossMargin && !TryParseDecimal(grossMargin, out _))
        {
            return $"毛利率（GrossMargin）的值无效：{grossMargin}。";
        }

        if (values.GetTrimmed("OrderedAt") is string orderedAt && !TryParseDateTime(orderedAt, out _))
        {
            return $"下单时间（OrderedAt）的值无效：{orderedAt}。";
        }

        if (values.GetTrimmed("CustomerId") is string customer && !TryParseGuidOptional(customer, out _))
        {
            return $"客户（CustomerId）不是有效的标识：{customer}。";
        }

        if (values.GetTrimmed("WorkspaceId") is string workspace && !TryParseGuid(workspace, out _))
        {
            return $"工作区（WorkspaceId）不是有效的标识：{workspace}。";
        }

        return null;
    }

    protected override Order BuildEntity(RowValues values)
    {
        var order = new Order { WorkspaceId = RequireWorkspaceId(values) };
        ApplyFields(order, values);
        return order;
    }

    protected override void ApplyUpdate(Order existing, RowValues values) => ApplyFields(existing, values);

    private static void ApplyFields(Order order, RowValues values)
    {
        order.OrderNo = values.GetTrimmed("OrderNo")!;

        if (values.GetTrimmed("CustomerId") is string customer && TryParseGuidOptional(customer, out Guid? customerId) && customerId is Guid c) order.CustomerId = c;
        if (values.GetTrimmed("SalesStage") is string sales && TryParseEnum<OrderSalesStage>(sales, out OrderSalesStage salesStage)) order.SalesStage = salesStage;
        if (values.GetTrimmed("PaymentStage") is string payment && TryParseEnum<OrderPaymentStage>(payment, out OrderPaymentStage paymentStage)) order.PaymentStage = paymentStage;
        if (values.GetTrimmed("FulfillmentStage") is string fulfillment && TryParseEnum<OrderFulfillmentStage>(fulfillment, out OrderFulfillmentStage fulfillmentStage)) order.FulfillmentStage = fulfillmentStage;
        if (values.GetTrimmed("Subtotal") is string subtotal && TryParseMoney(subtotal, out CommerceMoney sub)) order.Subtotal = sub;
        if (values.GetTrimmed("Total") is string total && TryParseMoney(total, out CommerceMoney tot)) order.Total = tot;
        if (values.GetTrimmed("Cost") is string cost && TryParseMoney(cost, out CommerceMoney cst)) order.Cost = cst;
        if (values.GetTrimmed("GrossProfit") is string grossProfit && TryParseMoney(grossProfit, out CommerceMoney gp)) order.GrossProfit = gp;
        if (values.GetTrimmed("GrossMargin") is string grossMargin && TryParseDecimal(grossMargin, out decimal gm)) order.GrossMargin = gm;
        if (values.GetTrimmed("PaidAmount") is string paid && TryParseMoney(paid, out CommerceMoney pa)) order.PaidAmount = pa;
        if (values.GetTrimmed("ReceivableAmount") is string receivable && TryParseMoney(receivable, out CommerceMoney ra)) order.ReceivableAmount = ra;
        if (values.GetTrimmed("OrderedAt") is string orderedAt && TryParseDateTime(orderedAt, out DateTime at)) order.OrderedAt = at;
        if (values.GetTrimmed("Note") is string note) order.Note = note;
        if (values.Has("CustomFieldsJson")) order.CustomFieldsJson = values.GetTrimmed("CustomFieldsJson");
    }
}

/// <summary>
/// Import/export handler for <see cref="CashFlowEntry"/>. Deterministic match key: the composite
/// <c>ImportBatchId</c> + <c>SourceRowKey</c>. A row missing either component cannot match an existing
/// record and is therefore always treated as an Add (design Import section).
/// </summary>
internal sealed class CashFlowEntryImportExportHandler : ImportExportHandler<CashFlowEntry>
{
    private const char KeySeparator = '\u0001';

    public CashFlowEntryImportExportHandler(SqliteConnectionFactory connectionFactory, ICashFlowEntryRepository repository)
        : base(connectionFactory, repository)
    {
    }

    public override ImportExportDataType DataType => ImportExportDataType.CashFlowEntry;

    public override IReadOnlyList<string> Columns { get; } = new[]
    {
        "Id", "WorkspaceId", "Direction", "Amount", "SettledAmount", "SettlementStatus",
        "OccurredAt", "DueDate", "CategoryName", "OrderId", "PaymentRecordId",
        "ImportBatchId", "SourceRowKey", "BusinessKey", "CustomFieldsJson",
    };

    public override IReadOnlyList<string> RequiredHeaderColumns { get; } = new[]
    {
        "ImportBatchId", "SourceRowKey", "Direction", "Amount",
    };

    protected override IReadOnlyList<Func<RowValues, string?>> RowKeyExtractors { get; } = new Func<RowValues, string?>[]
    {
        values => Composite(values.GetTrimmed("ImportBatchId"), values.GetTrimmed("SourceRowKey")),
    };

    protected override IReadOnlyList<Func<CashFlowEntry, string?>> EntityKeyExtractors { get; } = new Func<CashFlowEntry, string?>[]
    {
        entry => Composite(
            string.IsNullOrWhiteSpace(entry.ImportBatchId) ? null : entry.ImportBatchId!.Trim(),
            string.IsNullOrWhiteSpace(entry.SourceRowKey) ? null : entry.SourceRowKey!.Trim()),
    };

    private static string? Composite(string? batchId, string? sourceRowKey)
        => batchId is null || sourceRowKey is null ? null : batchId + KeySeparator + sourceRowKey;

    protected override IReadOnlyList<string> ToCells(CashFlowEntry entity) => new[]
    {
        Text(entity.Id),
        Text(entity.WorkspaceId),
        Text(entity.Direction),
        Text(entity.Amount),
        Text(entity.SettledAmount),
        Text(entity.SettlementStatus),
        Text(entity.OccurredAt),
        Text(entity.DueDate),
        entity.CategoryName ?? string.Empty,
        Text(entity.OrderId),
        Text(entity.PaymentRecordId),
        entity.ImportBatchId ?? string.Empty,
        entity.SourceRowKey ?? string.Empty,
        entity.BusinessKey ?? string.Empty,
        entity.CustomFieldsJson ?? string.Empty,
    };

    protected override string? ValidateRow(RowValues values)
    {
        string? direction = values.GetTrimmed("Direction");
        if (direction is null)
        {
            return "缺少现金流方向（Direction）。";
        }

        if (!TryParseEnum<CashFlowDirection>(direction, out _))
        {
            return $"现金流方向（Direction）的值无效：{direction}。";
        }

        string? amount = values.GetTrimmed("Amount");
        if (amount is null)
        {
            return "缺少金额（Amount）。";
        }

        if (!TryParseMoney(amount, out _))
        {
            return $"金额（Amount）的值无效：{amount}。";
        }

        if (values.GetTrimmed("SettledAmount") is string settled && !TryParseMoney(settled, out _))
        {
            return $"已结算金额（SettledAmount）的值无效：{settled}。";
        }

        if (values.GetTrimmed("SettlementStatus") is string status && !TryParseEnum<CashFlowSettlementStatus>(status, out _))
        {
            return $"结算状态（SettlementStatus）的值无效：{status}。";
        }

        if (values.GetTrimmed("OccurredAt") is string occurredAt && !TryParseDateTime(occurredAt, out _))
        {
            return $"发生时间（OccurredAt）的值无效：{occurredAt}。";
        }

        if (values.GetTrimmed("DueDate") is string dueDate && !TryParseDateTimeOptional(dueDate, out _))
        {
            return $"到期日（DueDate）的值无效：{dueDate}。";
        }

        if (values.GetTrimmed("OrderId") is string order && !TryParseGuidOptional(order, out _))
        {
            return $"订单（OrderId）不是有效的标识：{order}。";
        }

        if (values.GetTrimmed("PaymentRecordId") is string payment && !TryParseGuidOptional(payment, out _))
        {
            return $"付款记录（PaymentRecordId）不是有效的标识：{payment}。";
        }

        if (values.GetTrimmed("WorkspaceId") is string workspace && !TryParseGuid(workspace, out _))
        {
            return $"工作区（WorkspaceId）不是有效的标识：{workspace}。";
        }

        return null;
    }

    protected override CashFlowEntry BuildEntity(RowValues values)
    {
        var entry = new CashFlowEntry
        {
            WorkspaceId = RequireWorkspaceId(values),
            ImportBatchId = values.GetTrimmed("ImportBatchId"),
            SourceRowKey = values.GetTrimmed("SourceRowKey"),
            BusinessKey = values.GetTrimmed("BusinessKey"),
        };
        ApplyFields(entry, values);
        return entry;
    }

    protected override void ApplyUpdate(CashFlowEntry existing, RowValues values) => ApplyFields(existing, values);

    private static void ApplyFields(CashFlowEntry entry, RowValues values)
    {
        if (values.GetTrimmed("Direction") is string direction && TryParseEnum<CashFlowDirection>(direction, out CashFlowDirection dir)) entry.Direction = dir;
        if (values.GetTrimmed("Amount") is string amount && TryParseMoney(amount, out CommerceMoney amt)) entry.Amount = amt;
        if (values.GetTrimmed("SettledAmount") is string settled && TryParseMoney(settled, out CommerceMoney set)) entry.SettledAmount = set;
        if (values.GetTrimmed("SettlementStatus") is string status && TryParseEnum<CashFlowSettlementStatus>(status, out CashFlowSettlementStatus st)) entry.SettlementStatus = st;
        if (values.GetTrimmed("OccurredAt") is string occurredAt && TryParseDateTime(occurredAt, out DateTime occurred)) entry.OccurredAt = occurred;
        if (values.GetTrimmed("DueDate") is string dueDate && TryParseDateTimeOptional(dueDate, out DateTime? due)) entry.DueDate = due;
        if (values.GetTrimmed("CategoryName") is string category) entry.CategoryName = category;
        if (values.GetTrimmed("OrderId") is string order && TryParseGuidOptional(order, out Guid? orderId) && orderId is Guid o) entry.OrderId = o;
        if (values.GetTrimmed("PaymentRecordId") is string payment && TryParseGuidOptional(payment, out Guid? paymentId) && paymentId is Guid p) entry.PaymentRecordId = p;
        if (values.Has("CustomFieldsJson")) entry.CustomFieldsJson = values.GetTrimmed("CustomFieldsJson");
    }
}
