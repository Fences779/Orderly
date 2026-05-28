using CommunityToolkit.Mvvm.Input;
using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    partial void OnStringNarrationListKeywordChanged(string value)
    {
        StringNarrationCurrentPage = 1;
        OnPropertyChanged(nameof(Keyword));
    }

    partial void OnSelectedStringNarrationStatusFilterChanged(string value)
    {
        OnPropertyChanged(nameof(Status));
    }

    partial void OnSelectedStringNarrationFulfillmentStatusFilterChanged(string value)
    {
        OnPropertyChanged(nameof(FulfillmentStatus));
    }

    partial void OnStringNarrationStartAtChanged(long value)
    {
        OnPropertyChanged(nameof(StartAt));
    }

    partial void OnStringNarrationEndAtChanged(long value)
    {
        OnPropertyChanged(nameof(EndAt));
    }

    partial void OnStringNarrationFulfillmentStatsChanged(StringNarrationFulfillmentStats value)
    {
        OnPropertyChanged(nameof(Stats));
        OnPropertyChanged(nameof(StringNarrationStatsCalculatedAtText));
        NotifyStringNarrationWorkbenchDashboardChanged();
    }

    public string Keyword
    {
        get => StringNarrationListKeyword;
        set => StringNarrationListKeyword = value ?? string.Empty;
    }

    public string Status
    {
        get => SelectedStringNarrationStatusFilter;
        set => SelectedStringNarrationStatusFilter = string.IsNullOrWhiteSpace(value) ? "全部" : value.Trim();
    }

    public string FulfillmentStatus
    {
        get => SelectedStringNarrationFulfillmentStatusFilter;
        set => SelectedStringNarrationFulfillmentStatusFilter = string.IsNullOrWhiteSpace(value) ? "全部" : value.Trim();
    }

    public long StartAt
    {
        get => StringNarrationStartAt;
        set => StringNarrationStartAt = value;
    }

    public long EndAt
    {
        get => StringNarrationEndAt;
        set => StringNarrationEndAt = value;
    }

    public StringNarrationFulfillmentStats Stats => StringNarrationFulfillmentStats;

    public string StringNarrationStatsCalculatedAtText => StringNarrationFulfillmentStats is null || StringNarrationFulfillmentStats.CalculatedAt <= 0
        ? "未同步"
        : FormatGatewayTime(StringNarrationFulfillmentStats.CalculatedAt);

    public IAsyncRelayCommand LoadOrders => LoadStringNarrationOrdersCommand;
    public IAsyncRelayCommand LoadStats => LoadStringNarrationStatsCommand;
    public IAsyncRelayCommand RefreshDetail => RefreshStringNarrationOrderDetailCommand;
    public IAsyncRelayCommand UpdateFulfillment => UpdateStringNarrationFulfillmentCommand;
    public IAsyncRelayCommand GenerateProductionOrder => GenerateStringNarrationProductionOrderCommand;

    private void NotifyStringNarrationWorkbenchDashboardChanged()
    {
        OnPropertyChanged(nameof(StringNarrationWorkbenchDashboard));
        OnPropertyChanged(nameof(HasStringNarrationWorkbenchTrendItems));
        OnPropertyChanged(nameof(HasStringNarrationWorkbenchPressureItems));
        OnPropertyChanged(nameof(StringNarrationWorkbenchTodayOrderCount));
        OnPropertyChanged(nameof(StringNarrationWorkbenchTodayOrderCountText));
        OnPropertyChanged(nameof(StringNarrationWorkbenchTodayOrderCountDelta));
        OnPropertyChanged(nameof(StringNarrationWorkbenchTodayOrderCountDeltaText));
        OnPropertyChanged(nameof(IsStringNarrationWorkbenchTodayOrderCountDeltaPositive));
        OnPropertyChanged(nameof(IsStringNarrationWorkbenchTodayOrderCountDeltaZero));
        OnPropertyChanged(nameof(StringNarrationWorkbenchTodayRevenueAmount));
        OnPropertyChanged(nameof(StringNarrationWorkbenchTodayRevenueAmountText));
        OnPropertyChanged(nameof(StringNarrationWorkbenchTodayRevenueAmountDelta));
        OnPropertyChanged(nameof(StringNarrationWorkbenchTodayRevenueAmountDeltaText));
        OnPropertyChanged(nameof(IsStringNarrationWorkbenchTodayRevenueAmountDeltaPositive));
        OnPropertyChanged(nameof(IsStringNarrationWorkbenchTodayRevenueAmountDeltaZero));
        OnPropertyChanged(nameof(StringNarrationWorkbenchPendingMakeCount));
        OnPropertyChanged(nameof(StringNarrationWorkbenchPendingMakeCountText));
        OnPropertyChanged(nameof(StringNarrationWorkbenchPendingMakeDelta));
        OnPropertyChanged(nameof(StringNarrationWorkbenchPendingMakeDeltaText));
        OnPropertyChanged(nameof(IsStringNarrationWorkbenchPendingMakeDeltaPositive));
        OnPropertyChanged(nameof(IsStringNarrationWorkbenchPendingMakeDeltaZero));
        OnPropertyChanged(nameof(StringNarrationWorkbenchReadyToShipCount));
        OnPropertyChanged(nameof(StringNarrationWorkbenchReadyToShipCountText));
        OnPropertyChanged(nameof(StringNarrationWorkbenchReadyToShipDelta));
        OnPropertyChanged(nameof(StringNarrationWorkbenchReadyToShipDeltaText));
        OnPropertyChanged(nameof(IsStringNarrationWorkbenchReadyToShipDeltaPositive));
        OnPropertyChanged(nameof(IsStringNarrationWorkbenchReadyToShipDeltaZero));
        OnPropertyChanged(nameof(StringNarrationWorkbenchExceptionOrderCount));
        OnPropertyChanged(nameof(StringNarrationWorkbenchExceptionOrderCountText));
        OnPropertyChanged(nameof(StringNarrationWorkbenchExceptionOrderDelta));
        OnPropertyChanged(nameof(StringNarrationWorkbenchExceptionOrderDeltaText));
        OnPropertyChanged(nameof(IsStringNarrationWorkbenchExceptionOrderDeltaPositive));
        OnPropertyChanged(nameof(IsStringNarrationWorkbenchExceptionOrderDeltaZero));
        OnPropertyChanged(nameof(StringNarrationWorkbenchUnfinishedOrderCount));
        OnPropertyChanged(nameof(StringNarrationWorkbenchUnfinishedOrderCountText));
        OnPropertyChanged(nameof(StringNarrationWorkbenchInventoryHealthStatusText));
        OnPropertyChanged(nameof(StringNarrationWorkbenchInventoryHealthSummaryText));
        OnPropertyChanged(nameof(StringNarrationWorkbenchInventoryWarningCount));
        OnPropertyChanged(nameof(StringNarrationWorkbenchInventoryWarningCountText));
        OnPropertyChanged(nameof(StringNarrationWorkbenchCashFlowScore));
        OnPropertyChanged(nameof(StringNarrationWorkbenchCashFlowScoreText));
        OnPropertyChanged(nameof(StringNarrationWorkbenchCashFlowStatusText));
        OnPropertyChanged(nameof(StringNarrationWorkbenchCashFlowColorText));
        OnPropertyChanged(nameof(StringNarrationWorkbenchCashFlowEvaluation));
        OnPropertyChanged(nameof(StringNarrationWorkbenchCashFlowDelta));
        OnPropertyChanged(nameof(StringNarrationWorkbenchCashFlowDeltaText));
        OnPropertyChanged(nameof(StringNarrationWorkbenchLastSyncedAtText));
        OnPropertyChanged(nameof(StringNarrationWorkbenchLastSyncedAtFriendlyText));
        OnPropertyChanged(nameof(IsStringNarrationWorkbenchFallbackProjection));
    }
}
