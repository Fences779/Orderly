using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

// Read-only workbench dashboard projection properties. Pure getters over
// StringNarrationWorkbenchDashboard; no side effects, no gateway, no transaction logic.
public partial class MainViewModel
{
    public StringNarrationWorkbenchDashboardStats StringNarrationWorkbenchDashboard => StringNarrationFulfillmentStats.WorkbenchDashboard;
    public int StringNarrationWorkbenchTodayOrderCount => StringNarrationWorkbenchDashboard.TodayOrderCount;
    public string StringNarrationWorkbenchTodayOrderCountText => StringNarrationWorkbenchDashboard.TodayOrderCountText;
    public int StringNarrationWorkbenchTodayOrderCountDelta => StringNarrationWorkbenchDashboard.TodayOrderCountDelta;
    public string StringNarrationWorkbenchTodayOrderCountDeltaText => StringNarrationWorkbenchDashboard.TodayOrderCountDeltaText;
    public decimal StringNarrationWorkbenchTodayRevenueAmount => StringNarrationWorkbenchDashboard.TodayRevenueAmount;
    public string StringNarrationWorkbenchTodayRevenueAmountText => StringNarrationWorkbenchDashboard.TodayRevenueAmountText;
    public decimal StringNarrationWorkbenchTodayRevenueAmountDelta => StringNarrationWorkbenchDashboard.TodayRevenueAmountDelta;
    public string StringNarrationWorkbenchTodayRevenueAmountDeltaText => StringNarrationWorkbenchDashboard.TodayRevenueAmountDeltaText;
    public int StringNarrationWorkbenchPendingMakeCount => StringNarrationWorkbenchDashboard.PendingMakeCount;
    public string StringNarrationWorkbenchPendingMakeCountText => StringNarrationWorkbenchDashboard.PendingMakeCountText;
    public int StringNarrationWorkbenchPendingMakeDelta => StringNarrationWorkbenchDashboard.PendingMakeDelta;
    public string StringNarrationWorkbenchPendingMakeDeltaText => StringNarrationWorkbenchDashboard.PendingMakeDeltaText;
    public int StringNarrationWorkbenchReadyToShipCount => StringNarrationWorkbenchDashboard.ReadyToShipCount;
    public string StringNarrationWorkbenchReadyToShipCountText => StringNarrationWorkbenchDashboard.ReadyToShipCountText;
    public int StringNarrationWorkbenchReadyToShipDelta => StringNarrationWorkbenchDashboard.ReadyToShipDelta;
    public string StringNarrationWorkbenchReadyToShipDeltaText => StringNarrationWorkbenchDashboard.ReadyToShipDeltaText;
    public int StringNarrationWorkbenchExceptionOrderCount => StringNarrationWorkbenchDashboard.ExceptionOrderCount;
    public string StringNarrationWorkbenchExceptionOrderCountText => StringNarrationWorkbenchDashboard.ExceptionOrderCountText;
    public int StringNarrationWorkbenchExceptionOrderDelta => StringNarrationWorkbenchDashboard.ExceptionOrderDelta;
    public string StringNarrationWorkbenchExceptionOrderDeltaText => StringNarrationWorkbenchDashboard.ExceptionOrderDeltaText;
    public int StringNarrationWorkbenchUnfinishedOrderCount => StringNarrationWorkbenchDashboard.UnfinishedOrderCount;
    public string StringNarrationWorkbenchUnfinishedOrderCountText => StringNarrationWorkbenchDashboard.UnfinishedOrderCountText;
    public string StringNarrationWorkbenchInventoryHealthStatusText => StringNarrationWorkbenchDashboard.InventoryHealthStatusText;
    public string StringNarrationWorkbenchInventoryHealthSummaryText => StringNarrationWorkbenchDashboard.InventoryHealthSummaryText;
    public int StringNarrationWorkbenchInventoryWarningCount => StringNarrationWorkbenchDashboard.InventoryWarningCount;
    public string StringNarrationWorkbenchInventoryWarningCountText => StringNarrationWorkbenchDashboard.InventoryWarningCountText;
    public int StringNarrationWorkbenchCashFlowScore => StringNarrationWorkbenchDashboard.CashFlowScore;
    public string StringNarrationWorkbenchCashFlowScoreText => StringNarrationWorkbenchDashboard.CashFlowScoreText;
    public string StringNarrationWorkbenchCashFlowStatusText => StringNarrationWorkbenchDashboard.CashFlowStatusText;
    public int StringNarrationWorkbenchCashFlowDelta => StringNarrationWorkbenchDashboard.CashFlowDelta;
    public string StringNarrationWorkbenchCashFlowDeltaText => StringNarrationWorkbenchDashboard.CashFlowDeltaText;
    public string StringNarrationWorkbenchLastSyncedAtText => StringNarrationWorkbenchDashboard.LastSyncedAt <= 0
        ? "未同步"
        : FormatGatewayTime(StringNarrationWorkbenchDashboard.LastSyncedAt);
    public bool IsStringNarrationWorkbenchFallbackProjection => StringNarrationWorkbenchDashboard.IsFallbackProjection;

