namespace Orderly.Core.Models;

public sealed class StringNarrationBusinessTrendPoint
{
    public string DateKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int OrderCount { get; set; }
    public decimal RevenueAmount { get; set; }

    public string OrderCountText => $"{OrderCount} 单";
    public string RevenueAmountText => $"¥{RevenueAmount:N2}";
}

public sealed class StringNarrationFulfillmentPressureMetric
{
    public string FulfillmentStatus { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
    public int TargetCount { get; set; }
    public decimal Ratio { get; set; }

    public string CountText => $"{Count} 单";
    public string TargetCountText => $"{TargetCount} 单";
    public string RatioText => $"{Ratio:P0}";
}

public sealed class StringNarrationWorkbenchDashboardStats
{
    public int TodayOrderCount { get; set; }
    public int TodayOrderCountDelta { get; set; }
    public decimal TodayRevenueAmount { get; set; }
    public decimal TodayRevenueAmountDelta { get; set; }
    public int PendingMakeCount { get; set; }
    public int PendingMakeDelta { get; set; }
    public int MakingCount { get; set; }
    public int MakingDelta { get; set; }
    public int ReadyToShipCount { get; set; }
    public int ReadyToShipDelta { get; set; }
    public int ExceptionOrderCount { get; set; }
    public int ExceptionOrderDelta { get; set; }
    public int UnfinishedOrderCount { get; set; }
    public string InventoryHealthStatus { get; set; } = string.Empty;
    public string InventoryHealthSummary { get; set; } = string.Empty;
    public int InventoryWarningCount { get; set; }
    public int CashFlowScore { get; set; }
    public string CashFlowStatus { get; set; } = string.Empty;
    public int CashFlowDelta { get; set; }
    public long LastSyncedAt { get; set; }
    public bool IsFallbackProjection { get; set; }
    public IReadOnlyList<StringNarrationBusinessTrendPoint> RecentBusinessTrendItems { get; set; } = [];
    public IReadOnlyList<StringNarrationFulfillmentPressureMetric> FulfillmentPressureItems { get; set; } = [];

    public string TodayOrderCountText => TodayOrderCount.ToString("N0");
    public string TodayOrderCountDeltaText => FormatSignedNumber(TodayOrderCountDelta);
    public string TodayRevenueAmountText => $"¥{TodayRevenueAmount:N2}";
    public string TodayRevenueAmountDeltaText => FormatSignedCurrency(TodayRevenueAmountDelta);
    public string PendingMakeCountText => PendingMakeCount.ToString("N0");
    public string PendingMakeDeltaText => FormatSignedNumber(PendingMakeDelta);
    public string MakingCountText => MakingCount.ToString("N0");
    public string MakingDeltaText => FormatSignedNumber(MakingDelta);
    public string ReadyToShipCountText => ReadyToShipCount.ToString("N0");
    public string ReadyToShipDeltaText => FormatSignedNumber(ReadyToShipDelta);
    public string ExceptionOrderCountText => ExceptionOrderCount.ToString("N0");
    public string ExceptionOrderDeltaText => FormatSignedNumber(ExceptionOrderDelta);
    public string UnfinishedOrderCountText => $"{UnfinishedOrderCount:N0} 单";
    public string InventoryHealthStatusText => string.IsNullOrWhiteSpace(InventoryHealthStatus) ? "未同步" : InventoryHealthStatus.Trim();
    public string InventoryHealthSummaryText => string.IsNullOrWhiteSpace(InventoryHealthSummary) ? "暂无库存健康数据" : InventoryHealthSummary.Trim();
    public string InventoryWarningCountText => $"{InventoryWarningCount:N0} 项";
    public string CashFlowScoreText => CashFlowScore.ToString("N0");
    public string CashFlowStatusText => string.IsNullOrWhiteSpace(CashFlowStatus) ? "暂无现金流数据" : CashFlowStatus.Trim();
    public string CashFlowDeltaText => FormatSignedNumber(CashFlowDelta);

    private static string FormatSignedNumber(int value)
    {
        return value > 0 ? $"+{value:N0}" : value.ToString("N0");
    }

    private static string FormatSignedCurrency(decimal value)
    {
        return value > 0
            ? $"+¥{value:N2}"
            : value < 0
                ? $"-¥{Math.Abs(value):N2}"
                : "¥0.00";
    }
}
