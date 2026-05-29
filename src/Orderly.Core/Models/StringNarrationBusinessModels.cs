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

public sealed class StringNarrationInventoryManagementDashboardRequest
{
    public string Keyword { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string SortBy { get; set; } = "sold30dRatio";
    public string SortDirection { get; set; } = "desc";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public sealed class StringNarrationInventoryManagementDashboardResult
{
    public long UpdatedAt { get; set; }
    public StringNarrationBusinessDataAvailability DataAvailability { get; set; } = new();
    public StringNarrationInventoryManagementDashboardSummary Summary { get; set; } = new();
    public StringNarrationInventoryManagementDashboardFilterOptions FilterOptions { get; set; } = new();
    public IReadOnlyList<StringNarrationInventoryManagementDashboardItem> Items { get; set; } = [];
    public StringNarrationInventoryManagementDashboardPageInfo PageInfo { get; set; } = new();
}

public sealed class StringNarrationBusinessDataAvailability
{
    public StringNarrationBusinessDataAvailabilityItem CashBalance { get; set; } = new();
    public StringNarrationBusinessDataAvailabilityItem Receivable { get; set; } = new();
    public StringNarrationBusinessDataAvailabilityItem Payable { get; set; } = new();
    public StringNarrationBusinessDataAvailabilityItem InventorySource { get; set; } = new();
    public StringNarrationBusinessDataAvailabilityItem MaterialConsumption { get; set; } = new();
}

public sealed class StringNarrationBusinessDataAvailabilityItem
{
    public string Status { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;

    public bool IsUnavailable => string.Equals(Status, "unavailable", StringComparison.OrdinalIgnoreCase);
    public bool IsCompat => string.Equals(Status, "compat", StringComparison.OrdinalIgnoreCase);
    public bool IsAvailable => !IsUnavailable && !IsCompat;
}

public sealed class StringNarrationInventoryManagementDashboardSummary
{
    public decimal? AvgOrderMaterialUsage { get; set; }
    public decimal? AvgMaterialUnitCost { get; set; }
    public decimal? AvgBraceletSalePrice { get; set; }
    public decimal? AvgBraceletCostPrice { get; set; }
    public decimal? GrossMarginRate { get; set; }
    public int LowStockCount { get; set; }
    public int FastSellingCount { get; set; }
    public int LowSellingCount { get; set; }
    public string InventoryHealthStatus { get; set; } = string.Empty;
    public string InventoryHealthSummary { get; set; } = string.Empty;
    public int InventoryWarningCount { get; set; }
}

public sealed class StringNarrationInventoryManagementDashboardFilterOption
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public sealed class StringNarrationInventoryManagementDashboardFilterOptions
{
    public IReadOnlyList<string> Categories { get; set; } = [];
    public IReadOnlyList<StringNarrationInventoryManagementDashboardFilterOption> Statuses { get; set; } = [];
    public string DefaultSortBy { get; set; } = "sold30dRatio";
    public string DefaultSortDirection { get; set; } = "desc";
}

public sealed class StringNarrationInventoryManagementDashboardItem
{
    public string MaterialId { get; set; } = string.Empty;
    public string MaterialName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal CurrentStockQty { get; set; }
    public string StockUnit { get; set; } = string.Empty;
    public decimal Sold7dQty { get; set; }
    public decimal Sold7dRatio { get; set; }
    public decimal Sold30dQty { get; set; }
    public decimal Sold30dRatio { get; set; }
    public decimal Consumed7dQty { get; set; }
    public decimal Consumed30dQty { get; set; }
    public decimal? SafeStockSuggestedQty { get; set; }
    public string Status { get; set; } = string.Empty;
    public string StatusLabel { get; set; } = string.Empty;
    public decimal? UnitCost { get; set; }
    public long LastRestockedAt { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
}

public sealed class StringNarrationInventoryManagementDashboardPageInfo
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public int Total { get; set; }
    public int TotalPages { get; set; }
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

public sealed class StringNarrationCashflowHealthDashboardRequest
{
    public string Range { get; set; } = "30d";
    public long StartAt { get; set; }
    public long EndAt { get; set; }
}

public sealed class StringNarrationCashflowHealthDashboardResult
{
    public string Range { get; set; } = string.Empty;
    public long StartAt { get; set; }
    public long EndAt { get; set; }
    public long UpdatedAt { get; set; }
    public StringNarrationBusinessDataAvailability DataAvailability { get; set; } = new();
    public StringNarrationCashflowHealthDashboardSummary Summary { get; set; } = new();
    public IReadOnlyList<StringNarrationCashflowHealthDashboardTrendItem> TrendItems { get; set; } = [];
    public StringNarrationCashflowHealthDashboardBreakdown IncomeBreakdown { get; set; } = new();
    public StringNarrationCashflowHealthDashboardBreakdown ExpenseBreakdown { get; set; } = new();
    public IReadOnlyList<StringNarrationCashflowHealthDashboardUpcomingCashItem> UpcomingCashItems { get; set; } = [];
    public StringNarrationCashflowHealthDashboardAdvice Advice { get; set; } = new();
}

public sealed class StringNarrationCashflowHealthDashboardSummary
{
    public int? CashFlowHealthScore { get; set; }
    public string CashFlowHealthLevel { get; set; } = string.Empty;
    public string CashFlowHealthSummary { get; set; } = string.Empty;
    public decimal? CashBalanceAmount { get; set; }
    public decimal? AvailableCashAmount { get; set; }
    public decimal? ReceivableAmount { get; set; }
    public decimal? PayableAmount { get; set; }
    public decimal AvgDailyExpense7d { get; set; }
    public int? SupportDays { get; set; }
}

public sealed class StringNarrationCashflowHealthDashboardTrendItem
{
    public string Date { get; set; } = string.Empty;
    public decimal IncomeAmount { get; set; }
    public decimal ExpenseAmount { get; set; }
    public decimal NetCashflowAmount { get; set; }
}

public sealed class StringNarrationCashflowHealthDashboardBreakdown
{
    public decimal TotalAmount { get; set; }
    public IReadOnlyList<StringNarrationCashflowHealthDashboardBreakdownItem> Items { get; set; } = [];
}

public sealed class StringNarrationCashflowHealthDashboardBreakdownItem
{
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal Percent { get; set; }
}

public sealed class StringNarrationCashflowHealthDashboardUpcomingCashItem
{
    public string Type { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int Count { get; set; }
    public string Note { get; set; } = string.Empty;
    public bool IsCompatPlaceholder { get; set; }
}

public sealed class StringNarrationCashflowHealthDashboardAdvice
{
    public string HealthTitle { get; set; } = string.Empty;
    public string HealthDescription { get; set; } = string.Empty;
    public decimal? RestockSuggestionAmount { get; set; }
    public string RiskHint { get; set; } = string.Empty;
    public IReadOnlyList<string> NextFocus { get; set; } = [];
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
    public decimal? ReceivableAmount { get; set; }
    public decimal? PayableAmount { get; set; }
    public int EntryCount { get; set; }

    public string IncomeTotalText => FormatCurrency(IncomeTotal);
    public string ExpenseTotalText => FormatCurrency(ExpenseTotal);
    public string NetAmountText => FormatCurrency(NetAmount);
    public string ReceivableAmountText => FormatCurrency(ReceivableAmount);
    public string PayableAmountText => FormatCurrency(PayableAmount);

    private static string FormatCurrency(decimal? value)
    {
        if (!value.HasValue)
        {
            return "不可得";
        }

        return value.Value == 0 ? "¥0" : $"¥{value.Value:N0}";
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