    public bool IsStringNarrationWorkbenchTodayOrderCountDeltaPositive => StringNarrationWorkbenchTodayOrderCountDelta > 0;
    public bool IsStringNarrationWorkbenchTodayOrderCountDeltaZero => StringNarrationWorkbenchTodayOrderCountDelta == 0;

    public bool IsStringNarrationWorkbenchTodayRevenueAmountDeltaPositive => StringNarrationWorkbenchTodayRevenueAmountDelta > 0;
    public bool IsStringNarrationWorkbenchTodayRevenueAmountDeltaZero => StringNarrationWorkbenchTodayRevenueAmountDelta == 0;

    public bool IsStringNarrationWorkbenchPendingMakeDeltaPositive => StringNarrationWorkbenchPendingMakeDelta > 0;
    public bool IsStringNarrationWorkbenchPendingMakeDeltaZero => StringNarrationWorkbenchPendingMakeDelta == 0;

    public bool IsStringNarrationWorkbenchReadyToShipDeltaPositive => StringNarrationWorkbenchReadyToShipDelta > 0;
    public bool IsStringNarrationWorkbenchReadyToShipDeltaZero => StringNarrationWorkbenchReadyToShipDelta == 0;

    public bool IsStringNarrationWorkbenchExceptionOrderDeltaPositive => StringNarrationWorkbenchExceptionOrderDelta > 0;
    public bool IsStringNarrationWorkbenchExceptionOrderDeltaZero => StringNarrationWorkbenchExceptionOrderDelta == 0;

    public string StringNarrationWorkbenchCashFlowColorText
    {
        get
        {
            var score = StringNarrationWorkbenchCashFlowScore;
            if (score < 15) return "#DE4C4A"; // 红色
            if (score <= 30) return "#D97A26"; // 橙色
            if (score <= 90) return "#2B7A5C"; // 绿色
            return "#D4AF37"; // 金色
        }
    }

    public string StringNarrationWorkbenchCashFlowEvaluation
    {
        get
        {
            var score = StringNarrationWorkbenchCashFlowScore;
            if (score < 15) return "危险";
            if (score <= 30) return "预警";
            if (score <= 90) return "健康";
            return "充裕";
        }
    }

    public string StringNarrationWorkbenchLastSyncedAtFriendlyText
    {
        get
        {
            var timestamp = StringNarrationWorkbenchDashboard.LastSyncedAt;
            if (timestamp <= 0)
            {
                return "未同步";
            }

            try
            {
                var milliseconds = timestamp < 10_000_000_000 ? timestamp * 1000 : timestamp;
                var dt = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).LocalDateTime;
                var now = DateTime.Now;

                if (dt.Date == now.Date)
                {
                    var diff = now - dt;
                    if (diff.TotalMilliseconds < 0)
                    {
                        return dt.ToString("HH:mm");
                    }
                    if (diff.TotalMinutes < 60)
                    {
                        var mins = (int)diff.TotalMinutes;
                        return mins <= 0 ? "刚刚" : $"{mins}分钟前";
                    }
                    if (diff.TotalHours < 3)
                    {
                        return $"{(int)diff.TotalHours}小时前";
                    }
                    return dt.ToString("HH:mm");
                }
                else
                {
                    return dt.ToString("M.d HH:mm");
                }
            }
            catch
            {
                return "未同步";
            }
        }
    }
}
