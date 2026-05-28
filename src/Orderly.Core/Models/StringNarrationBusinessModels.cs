namespace Orderly.Core.Models;

public sealed class StringNarrationInventoryQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;
    public string Keyword { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IncludeDisabled { get; set; } = true;
    public bool LowStockOnly { get; set; }
}

public sealed class StringNarrationInventoryListResult
{
    public IReadOnlyList<StringNarrationInventoryItem> Items { get; set; } = [];
    public IReadOnlyList<StringNarrationInventoryMovement> RecentMovements { get; set; } = [];
    public int Total { get; set; }
    public long UpdatedAt { get; set; }
}

public sealed class StringNarrationInventoryItem
{
    public string Id { get; set; } = string.Empty;
    public string SkuId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal BasePrice { get; set; }
    public decimal CostPrice { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal StockOnHand { get; set; }
    public decimal StockReserved { get; set; }
    public decimal SafetyStock { get; set; }
    public string StockUnit { get; set; } = string.Empty;
    public string StockLocation { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string InventoryRemark { get; set; } = string.Empty;
    public bool ReorderEnabled { get; set; }
    public bool Enabled { get; set; } = true;
    public long LastRestockedAt { get; set; }
    public long UpdatedAt { get; set; }
    public IReadOnlyList<string> Tags { get; set; } = [];

    public decimal AvailableStock => Math.Max(0, StockOnHand - StockReserved);
    public bool IsLowStock => SafetyStock > 0 && AvailableStock <= SafetyStock;
    public decimal UnitCost => PurchasePrice > 0 ? PurchasePrice : CostPrice;
    public decimal InventoryValue => StockOnHand * UnitCost;
    public string NameText => BuildValue(Name, "未命名 SKU");
    public string CategoryText => BuildValue(Category, "未分类");
    public string AvailableStockText => $"{AvailableStock:0.##} {BuildValue(StockUnit, "件")}";
    public string StockOnHandText => $"{StockOnHand:0.##} {BuildValue(StockUnit, "件")}";
    public string SafetyStockText => SafetyStock > 0 ? $"{SafetyStock:0.##} {BuildValue(StockUnit, "件")}" : "未设置";
    public string InventoryValueText => FormatCurrency(InventoryValue);
    public string UnitCostText => UnitCost > 0 ? FormatCurrency(UnitCost) : "未记录";
    public string StockLocationText => BuildValue(StockLocation, "未记录库位");
    public string SupplierNameText => BuildValue(SupplierName, "未记录供应商");
    public string StatusText => !Enabled ? "停用" : IsLowStock ? "低库存" : "正常";
    public string TagsText => Tags.Count == 0 ? "无标签" : string.Join(" / ", Tags);

    private static string BuildValue(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string FormatCurrency(decimal value)
    {
        return value == 0 ? "¥0" : $"¥{value:N0}";
    }
}

public sealed class StringNarrationInventoryMovement
{
    public string Id { get; set; } = string.Empty;
    public string SkuId { get; set; } = string.Empty;
    public string SkuName { get; set; } = string.Empty;
    public string MovementType { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    public string RelatedOrderNo { get; set; } = string.Empty;
    public string OperatorId { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public long OccurredAt { get; set; }

    public string MovementTypeText => MovementType switch
    {
        "in" => "入库",
        "out" => "出库",
        "adjust" => "调整",
        "reserve" => "占用",
        "release" => "释放",
        _ => string.IsNullOrWhiteSpace(MovementType) ? "未知" : MovementType
    };
    public string QuantityText => $"{Quantity:0.##}";
    public string TotalCostText => TotalCost == 0 ? "¥0" : $"¥{TotalCost:N0}";
    public string OccurredAtText => FormatTimestamp(OccurredAt);

    private static string FormatTimestamp(long timestamp)
    {
        if (timestamp <= 0)
        {
            return "暂无";
        }

        try
        {
            var milliseconds = timestamp < 10_000_000_000 ? timestamp * 1000 : timestamp;
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).LocalDateTime.ToString("yyyy-MM-dd HH:mm");
        }
        catch (ArgumentOutOfRangeException)
        {
            return "暂无";
        }
    }
}

public sealed class StringNarrationCashflowQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;
    public string Keyword { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public long StartAt { get; set; }
    public long EndAt { get; set; }
}

public sealed class StringNarrationCashflowListResult
{
    public IReadOnlyList<StringNarrationCashflowEntry> Entries { get; set; } = [];
    public StringNarrationCashflowSummary Summary { get; set; } = new();
    public int Total { get; set; }
    public long UpdatedAt { get; set; }
}

public sealed class StringNarrationCashflowSummary
{
    public decimal IncomeTotal { get; set; }
    public decimal ExpenseTotal { get; set; }
    public decimal NetAmount { get; set; }
    public decimal ReceivableAmount { get; set; }
    public decimal PayableAmount { get; set; }
    public int EntryCount { get; set; }

    public string IncomeTotalText => FormatCurrency(IncomeTotal);
    public string ExpenseTotalText => FormatCurrency(ExpenseTotal);
    public string NetAmountText => FormatCurrency(NetAmount);
    public string ReceivableAmountText => FormatCurrency(ReceivableAmount);
    public string PayableAmountText => FormatCurrency(PayableAmount);

    private static string FormatCurrency(decimal value)
    {
        return value == 0 ? "¥0" : $"¥{value:N0}";
    }
}

public sealed class StringNarrationCashflowEntry
{
    public string Id { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Category { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string RelatedOrderId { get; set; } = string.Empty;
    public string RelatedOrderNo { get; set; } = string.Empty;
    public string RelatedQuoteId { get; set; } = string.Empty;
    public string RelatedSkuId { get; set; } = string.Empty;
    public string CounterpartyName { get; set; } = string.Empty;
    public string OperatorId { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public long OccurredAt { get; set; }
    public long CreatedAt { get; set; }

    public bool IsIncome => string.Equals(Direction, "income", StringComparison.OrdinalIgnoreCase);
    public string DirectionText => IsIncome ? "收入" : string.Equals(Direction, "expense", StringComparison.OrdinalIgnoreCase) ? "支出" : "未知";
    public string AmountText => Amount == 0 ? "¥0" : $"¥{Amount:N0}";
    public string CategoryText => string.IsNullOrWhiteSpace(Category) ? "未分类" : Category.Trim();
    public string PaymentMethodText => string.IsNullOrWhiteSpace(PaymentMethod) ? "未记录" : PaymentMethod.Trim();
    public string StatusText => string.IsNullOrWhiteSpace(Status) ? "未确认" : Status.Trim();
    public string RelatedOrderNoText => string.IsNullOrWhiteSpace(RelatedOrderNo) ? "无关联订单" : RelatedOrderNo.Trim();
    public string CounterpartyNameText => string.IsNullOrWhiteSpace(CounterpartyName) ? "未记录对象" : CounterpartyName.Trim();
    public string OccurredAtText => FormatTimestamp(OccurredAt);

    private static string FormatTimestamp(long timestamp)
    {
        if (timestamp <= 0)
        {
            return "暂无";
        }

        try
        {
            var milliseconds = timestamp < 10_000_000_000 ? timestamp * 1000 : timestamp;
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).LocalDateTime.ToString("yyyy-MM-dd HH:mm");
        }
        catch (ArgumentOutOfRangeException)
        {
            return "暂无";
        }
    }
}
