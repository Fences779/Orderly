using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    private const string StringNarrationLeftPaneOrderList = "OrderList";
    private const string StringNarrationLeftPaneProductionSheet = "ProductionSheet";
    private bool _isSynchronizingStringNarrationSelection;
    private bool _hasDismissedStringNarrationDetailsThisSession;
    private bool _isDetectingCarrier;
    private bool _hasLoadedStringNarrationOnce;

    public ObservableCollection<StringNarrationOrderSummary> StringNarrationOrders { get; } = new();
    public ObservableCollection<StringNarrationOrderItemSnapshot> StringNarrationOrderItems { get; } = new();
    public ObservableCollection<StringNarrationStatusLog> StringNarrationStatusLogs { get; } = new();
    public ObservableCollection<StringNarrationWorkOrderSnapshot> StringNarrationWorkOrders { get; } = new();
    public ObservableCollection<StringNarrationProductionSheetMaterialItem> StringNarrationProductionSheetMaterials { get; } = new();
    public ObservableCollection<StringNarrationFulfillmentStatusMetric> StringNarrationFulfillmentMetrics { get; } = new();
    public ObservableCollection<StringNarrationBusinessTrendPoint> StringNarrationWorkbenchTrendItems { get; } = new();
    public ObservableCollection<StringNarrationFulfillmentPressureMetric> StringNarrationWorkbenchPressureItems { get; } = new();
    public ObservableCollection<string> StringNarrationStatusFilterOptions { get; } = new(new[]
    {
        "全部",
        "paid",
        "pending_payment",
        "closed",
        "refunded"
    });
    public ObservableCollection<string> StringNarrationFulfillmentStatusFilterOptions { get; } = new(new[]
    {
        "全部",
        "paid_pending_confirm",
        "pending_make",
        "making",
        "ready_to_ship",
        "shipped",
        "exception",
        "completed"
    });
    public ObservableCollection<string> StringNarrationFulfillmentStatusOptions { get; } = new(new[]
    {
        "paid_pending_confirm",
        "pending_make",
        "making",
        "ready_to_ship",
        "shipped",
        "exception",
        "completed"
    });

    public string StringNarrationGatewayEndpoint { get; }
    public bool IsStringNarrationGatewayTokenConfigured { get; }
    public bool IsStringNarrationGatewayEndpointConfigured { get; }
    public int StringNarrationGatewayTimeoutSeconds { get; }
    public string StringNarrationGatewayEndpointStatus => IsStringNarrationGatewayEndpointConfigured ? "endpoint 已配置" : "endpoint 未配置";
    public string StringNarrationGatewayTokenStatus => IsStringNarrationGatewayTokenConfigured ? "token 已配置" : "token 未配置";
    public string StringNarrationGatewayTimeoutText => $"{StringNarrationGatewayTimeoutSeconds} 秒";
    public string StringNarrationGatewayConfigurationPrompt => IsStringNarrationGatewayEndpointConfigured && IsStringNarrationGatewayTokenConfigured
        ? "配置完整，可调用 adminPcGateway。"
        : "需要配置 ADMIN_PC_GATEWAY_ENDPOINT / ADMIN_PC_GATEWAY_TOKEN 后才能调用；页面不会显示 token 明文。";
    public bool HasStringNarrationOrders => StringNarrationOrders.Count > 0;
    public bool HasSelectedStringNarrationOrderDetail => SelectedStringNarrationOrderDetail is not null;
    public bool IsStringNarrationDetailPanelVisible => SelectedStringNarrationOrderDetail is not null;
    public bool IsStringNarrationListExpanded => SelectedStringNarrationOrderDetail is null;
    public bool IsStringNarrationOrderListVisible => SelectedStringNarrationOrderDetail is null || string.Equals(StringNarrationLeftPaneMode, StringNarrationLeftPaneOrderList, StringComparison.Ordinal);
    public bool IsStringNarrationProductionSheetVisible => SelectedStringNarrationOrderDetail is not null && string.Equals(StringNarrationLeftPaneMode, StringNarrationLeftPaneProductionSheet, StringComparison.Ordinal);
    public bool HasStringNarrationOrderItems => StringNarrationOrderItems.Count > 0;
    public bool HasStringNarrationStatusLogs => StringNarrationStatusLogs.Count > 0;
    public bool HasStringNarrationWorkOrders => StringNarrationWorkOrders.Count > 0;
    public bool HasStringNarrationProductionSheet => SelectedStringNarrationProductionSheet?.HasDisplayableContent == true;
    public bool HasStringNarrationProductionSheetMaterials => StringNarrationProductionSheetMaterials.Count > 0;
    public bool HasStringNarrationFulfillmentMetrics => StringNarrationFulfillmentMetrics.Count > 0;
    public bool HasStringNarrationWorkbenchTrendItems => StringNarrationWorkbenchTrendItems.Count > 0;
    public bool HasStringNarrationWorkbenchPressureItems => StringNarrationWorkbenchPressureItems.Count > 0;
    public bool IsStringNarrationBusy => IsStringNarrationLoading || IsStringNarrationSaving || IsStringNarrationProductionOrderErrorVisible;
    public bool IsStringNarrationWorkAreaBusy => IsStringNarrationLoading || (IsStringNarrationSaving && !IsStringNarrationGeneratingProductionOrder);
    public bool IsStringNarrationDetailBusyVisible => IsStringNarrationBusy && !IsStringNarrationProductionOrderOverlayVisible;
    public bool IsStringNarrationProductionOrderOverlayVisible => IsStringNarrationGeneratingProductionOrder || IsStringNarrationProductionOrderErrorVisible;
    public string StringNarrationOrdersCountText => $"{StringNarrationOrders.Count} 单";
    public string StringNarrationStatsTotalText => $"{StringNarrationFulfillmentStats.TotalCount} 单";
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
    
    [ObservableProperty]
    private string stringNarrationWorkbenchInventoryTestStatus = "需注意";

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

    [RelayCommand]
    private void ToggleInventoryTestStatus()
    {
        StringNarrationWorkbenchInventoryTestStatus = StringNarrationWorkbenchInventoryTestStatus switch
        {
            "健康" => "需注意",
            "需注意" => "警告",
            "警告" => "健康",
            _ => "需注意"
        };
    }
    public string StringNarrationEmptyStateText => string.IsNullOrWhiteSpace(StringNarrationError)
        ? "暂无串述订单，点击刷新从 adminPcGateway 拉取。"
        : StringNarrationError;
    public string StringNarrationSelectedTitle => string.IsNullOrWhiteSpace(SelectedStringNarrationOrderDetail?.TitleSnapshot)
        ? "未选择串述订单"
        : SelectedStringNarrationOrderDetail.TitleSnapshot;
    public string StringNarrationSelectedOrderNo => string.IsNullOrWhiteSpace(SelectedStringNarrationOrderDetail?.OrderNo)
        ? "无 orderNo"
        : SelectedStringNarrationOrderDetail.OrderNo;
    public string StringNarrationSelectedTradeNo => string.IsNullOrWhiteSpace(SelectedStringNarrationOrderDetail?.WxOutTradeNo)
        ? "无 tradeNo"
        : SelectedStringNarrationOrderDetail.WxOutTradeNo;
    public string StringNarrationSelectedAmountText => SelectedStringNarrationOrderDetail is null
        ? "¥0"
        : $"¥{SelectedStringNarrationOrderDetail.Amount:N0}";
    public string StringNarrationSelectedPaidAtText => FormatGatewayTime(SelectedStringNarrationOrderDetail?.PaidAt ?? 0);
    public string StringNarrationSelectedCreatedAtText => FormatGatewayTime(SelectedStringNarrationOrderDetail?.CreatedAt ?? 0);
    public string StringNarrationAddressText => SelectedStringNarrationOrderDetail?.Address is null
        ? "暂无收件信息"
        : BuildAddressText(SelectedStringNarrationOrderDetail.Address);
    public string StringNarrationRemarkText => string.IsNullOrWhiteSpace(SelectedStringNarrationOrderDetail?.Remark)
        ? "暂无买家备注"
        : SelectedStringNarrationOrderDetail.Remark;
    public string StringNarrationShippingStateText => SelectedStringNarrationOrderDetail is null
        ? "暂无履约信息"
        : $"支付/订单状态：{SelectedStringNarrationOrderDetail.StatusText} / 履约状态：{SelectedStringNarrationOrderDetail.FulfillmentStatusLabel} ({SelectedStringNarrationOrderDetail.FulfillmentStatus}) / 微信发货同步：{SelectedStringNarrationOrderDetail.WxShippingSyncStatusText}";
    public string StringNarrationTrackingText => SelectedStringNarrationOrderDetail is null
        ? "暂无物流"
        : $"{BuildValue(SelectedStringNarrationOrderDetail.Carrier, "未填快递公司")} {BuildValue(SelectedStringNarrationOrderDetail.ExpressCompanyCode, "未填编码")} {BuildValue(SelectedStringNarrationOrderDetail.TrackingNo, "未填单号")}";
    public string StringNarrationFulfillmentTimeText => SelectedStringNarrationOrderDetail is null
        ? "暂无履约时间"
        : $"shippedAt：{SelectedStringNarrationOrderDetail.ShippedAtText} / completedAt：{SelectedStringNarrationOrderDetail.CompletedAtText} / fulfillmentUpdatedAt：{SelectedStringNarrationOrderDetail.FulfillmentUpdatedAtText}";
    public string StringNarrationDetailOrderNo => SelectedStringNarrationOrderDetail?.OrderNoText ?? "无 orderNo";
    public string StringNarrationDetailTransactionId => SelectedStringNarrationOrderDetail?.WxTransactionIdText ?? "无 wxTransactionId";
    public string StringNarrationDetailStatus => SelectedStringNarrationOrderDetail is null
        ? "未选择订单"
        : $"{SelectedStringNarrationOrderDetail.StatusText} / {SelectedStringNarrationOrderDetail.FulfillmentStatusLabel}";
    public string StringNarrationDetailProduct => SelectedStringNarrationOrderDetail is null
        ? "暂无商品信息"
        : $"{SelectedStringNarrationOrderDetail.TitleSnapshotText} / {SelectedStringNarrationOrderDetail.ItemsSnapshotStateText}";
    public string StringNarrationDetailReceiver => SelectedStringNarrationOrderDetail is null
        ? "暂无收货信息"
        : $"{SelectedStringNarrationOrderDetail.ReceiverSummaryText}{Environment.NewLine}{SelectedStringNarrationOrderDetail.FullAddressText}";
    public string StringNarrationDetailProduction => SelectedStringNarrationOrderDetail?.ProductionOrderSummaryText ?? "暂无制作单";
    public string StringNarrationProductionSheetOrderNoText => SelectedStringNarrationOrderDetail?.OrderNoText ?? "无 orderNo";
    public string StringNarrationProductionSheetProductionOrderNoText => SelectedStringNarrationProductionSheet?.ProductionOrderNoText ?? "无制作单号";
    public string StringNarrationProductionSheetWorkOrderNoText => SelectedStringNarrationProductionSheet?.WorkOrderNoText ?? "无工单号";
    public string StringNarrationProductionSheetStatusText => SelectedStringNarrationProductionSheet?.WorkOrderStatusText ?? "未知工单状态";
    public string StringNarrationProductionSheetArrangementText => SelectedStringNarrationProductionSheet?.ArrangementDisplayText ?? "未提供排列方式";
    public string StringNarrationProductionSheetRemarkText => SelectedStringNarrationProductionSheet?.RemarkText ?? "无制作备注";
    public string StringNarrationProductionSheetExampleImageUrl => SelectedStringNarrationProductionSheet?.ExampleImageUrl ?? string.Empty;
    public string StringNarrationProductionSheetExampleFallbackText => SelectedStringNarrationProductionSheet?.ExampleImageFallbackText ?? "未提供例图";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStringNarrationBusy))]
    [NotifyPropertyChangedFor(nameof(IsStringNarrationWorkAreaBusy))]
    [NotifyPropertyChangedFor(nameof(IsStringNarrationDetailBusyVisible))]
    [NotifyCanExecuteChangedFor(nameof(LoadStringNarrationOrdersCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadStringNarrationStatsCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestStringNarrationGatewayCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyStringNarrationFulfillmentFilterCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearStringNarrationFiltersCommand))]
    [NotifyCanExecuteChangedFor(nameof(SearchStringNarrationOrderDetailCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshStringNarrationOrderDetailCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateStringNarrationFulfillmentCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerateStringNarrationProductionOrderCommand))]
    [NotifyCanExecuteChangedFor(nameof(NavigateStringNarrationPrevPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NavigateStringNarrationNextPageCommand))]
    private bool isStringNarrationLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStringNarrationBusy))]
    [NotifyPropertyChangedFor(nameof(IsStringNarrationWorkAreaBusy))]
    [NotifyPropertyChangedFor(nameof(IsStringNarrationDetailBusyVisible))]
    [NotifyCanExecuteChangedFor(nameof(LoadStringNarrationOrdersCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadStringNarrationStatsCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestStringNarrationGatewayCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyStringNarrationFulfillmentFilterCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearStringNarrationFiltersCommand))]
    [NotifyCanExecuteChangedFor(nameof(SearchStringNarrationOrderDetailCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshStringNarrationOrderDetailCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateStringNarrationFulfillmentCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerateStringNarrationProductionOrderCommand))]
    [NotifyCanExecuteChangedFor(nameof(NavigateStringNarrationPrevPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NavigateStringNarrationNextPageCommand))]
    private bool isStringNarrationSaving;

    [ObservableProperty]
    private bool isStringNarrationInitializing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StringNarrationEmptyStateText))]
    private string stringNarrationError = string.Empty;

    [ObservableProperty]
    private string stringNarrationStatusMessage = "串述订单未加载";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStringNarrationDetailBusyVisible))]
    [NotifyPropertyChangedFor(nameof(IsStringNarrationProductionOrderOverlayVisible))]
[NotifyPropertyChangedFor(nameof(IsStringNarrationWorkAreaBusy))]
    private bool isStringNarrationGeneratingProductionOrder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStringNarrationBusy))]
    [NotifyPropertyChangedFor(nameof(IsStringNarrationDetailBusyVisible))]
    [NotifyPropertyChangedFor(nameof(IsStringNarrationProductionOrderOverlayVisible))]
    [NotifyCanExecuteChangedFor(nameof(LoadStringNarrationOrdersCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadStringNarrationStatsCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestStringNarrationGatewayCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyStringNarrationFulfillmentFilterCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearStringNarrationFiltersCommand))]
    [NotifyCanExecuteChangedFor(nameof(SearchStringNarrationOrderDetailCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshStringNarrationOrderDetailCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateStringNarrationFulfillmentCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerateStringNarrationProductionOrderCommand))]
    private bool isStringNarrationProductionOrderErrorVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StringNarrationStatsTotalText))]
    private StringNarrationFulfillmentStats stringNarrationFulfillmentStats = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchStringNarrationOrderDetailCommand))]
    private string stringNarrationLookupInput = string.Empty;

    [ObservableProperty]
    private string stringNarrationListKeyword = string.Empty;

    [ObservableProperty]
    private string selectedStringNarrationStatusFilter = "全部";

    [ObservableProperty]
    private string selectedStringNarrationFulfillmentStatusFilter = "全部";

    [ObservableProperty]
    private long stringNarrationStartAt;

    [ObservableProperty]
    private long stringNarrationEndAt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedStringNarrationOrderDetail))]
    [NotifyPropertyChangedFor(nameof(IsStringNarrationDetailPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsStringNarrationListExpanded))]
    [NotifyPropertyChangedFor(nameof(StringNarrationSelectedTitle))]
    [NotifyPropertyChangedFor(nameof(StringNarrationSelectedOrderNo))]
    [NotifyPropertyChangedFor(nameof(StringNarrationSelectedTradeNo))]
    [NotifyPropertyChangedFor(nameof(StringNarrationSelectedAmountText))]
    [NotifyPropertyChangedFor(nameof(StringNarrationSelectedPaidAtText))]
    [NotifyPropertyChangedFor(nameof(StringNarrationSelectedCreatedAtText))]
    [NotifyPropertyChangedFor(nameof(StringNarrationAddressText))]
    [NotifyPropertyChangedFor(nameof(StringNarrationRemarkText))]
    [NotifyPropertyChangedFor(nameof(StringNarrationShippingStateText))]
    [NotifyPropertyChangedFor(nameof(StringNarrationTrackingText))]
    [NotifyPropertyChangedFor(nameof(StringNarrationFulfillmentTimeText))]
    [NotifyPropertyChangedFor(nameof(StringNarrationDetailOrderNo))]
    [NotifyPropertyChangedFor(nameof(StringNarrationDetailTransactionId))]
    [NotifyPropertyChangedFor(nameof(StringNarrationDetailStatus))]
    [NotifyPropertyChangedFor(nameof(StringNarrationDetailProduct))]
    [NotifyPropertyChangedFor(nameof(StringNarrationDetailReceiver))]
    [NotifyPropertyChangedFor(nameof(StringNarrationDetailProduction))]
    [NotifyCanExecuteChangedFor(nameof(RefreshStringNarrationOrderDetailCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateStringNarrationFulfillmentCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerateStringNarrationProductionOrderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyStringNarrationOrderFieldCommand))]
    private StringNarrationOrderDetail? selectedStringNarrationOrderDetail;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshStringNarrationOrderDetailCommand))]
    private StringNarrationOrderSummary? selectedStringNarrationOrder;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateStringNarrationFulfillmentCommand))]
    private string stringNarrationFulfillmentStatusInput = StringNarrationFulfillmentStatusCatalog.PendingMake;

    [ObservableProperty]
    private string stringNarrationTrackingNoInput = string.Empty;

    [ObservableProperty]
    private string stringNarrationCarrierInput = string.Empty;

    [ObservableProperty]
    private string stringNarrationExpressCompanyCodeInput = string.Empty;

    [ObservableProperty]
    private string stringNarrationShippingRemarkInput = string.Empty;

    [ObservableProperty]
    private string stringNarrationAdminRemarkInput = string.Empty;

    [ObservableProperty]
    private string stringNarrationProductionOrderRemarkInput = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateStringNarrationProductionOrderCommand))]
    private bool stringNarrationProductionOrderForceRegenerate;

    [ObservableProperty]
    private bool isStringNarrationProductionRemarkEditing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStringNarrationOrderListVisible))]
    [NotifyPropertyChangedFor(nameof(IsStringNarrationProductionSheetVisible))]
    private string stringNarrationLeftPaneMode = StringNarrationLeftPaneOrderList;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStringNarrationProductionSheet))]
    [NotifyPropertyChangedFor(nameof(StringNarrationProductionSheetProductionOrderNoText))]
    [NotifyPropertyChangedFor(nameof(StringNarrationProductionSheetWorkOrderNoText))]
    [NotifyPropertyChangedFor(nameof(StringNarrationProductionSheetStatusText))]
    [NotifyPropertyChangedFor(nameof(StringNarrationProductionSheetArrangementText))]
    [NotifyPropertyChangedFor(nameof(StringNarrationProductionSheetRemarkText))]
    [NotifyPropertyChangedFor(nameof(StringNarrationProductionSheetExampleImageUrl))]
    [NotifyPropertyChangedFor(nameof(StringNarrationProductionSheetExampleFallbackText))]
    private StringNarrationProductionSheetSnapshot? selectedStringNarrationProductionSheet;

    partial void OnSelectedStringNarrationOrderChanged(StringNarrationOrderSummary? value)
    {
        if (_isSynchronizingStringNarrationSelection)
        {
            return;
        }

        if (value is null)
        {
            StringNarrationLeftPaneMode = StringNarrationLeftPaneOrderList;
            SelectedStringNarrationOrderDetail = null;
            ReplaceCollection(StringNarrationOrderItems, []);
            ReplaceCollection(StringNarrationStatusLogs, []);
            ReplaceCollection(StringNarrationWorkOrders, []);
            ReplaceCollection(StringNarrationProductionSheetMaterials, []);
            SelectedStringNarrationProductionSheet = null;
            return;
        }

        var currentDetail = SelectedStringNarrationOrderDetail;
        if (currentDetail is null)
        {
            return;
        }

        var isSameOrder = string.Equals(currentDetail.OrderNo, value.OrderNo, StringComparison.Ordinal)
            || string.Equals(currentDetail.Id, value.Id, StringComparison.Ordinal)
            || string.Equals(currentDetail.WxOutTradeNo, value.WxOutTradeNo, StringComparison.Ordinal);
        if (isSameOrder)
        {
            return;
        }

        _ = OpenStringNarrationOrderDetailAsync(value);
    }

    partial void OnSelectedStringNarrationOrderDetailChanged(StringNarrationOrderDetail? value)
    {
        ReplaceCollection(StringNarrationOrderItems, value?.ItemsSnapshot ?? []);
        ReplaceCollection(StringNarrationStatusLogs, value is null ? [] : value.StatusLogs.OrderByDescending(log => log.At));
        var workOrders = value?.WorkOrders ?? value?.ProductionOrder.WorkOrders ?? [];
        ReplaceCollection(StringNarrationWorkOrders, workOrders);
        SelectedStringNarrationProductionSheet = StringNarrationProductionSheetSnapshot.Create(value);
        ReplaceCollection(StringNarrationProductionSheetMaterials, SelectedStringNarrationProductionSheet?.Materials ?? []);
        PopulateStringNarrationFulfillmentForm(value);
        if (value is null)
        {
            StringNarrationLeftPaneMode = StringNarrationLeftPaneOrderList;
        }

        if (value is not null)
        {
            UpsertExceptionOrder(value);
        }

        OnStringNarrationCollectionStateChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRunStringNarrationReadAction))]
    private async Task TestStringNarrationGatewayAsync()
    {
        await ExecuteStringNarrationReadActionAsync("正在验证串述网关...", async () =>
        {
            var result = await _stringNarrationOrderService.WhoamiAsync();
            StringNarrationStatusMessage = result.Authorized
                ? $"网关已授权：{result.OperatorId} / {string.Join(", ", result.Permissions)}"
                : "网关未授权";
        });
    }

    [RelayCommand(CanExecute = nameof(CanRunStringNarrationReadAction))]
    private async Task LoadStringNarrationOrdersAsync()
    {
        var isFirstLoad = !_hasLoadedStringNarrationOnce;
        if (isFirstLoad)
        {
            IsStringNarrationInitializing = true;
        }

        try
        {
            await ExecuteStringNarrationReadActionAsync("正在加载串述订单...", async () =>
            {
                var previousSelection = CaptureStringNarrationSelection();
                var query = BuildStringNarrationQuery();
                ValidateTimeRangeOrThrow(query);
                var result = await _stringNarrationOrderService.GetOrdersAsync(query);

                StringNarrationTotalCount = result.PageInfo.Total;
                ReplaceCollection(StringNarrationOrders, result.Orders);
                SyncExceptionOrdersFromOrders(result.Orders);
                var statsLoaded = await TryLoadStatsWithoutThrowAsync(BuildStringNarrationStatsQuery(), result.Orders.Count);
                if (!statsLoaded)
                {
                    ApplyStringNarrationStats(ResolveStringNarrationStatsFallback(result.Stats, result.Orders));
                }

                RestoreStringNarrationSelection(previousSelection);
                var statsSuffix = statsLoaded ? string.Empty : "；统计未更新";
                StringNarrationStatusMessage = $"已加载 {StringNarrationOrders.Count} 单，总数 {result.PageInfo.Total}{statsSuffix}";
                OnStringNarrationCollectionStateChanged();
            });
        }
        finally
        {
            if (isFirstLoad)
            {
                _hasLoadedStringNarrationOnce = true;
                IsStringNarrationInitializing = false;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunStringNarrationReadAction))]
    private async Task LoadStringNarrationStatsAsync()
    {
        await ExecuteStringNarrationReadActionAsync("正在加载履约统计...", async () =>
        {
            var query = BuildStringNarrationStatsQuery();
            ValidateTimeRangeOrThrow(query);
            var stats = await _stringNarrationOrderService.GetFulfillmentStatsAsync(query);
            ApplyStringNarrationStats(stats);
            StringNarrationStatusMessage = $"履约统计已更新：{StringNarrationStatsTotalText}";
        });
    }

    [RelayCommand(CanExecute = nameof(CanRunStringNarrationReadAction))]
    private async Task ApplyStringNarrationFulfillmentFilterAsync(string? fulfillmentStatus)
    {
        var normalizedStatus = NormalizeStringNarrationFilter(fulfillmentStatus ?? string.Empty);
        SelectedStringNarrationFulfillmentStatusFilter = string.Equals(SelectedStringNarrationFulfillmentStatusFilter, normalizedStatus, StringComparison.OrdinalIgnoreCase)
            ? "全部"
            : normalizedStatus;

        StringNarrationCurrentPage = 1;
        await LoadStringNarrationOrdersAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRunStringNarrationReadAction))]
    private async Task ClearStringNarrationFiltersAsync()
    {
        StringNarrationListKeyword = string.Empty;
        SelectedStringNarrationStatusFilter = "全部";
        SelectedStringNarrationFulfillmentStatusFilter = "全部";
        StringNarrationStartAt = 0;
        StringNarrationEndAt = 0;

        StringNarrationCurrentPage = 1;
        await LoadStringNarrationOrdersAsync();
    }

    [RelayCommand(CanExecute = nameof(CanSearchStringNarrationOrderDetail))]
    private async Task SearchStringNarrationOrderDetailAsync()
    {
        var lookup = StringNarrationLookupInput.Trim();
        await ExecuteStringNarrationReadActionAsync("正在查询串述订单详情...", async () =>
        {
            _hasDismissedStringNarrationDetailsThisSession = false;
            try
            {
                SelectedStringNarrationOrderDetail = await _stringNarrationOrderService.GetOrderDetailAsync(orderNo: lookup);
            }
            catch (InvalidOperationException) when (!lookup.StartsWith("CS", StringComparison.OrdinalIgnoreCase))
            {
                SelectedStringNarrationOrderDetail = await _stringNarrationOrderService.GetOrderDetailAsync(orderNo: string.Empty, tradeNo: lookup);
            }

            SelectStringNarrationSummaryByDetail(SelectedStringNarrationOrderDetail);
            StringNarrationStatusMessage = $"已加载详情：{SelectedStringNarrationOrderDetail?.OrderNo}";
        });
    }

    [RelayCommand(CanExecute = nameof(CanRefreshStringNarrationOrderDetail))]
    private async Task RefreshStringNarrationOrderDetailAsync()
    {
        var detail = SelectedStringNarrationOrderDetail;
        var summary = SelectedStringNarrationOrder;
        if (detail is not null)
        {
            await LoadStringNarrationOrderDetailAsync(detail.OrderNo, detail.WxOutTradeNo, detail.Id);
            return;
        }

        if (summary is not null)
        {
            await LoadStringNarrationOrderDetailAsync(summary);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUpdateStringNarrationFulfillment))]
    private async Task UpdateStringNarrationFulfillmentAsync()
    {
        var detail = SelectedStringNarrationOrderDetail;
        if (detail is null)
        {
            return;
        }

        if (!ConfirmStringNarrationFulfillmentUpdate(detail))
        {
            StringNarrationStatusMessage = "已取消履约更新";
            return;
        }

        try
        {
            IsStringNarrationSaving = true;
            StringNarrationError = string.Empty;
            StringNarrationStatusMessage = "正在更新串述履约信息...";

            SelectedStringNarrationOrderDetail = await _stringNarrationOrderService.UpdateFulfillmentAsync(new StringNarrationFulfillmentUpdateRequest
            {
                Id = detail.Id,
                OrderNo = detail.OrderNo,
                TradeNo = detail.WxOutTradeNo,
                FulfillmentStatus = StringNarrationFulfillmentStatusInput,
                TrackingNo = StringNarrationTrackingNoInput,
                Carrier = StringNarrationCarrierInput,
                ExpressCompanyCode = StringNarrationExpressCompanyCodeInput,
                ShippingRemark = StringNarrationShippingRemarkInput,
                AdminRemark = StringNarrationAdminRemarkInput
            });

            UpdateStringNarrationSummary(SelectedStringNarrationOrderDetail);
            StringNarrationStatusMessage = $"履约已更新：{SelectedStringNarrationOrderDetail.FulfillmentStatus}";
        }
        catch (Exception ex)
        {
            StringNarrationError = ex.Message;
            StringNarrationStatusMessage = $"更新履约失败：{ex.Message}";
        }
        finally
        {
            IsStringNarrationSaving = false;
            OnStringNarrationCollectionStateChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanGenerateStringNarrationProductionOrder))]
    private async Task GenerateStringNarrationProductionOrderAsync()
    {
        var detail = SelectedStringNarrationOrderDetail;
        if (detail is null)
        {
            return;
        }

        try
        {
            IsStringNarrationSaving = true;
            IsStringNarrationGeneratingProductionOrder = true;
            IsStringNarrationProductionOrderErrorVisible = false;
            StringNarrationError = string.Empty;
            StringNarrationStatusMessage = "正在生成制作单...";

            SelectedStringNarrationOrderDetail = await _stringNarrationOrderService.GenerateProductionOrderAsync(new StringNarrationGenerateProductionOrderRequest
            {
                Id = detail.Id,
                OrderNo = detail.OrderNo,
                TradeNo = detail.WxOutTradeNo,
                Remark = StringNarrationProductionOrderRemarkInput,
                ForceRegenerate = StringNarrationProductionOrderForceRegenerate
            });

            UpdateStringNarrationSummary(SelectedStringNarrationOrderDetail);
            ShowStringNarrationProductionSheet();
            StringNarrationStatusMessage = "制作单请求已提交并刷新详情。";
        }
        catch (Exception ex)
        {
            StringNarrationError = ex.Message;
            StringNarrationStatusMessage = $"生成制作单失败：{ex.Message}";
            IsStringNarrationProductionOrderErrorVisible = true;
        }
        finally
        {
            IsStringNarrationGeneratingProductionOrder = false;
            IsStringNarrationSaving = false;
            OnStringNarrationCollectionStateChanged();
        }
    }

    [RelayCommand]
    private void DismissStringNarrationProductionOrderError()
    {
        IsStringNarrationProductionOrderErrorVisible = false;
        IsStringNarrationGeneratingProductionOrder = false;
    }

    [RelayCommand]
    private void StartEditStringNarrationProductionRemark()
    {
        StringNarrationProductionOrderRemarkInput = SelectedStringNarrationProductionSheet?.Remark ?? string.Empty;
        IsStringNarrationProductionRemarkEditing = true;
    }

    [RelayCommand]
    private void CancelEditStringNarrationProductionRemark()
    {
        IsStringNarrationProductionRemarkEditing = false;
    }

    [RelayCommand]
    private async Task SaveStringNarrationProductionRemarkAsync()
    {
        var detail = SelectedStringNarrationOrderDetail;
        if (detail is null)
        {
            return;
        }

        try
        {
            IsStringNarrationSaving = true;
            StringNarrationError = string.Empty;
            StringNarrationStatusMessage = "正在保存制作备注...";

            SelectedStringNarrationOrderDetail = await _stringNarrationOrderService.GenerateProductionOrderAsync(new StringNarrationGenerateProductionOrderRequest
            {
                Id = detail.Id,
                OrderNo = detail.OrderNo,
                TradeNo = detail.WxOutTradeNo,
                Remark = StringNarrationProductionOrderRemarkInput,
                ForceRegenerate = true
            });

            UpdateStringNarrationSummary(SelectedStringNarrationOrderDetail);
            ShowStringNarrationProductionSheet();
            IsStringNarrationProductionRemarkEditing = false;
            StringNarrationStatusMessage = "制作备注已更新。";
        }
        catch (Exception ex)
        {
            StringNarrationError = ex.Message;
            StringNarrationStatusMessage = $"更新制作备注失败：{ex.Message}";
        }
        finally
        {
            IsStringNarrationSaving = false;
            OnStringNarrationCollectionStateChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCopyStringNarrationOrderField))]
    private void CopyStringNarrationOrderField(string? field)
    {
        var detail = SelectedStringNarrationOrderDetail;
        if (detail is null || string.IsNullOrWhiteSpace(field))
        {
            return;
        }

        var (label, text) = BuildStringNarrationCopyText(detail, field);
        if (string.IsNullOrWhiteSpace(text))
        {
            StringNarrationStatusMessage = $"{label}为空，未复制";
            return;
        }

        _clipboardService.SetText(text);
        StringNarrationStatusMessage = $"已复制{label}";
    }

    [RelayCommand(CanExecute = nameof(CanCopyStringNarrationOrderSummaryOrderNo))]
    private void CopyStringNarrationOrderSummaryOrderNo(StringNarrationOrderSummary? summary)
    {
        var orderNo = summary?.OrderNo;
        if (string.IsNullOrWhiteSpace(orderNo))
        {
            StringNarrationStatusMessage = "订单号为空，未复制";
            return;
        }

        _clipboardService.SetText(orderNo);
        StringNarrationStatusMessage = "已复制订单号";
    }

    [RelayCommand]
    private void ShowStringNarrationOrderList()
    {
        StringNarrationLeftPaneMode = StringNarrationLeftPaneOrderList;
    }

    private void ShowStringNarrationProductionSheet()
    {
        if (SelectedStringNarrationOrderDetail is null)
        {
            return;
        }

        SelectedStringNarrationProductionSheet = StringNarrationProductionSheetSnapshot.Create(SelectedStringNarrationOrderDetail);
        ReplaceCollection(StringNarrationProductionSheetMaterials, SelectedStringNarrationProductionSheet.Materials);
        OnPropertyChanged(nameof(HasStringNarrationProductionSheetMaterials));
        StringNarrationLeftPaneMode = StringNarrationLeftPaneProductionSheet;
    }

    private async Task LoadStringNarrationOrderDetailAsync(StringNarrationOrderSummary summary)
    {
        await LoadStringNarrationOrderDetailAsync(summary.OrderNo, summary.WxOutTradeNo, summary.Id);
    }

    private async Task LoadStringNarrationOrderDetailAsync(string orderNo, string tradeNo, string id)
    {
        await ExecuteStringNarrationReadActionAsync("正在加载串述订单详情...", async () =>
        {
            SelectedStringNarrationOrderDetail = await _stringNarrationOrderService.GetOrderDetailAsync(orderNo, tradeNo, id);
            StringNarrationStatusMessage = $"已加载详情：{SelectedStringNarrationOrderDetail.OrderNo}";
        });
    }

    private async Task ExecuteStringNarrationReadActionAsync(string busyMessage, Func<Task> action)
    {
        if (IsStringNarrationBusy)
        {
            return;
        }

        try
        {
            IsStringNarrationLoading = true;
            StringNarrationError = string.Empty;
            StringNarrationStatusMessage = busyMessage;
            await action();
        }
        catch (Exception ex)
        {
            StringNarrationError = ex.Message;
            StringNarrationStatusMessage = $"串述网关调用失败：{ex.Message}";
        }
        finally
        {
            IsStringNarrationLoading = false;
            OnStringNarrationCollectionStateChanged();
        }
    }

    private bool CanRunStringNarrationReadAction()
    {
        return !IsStringNarrationBusy;
    }

    private bool CanSearchStringNarrationOrderDetail()
    {
        return !IsStringNarrationBusy && !string.IsNullOrWhiteSpace(StringNarrationLookupInput);
    }

    private bool CanRefreshStringNarrationOrderDetail()
    {
        return !IsStringNarrationBusy && (SelectedStringNarrationOrderDetail is not null || SelectedStringNarrationOrder is not null);
    }

    private bool CanUpdateStringNarrationFulfillment()
    {
        return !IsStringNarrationBusy
            && SelectedStringNarrationOrderDetail is not null
            && !string.IsNullOrWhiteSpace(StringNarrationFulfillmentStatusInput);
    }

    private bool CanGenerateStringNarrationProductionOrder()
    {
        return !IsStringNarrationBusy && SelectedStringNarrationOrderDetail is not null;
    }

    private bool CanCopyStringNarrationOrderField(string? field)
    {
        return SelectedStringNarrationOrderDetail is not null && !string.IsNullOrWhiteSpace(field);
    }

    private bool CanCopyStringNarrationOrderSummaryOrderNo(StringNarrationOrderSummary? summary)
    {
        return !IsStringNarrationBusy && !string.IsNullOrWhiteSpace(summary?.OrderNo);
    }

    private void PopulateStringNarrationFulfillmentForm(StringNarrationOrderDetail? detail)
    {
        StringNarrationFulfillmentStatusInput = string.IsNullOrWhiteSpace(detail?.FulfillmentStatus)
            ? StringNarrationFulfillmentStatusCatalog.PendingMake
            : detail.FulfillmentStatus;
        StringNarrationTrackingNoInput = detail?.TrackingNo ?? string.Empty;
        StringNarrationCarrierInput = detail?.Carrier ?? string.Empty;
        StringNarrationExpressCompanyCodeInput = detail?.ExpressCompanyCode ?? string.Empty;
        StringNarrationShippingRemarkInput = detail?.ShippingRemark ?? string.Empty;
        StringNarrationAdminRemarkInput = detail?.AdminRemark ?? string.Empty;
    }

    private void ApplyStringNarrationStats(StringNarrationFulfillmentStats? stats)
    {
        var resolved = stats ?? new StringNarrationFulfillmentStats();
        if (resolved.Metrics.Count == 0)
        {
            resolved = new StringNarrationFulfillmentStats
            {
                TotalCount = resolved.TotalCount,
                CalculatedAt = resolved.CalculatedAt,
                Metrics = StringNarrationFulfillmentStatusCatalog.GetDefinitions()
                    .OrderBy(item => item.SortOrder)
                    .Select(item => new StringNarrationFulfillmentStatusMetric
                    {
                        FulfillmentStatus = item.FulfillmentStatus,
                        Label = item.Label,
                        SortOrder = item.SortOrder,
                        Count = 0,
                        IsTerminal = item.IsTerminal,
                        IsException = item.IsException
                    })
                    .ToArray()
            };
        }

        resolved.WorkbenchDashboard = ResolveStringNarrationWorkbenchDashboard(resolved.WorkbenchDashboard, resolved, StringNarrationOrders);
        StringNarrationFulfillmentStats = resolved;
        ReplaceCollection(StringNarrationFulfillmentMetrics, resolved.Metrics.OrderBy(item => item.SortOrder));
        ReplaceCollection(StringNarrationWorkbenchTrendItems, resolved.WorkbenchDashboard.RecentBusinessTrendItems);
        ReplaceCollection(StringNarrationWorkbenchPressureItems, resolved.WorkbenchDashboard.FulfillmentPressureItems);
        NotifyStringNarrationWorkbenchDashboardChanged();
    }

    private StringNarrationOrderQuery BuildStringNarrationQuery()
    {
        return new StringNarrationOrderQuery
        {
            Page = StringNarrationCurrentPage,
            PageSize = StringNarrationPageSize,
            Keyword = StringNarrationListKeyword,
            Status = NormalizeStringNarrationFilter(SelectedStringNarrationStatusFilter),
            FulfillmentStatus = NormalizeStringNarrationFilter(SelectedStringNarrationFulfillmentStatusFilter),
            StartAt = StringNarrationStartAt,
            EndAt = StringNarrationEndAt
        };
    }

    private StringNarrationOrderQuery BuildStringNarrationStatsQuery()
    {
        var query = BuildStringNarrationQuery();
        query.FulfillmentStatus = string.Empty;
        return query;
    }

    private static void ValidateTimeRangeOrThrow(StringNarrationOrderQuery query)
    {
        if (query.StartAt > 0 && query.EndAt > 0 && query.StartAt > query.EndAt)
        {
            throw new InvalidOperationException("时间筛选范围无效：startAt 不能大于 endAt。");
        }
    }

    private async Task<bool> TryLoadStatsWithoutThrowAsync(StringNarrationOrderQuery query, int fallbackOrderCount)
    {
        try
        {
            var stats = await _stringNarrationOrderService.GetFulfillmentStatsAsync(query);
            if (!HasStringNarrationStatsData(stats, fallbackOrderCount))
            {
                StringNarrationStatusMessage = "列表已加载，统计接口返回空数据，已使用列表统计回退。";
                return false;
            }

            ApplyStringNarrationStats(stats);
            return true;
        }
        catch (Exception ex)
        {
            StringNarrationStatusMessage = $"列表已加载，统计单独拉取失败：{ex.Message}";
            return false;
        }
    }

    private static bool HasStringNarrationStatsData(StringNarrationFulfillmentStats? stats, int fallbackOrderCount)
    {
        if (stats?.Metrics.Count > 0 && (stats.TotalCount > 0 || stats.Metrics.Any(item => item.Count > 0)))
        {
            return true;
        }

        return fallbackOrderCount == 0 && stats?.Metrics.Count > 0;
    }

    private static StringNarrationFulfillmentStats ResolveStringNarrationStatsFallback(
        StringNarrationFulfillmentStats? stats,
        IReadOnlyList<StringNarrationOrderSummary> orders)
    {
        if (HasStringNarrationStatsData(stats, orders.Count))
        {
            return stats!;
        }

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
        {
            var status = StringNarrationFulfillmentStatusCatalog.Normalize(order.FulfillmentStatus);
            if (string.IsNullOrWhiteSpace(status))
            {
                continue;
            }

            counts.TryGetValue(status, out var current);
            counts[status] = current + 1;
        }

        var metrics = StringNarrationFulfillmentStatusCatalog.GetDefinitions()
            .OrderBy(item => item.SortOrder)
            .Select(item =>
            {
                counts.TryGetValue(item.FulfillmentStatus, out var count);
                return new StringNarrationFulfillmentStatusMetric
                {
                    FulfillmentStatus = item.FulfillmentStatus,
                    Label = item.Label,
                    SortOrder = item.SortOrder,
                    Count = count,
                    IsTerminal = item.IsTerminal,
                    IsException = item.IsException,
                    IsUnknown = item.IsUnknown
                };
            })
            .ToArray();

        return new StringNarrationFulfillmentStats
        {
            Metrics = metrics,
            TotalCount = counts.Values.Sum(),
            CalculatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    private static StringNarrationWorkbenchDashboardStats ResolveStringNarrationWorkbenchDashboard(
        StringNarrationWorkbenchDashboardStats? dashboard,
        StringNarrationFulfillmentStats stats,
        IReadOnlyList<StringNarrationOrderSummary> orders)
    {
        dashboard ??= new StringNarrationWorkbenchDashboardStats();
        var fallback = BuildStringNarrationWorkbenchDashboardFallback(stats, orders);
        var fallbackUsed = false;

        if (dashboard.TodayOrderCount == 0)
        {
            dashboard.TodayOrderCount = fallback.TodayOrderCount;
            fallbackUsed = true;
        }

        if (dashboard.TodayOrderCountDelta == 0)
        {
            dashboard.TodayOrderCountDelta = fallback.TodayOrderCountDelta;
            fallbackUsed = true;
        }

        if (dashboard.TodayRevenueAmount == 0)
        {
            dashboard.TodayRevenueAmount = fallback.TodayRevenueAmount;
            fallbackUsed = true;
        }

        if (dashboard.TodayRevenueAmountDelta == 0)
        {
            dashboard.TodayRevenueAmountDelta = fallback.TodayRevenueAmountDelta;
            fallbackUsed = true;
        }

        if (dashboard.PendingMakeCount <= 0)
        {
            dashboard.PendingMakeCount = stats.PendingMakeCount;
            fallbackUsed = true;
        }

        if (dashboard.ReadyToShipCount <= 0)
        {
            dashboard.ReadyToShipCount = stats.ReadyToShipCount;
            fallbackUsed = true;
        }

        if (dashboard.ExceptionOrderCount <= 0)
        {
            dashboard.ExceptionOrderCount = stats.ExceptionCount;
            fallbackUsed = true;
        }

        if (dashboard.UnfinishedOrderCount <= 0)
        {
            dashboard.UnfinishedOrderCount = fallback.UnfinishedOrderCount;
            fallbackUsed = true;
        }

        if (dashboard.LastSyncedAt <= 0)
        {
            dashboard.LastSyncedAt = stats.CalculatedAt > 0 ? stats.CalculatedAt : fallback.LastSyncedAt;
            fallbackUsed = true;
        }

        if (dashboard.RecentBusinessTrendItems.Count == 0)
        {
            dashboard.RecentBusinessTrendItems = fallback.RecentBusinessTrendItems;
            fallbackUsed = true;
        }

        if (dashboard.FulfillmentPressureItems.Count == 0)
        {
            dashboard.FulfillmentPressureItems = BuildStringNarrationPressureItems(stats, dashboard.UnfinishedOrderCount);
            fallbackUsed = true;
        }

        dashboard.IsFallbackProjection = dashboard.IsFallbackProjection || fallbackUsed;

        return dashboard;
    }

    private static StringNarrationWorkbenchDashboardStats BuildStringNarrationWorkbenchDashboardFallback(
        StringNarrationFulfillmentStats stats,
        IReadOnlyList<StringNarrationOrderSummary> orders)
    {
        var today = DateTime.Today;
        var yesterday = today.AddDays(-1);
        var todayOrderCount = CountOrdersCreatedOn(orders, today);
        var yesterdayOrderCount = CountOrdersCreatedOn(orders, yesterday);
        var todayRevenue = SumRevenuePaidOn(orders, today);
        var yesterdayRevenue = SumRevenuePaidOn(orders, yesterday);
        var unfinishedOrderCount =
            stats.PaidPendingConfirmCount +
            stats.PendingMakeCount +
            stats.MakingCount +
            stats.ReadyToShipCount +
            stats.ExceptionCount;

        return new StringNarrationWorkbenchDashboardStats
        {
            TodayOrderCount = todayOrderCount,
            TodayOrderCountDelta = todayOrderCount - yesterdayOrderCount,
            TodayRevenueAmount = todayRevenue,
            TodayRevenueAmountDelta = todayRevenue - yesterdayRevenue,
            PendingMakeCount = stats.PendingMakeCount,
            ReadyToShipCount = stats.ReadyToShipCount,
            ExceptionOrderCount = stats.ExceptionCount,
            UnfinishedOrderCount = unfinishedOrderCount,
            LastSyncedAt = stats.CalculatedAt > 0 ? stats.CalculatedAt : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            RecentBusinessTrendItems = BuildStringNarrationTrendItems(orders, today),
            FulfillmentPressureItems = BuildStringNarrationPressureItems(stats, unfinishedOrderCount),
            IsFallbackProjection = true
        };
    }

    private static IReadOnlyList<StringNarrationBusinessTrendPoint> BuildStringNarrationTrendItems(
        IReadOnlyList<StringNarrationOrderSummary> orders,
        DateTime today)
    {
        var items = new List<StringNarrationBusinessTrendPoint>();
        var start = today.AddDays(-6);
        for (var date = start; date <= today; date = date.AddDays(1))
        {
            items.Add(new StringNarrationBusinessTrendPoint
            {
                DateKey = date.ToString("yyyy-MM-dd"),
                Label = date.ToString("MM-dd"),
                OrderCount = CountOrdersCreatedOn(orders, date),
                RevenueAmount = SumRevenuePaidOn(orders, date)
            });
        }

        return items;
    }

    private static IReadOnlyList<StringNarrationFulfillmentPressureMetric> BuildStringNarrationPressureItems(
        StringNarrationFulfillmentStats stats,
        int unfinishedOrderCount)
    {
        var targetCount = unfinishedOrderCount > 0 ? unfinishedOrderCount : stats.TotalCount;
        return new[]
        {
            BuildStringNarrationPressureItem(StringNarrationFulfillmentStatusCatalog.PendingMake, "待制作", stats.PendingMakeCount, targetCount),
            BuildStringNarrationPressureItem(StringNarrationFulfillmentStatusCatalog.ReadyToShip, "待发货", stats.ReadyToShipCount, targetCount)
        };
    }

    private static StringNarrationFulfillmentPressureMetric BuildStringNarrationPressureItem(
        string fulfillmentStatus,
        string label,
        int count,
        int targetCount)
    {
        return new StringNarrationFulfillmentPressureMetric
        {
            FulfillmentStatus = fulfillmentStatus,
            Label = label,
            Count = count,
            TargetCount = targetCount,
            Ratio = targetCount <= 0 ? 0 : (decimal)count / targetCount
        };
    }

    private static int CountOrdersCreatedOn(IReadOnlyList<StringNarrationOrderSummary> orders, DateTime date)
    {
        return orders.Count(order => TryGetLocalDate(order.CreatedAt, out var localDate) && localDate == date.Date);
    }

    private static decimal SumRevenuePaidOn(IReadOnlyList<StringNarrationOrderSummary> orders, DateTime date)
    {
        return orders
            .Where(order => TryGetLocalDate(order.PaidAt, out var localDate) && localDate == date.Date)
            .Sum(order => order.Amount);
    }

    private static bool TryGetLocalDate(long timestamp, out DateTime date)
    {
        date = default;
        if (timestamp <= 0)
        {
            return false;
        }

        try
        {
            var milliseconds = timestamp < 10_000_000_000 ? timestamp * 1000 : timestamp;
            date = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).LocalDateTime.Date;
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private void SelectStringNarrationSummaryByDetail(StringNarrationOrderDetail? detail)
    {
        if (detail is null)
        {
            return;
        }

        var match = StringNarrationOrders.FirstOrDefault(item =>
            string.Equals(item.OrderNo, detail.OrderNo, StringComparison.Ordinal)
            || string.Equals(item.Id, detail.Id, StringComparison.Ordinal)
            || string.Equals(item.WxOutTradeNo, detail.WxOutTradeNo, StringComparison.Ordinal));
        if (match is not null && SelectedStringNarrationOrder != match)
        {
            _isSynchronizingStringNarrationSelection = true;
            try
            {
                SelectedStringNarrationOrder = match;
            }
            finally
            {
                _isSynchronizingStringNarrationSelection = false;
            }
        }
    }

    public void DismissStringNarrationDetailsForSession()
    {
        _hasDismissedStringNarrationDetailsThisSession = true;
        ClearStringNarrationSelection();
        StringNarrationStatusMessage = "已关闭订单详情，本次会话内不会自动展开。";
    }

    public void EnsureStringNarrationDetailSelection()
    {
        if (_hasDismissedStringNarrationDetailsThisSession
            || StringNarrationOrders.Count == 0
            || SelectedStringNarrationOrder is not null
            || SelectedStringNarrationOrderDetail is not null)
        {
            return;
        }

        _ = OpenStringNarrationOrderDetailAsync(StringNarrationOrders.FirstOrDefault());
    }

    public async Task OpenStringNarrationOrderDetailAsync(StringNarrationOrderSummary? summary)
    {
        if (summary is null)
        {
            return;
        }

        _hasDismissedStringNarrationDetailsThisSession = false;
        if (SelectedStringNarrationOrder != summary)
        {
            SelectedStringNarrationOrder = summary;
        }

        await LoadStringNarrationOrderDetailAsync(summary);
    }

    private void UpdateStringNarrationSummary(StringNarrationOrderDetail detail)
    {
        var index = StringNarrationOrders.ToList().FindIndex(item =>
            string.Equals(item.OrderNo, detail.OrderNo, StringComparison.Ordinal)
            || string.Equals(item.Id, detail.Id, StringComparison.Ordinal));
        if (index < 0)
        {
            return;
        }

        _isSynchronizingStringNarrationSelection = true;
        try
        {
            var previousSummary = StringNarrationOrders[index];
            StringNarrationOrders[index] = detail;
            SelectedStringNarrationOrder = StringNarrationOrders[index];
            UpsertExceptionOrder(detail);
            SynchronizeStringNarrationStatsForSummaryChange(previousSummary, detail);
        }
        finally
        {
            _isSynchronizingStringNarrationSelection = false;
        }
    }

    private void ClearStringNarrationSelection()
    {
        _isSynchronizingStringNarrationSelection = true;
        try
        {
            SelectedStringNarrationOrder = null;
        }
        finally
        {
            _isSynchronizingStringNarrationSelection = false;
        }

        SelectedStringNarrationOrderDetail = null;
        StringNarrationLeftPaneMode = StringNarrationLeftPaneOrderList;
    }

    private void RestoreStringNarrationSelection(StringNarrationSelectionSnapshot previousSelection)
    {
        var matchedOrder = FindStringNarrationOrder(previousSelection);
        if (matchedOrder is not null)
        {
            SelectedStringNarrationOrder = matchedOrder;
            if (previousSelection.ShouldOpenDetail)
            {
                _ = OpenStringNarrationOrderDetailAsync(matchedOrder);
            }
            return;
        }

        // 找不到历史选择时，默认不再选中第一个，而是清除选择状态
        ClearStringNarrationSelection();
    }

    private StringNarrationOrderSummary? FindStringNarrationOrder(StringNarrationSelectionSnapshot snapshot)
    {
        if (snapshot.IsEmpty)
        {
            return null;
        }

        return StringNarrationOrders.FirstOrDefault(item =>
            (!string.IsNullOrWhiteSpace(snapshot.OrderNo) && string.Equals(item.OrderNo, snapshot.OrderNo, StringComparison.Ordinal))
            || (!string.IsNullOrWhiteSpace(snapshot.Id) && string.Equals(item.Id, snapshot.Id, StringComparison.Ordinal))
            || (!string.IsNullOrWhiteSpace(snapshot.TradeNo) && string.Equals(item.WxOutTradeNo, snapshot.TradeNo, StringComparison.Ordinal)));
    }

    private StringNarrationSelectionSnapshot CaptureStringNarrationSelection()
    {
        var detail = SelectedStringNarrationOrderDetail;
        if (detail is not null)
        {
            return new StringNarrationSelectionSnapshot(detail.OrderNo, detail.WxOutTradeNo, detail.Id, true);
        }

        var summary = SelectedStringNarrationOrder;
        return summary is null
            ? StringNarrationSelectionSnapshot.Empty
            : new StringNarrationSelectionSnapshot(summary.OrderNo, summary.WxOutTradeNo, summary.Id, false);
    }

    private void OnStringNarrationCollectionStateChanged()
    {
        OnPropertyChanged(nameof(HasStringNarrationOrders));
        OnPropertyChanged(nameof(HasSelectedStringNarrationOrderDetail));
        OnPropertyChanged(nameof(IsStringNarrationDetailPanelVisible));
        OnPropertyChanged(nameof(IsStringNarrationListExpanded));
        OnPropertyChanged(nameof(IsStringNarrationOrderListVisible));
        OnPropertyChanged(nameof(IsStringNarrationProductionSheetVisible));
        OnPropertyChanged(nameof(HasStringNarrationOrderItems));
        OnPropertyChanged(nameof(HasStringNarrationStatusLogs));
        OnPropertyChanged(nameof(HasStringNarrationWorkOrders));
        OnPropertyChanged(nameof(HasStringNarrationProductionSheet));
        OnPropertyChanged(nameof(HasStringNarrationProductionSheetMaterials));
        OnPropertyChanged(nameof(HasStringNarrationFulfillmentMetrics));
        OnPropertyChanged(nameof(HasStringNarrationWorkbenchTrendItems));
        OnPropertyChanged(nameof(HasStringNarrationWorkbenchPressureItems));
        OnPropertyChanged(nameof(StringNarrationOrdersCountText));
        OnPropertyChanged(nameof(StringNarrationStatsTotalText));
        OnPropertyChanged(nameof(StringNarrationStatsCalculatedAtText));
        OnPropertyChanged(nameof(StringNarrationEmptyStateText));
        OnPropertyChanged(nameof(StringNarrationSelectedTitle));
        OnPropertyChanged(nameof(StringNarrationSelectedOrderNo));
        OnPropertyChanged(nameof(StringNarrationSelectedTradeNo));
        OnPropertyChanged(nameof(StringNarrationSelectedAmountText));
        OnPropertyChanged(nameof(StringNarrationSelectedPaidAtText));
        OnPropertyChanged(nameof(StringNarrationSelectedCreatedAtText));
        OnPropertyChanged(nameof(StringNarrationAddressText));
        OnPropertyChanged(nameof(StringNarrationRemarkText));
        OnPropertyChanged(nameof(StringNarrationShippingStateText));
        OnPropertyChanged(nameof(StringNarrationTrackingText));
        OnPropertyChanged(nameof(StringNarrationFulfillmentTimeText));
        OnPropertyChanged(nameof(StringNarrationDetailOrderNo));
        OnPropertyChanged(nameof(StringNarrationDetailTransactionId));
        OnPropertyChanged(nameof(StringNarrationDetailStatus));
        OnPropertyChanged(nameof(StringNarrationDetailProduct));
        OnPropertyChanged(nameof(StringNarrationDetailReceiver));
        OnPropertyChanged(nameof(StringNarrationDetailProduction));
        OnPropertyChanged(nameof(StringNarrationProductionSheetOrderNoText));
        OnPropertyChanged(nameof(StringNarrationProductionSheetProductionOrderNoText));
        OnPropertyChanged(nameof(StringNarrationProductionSheetWorkOrderNoText));
        OnPropertyChanged(nameof(StringNarrationProductionSheetStatusText));
        OnPropertyChanged(nameof(StringNarrationProductionSheetArrangementText));
        OnPropertyChanged(nameof(StringNarrationProductionSheetRemarkText));
        OnPropertyChanged(nameof(StringNarrationProductionSheetExampleImageUrl));
        OnPropertyChanged(nameof(StringNarrationProductionSheetExampleFallbackText));
        NotifyStringNarrationWorkbenchDashboardChanged();
    }

    private bool ConfirmStringNarrationFulfillmentUpdate(StringNarrationOrderDetail detail)
    {
        var targetStatus = StringNarrationFulfillmentStatusInput.Trim();
        var message = targetStatus is StringNarrationFulfillmentStatusCatalog.Shipped or StringNarrationFulfillmentStatusCatalog.Completed
            ? $"确认将订单 {detail.OrderNoText} 的 fulfillmentStatus 更新为 {targetStatus}？\n\n本操作只提交履约字段，不提交支付 status；后端如有 shippedAt/completedAt 自动逻辑，将由接口处理。"
            : $"确认更新订单 {detail.OrderNoText} 的履约信息？\n\n本操作只提交 fulfillmentStatus、物流和备注字段，不提交支付 status。";

        var result = System.Windows.MessageBox.Show(
            GetDialogOwner(),
            message,
            "确认更新串述履约",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.No);

        return result == System.Windows.MessageBoxResult.Yes;
    }

    private static (string Label, string Text) BuildStringNarrationCopyText(StringNarrationOrderDetail detail, string field)
    {
        return field.Trim() switch
        {
            "orderNo" => ("订单号", detail.OrderNo),
            "wxOutTradeNo" => ("微信商户单号", detail.WxOutTradeNo),
            "phone" => ("手机号", detail.Address.ReceiverPhone),
            "address" => ("完整地址", detail.Address.FullAddressText == "无地址" ? string.Empty : detail.Address.FullAddressText),
            "trackingNo" => ("快递单号", detail.TrackingNo),
            "shippingInfo" => ("发货信息", JoinNonEmpty(" ", detail.Carrier, detail.TrackingNo)),
            "receiverInfo" => ("收件信息", JoinNonEmpty(Environment.NewLine, detail.Address.ReceiverName, detail.Address.ReceiverPhone, detail.Address.FullAddressText == "无地址" ? string.Empty : detail.Address.FullAddressText)),
            _ => ("内容", string.Empty)
        };
    }

    private static string JoinNonEmpty(string separator, params string[] values)
    {
        return string.Join(separator, values.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()));
    }

    private static string NormalizeStringNarrationFilter(string value)
    {
        return string.Equals(value, "全部", StringComparison.OrdinalIgnoreCase) ? string.Empty : value.Trim();
    }

    private static string BuildAddressText(StringNarrationAddressSnapshot address)
    {
        var summary = address.FullAddressText;
        var receiver = string.Join(" ", new[] { address.ReceiverName, address.ReceiverPhone }.Where(item => !string.IsNullOrWhiteSpace(item)));
        return string.IsNullOrWhiteSpace(receiver) ? summary : $"{receiver}\n{summary}";
    }

    private static string BuildValue(string? value, string fallback = "暂无")
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string FormatGatewayTime(long timestamp)
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

    private void SynchronizeStringNarrationStatsForSummaryChange(
        StringNarrationOrderSummary previousSummary,
        StringNarrationOrderSummary currentSummary)
    {
        var previousStatus = StringNarrationFulfillmentStatusCatalog.Normalize(previousSummary.FulfillmentStatus);
        var currentStatus = StringNarrationFulfillmentStatusCatalog.Normalize(currentSummary.FulfillmentStatus);
        if (string.Equals(previousStatus, currentStatus, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var metrics = StringNarrationFulfillmentStats.Metrics
            .Select(metric => new StringNarrationFulfillmentStatusMetric
            {
                FulfillmentStatus = metric.FulfillmentStatus,
                Label = metric.Label,
                SortOrder = metric.SortOrder,
                Count = metric.Count,
                IsTerminal = metric.IsTerminal,
                IsException = metric.IsException,
                IsUnknown = metric.IsUnknown
            })
            .ToList();

        AdjustStringNarrationMetricCount(metrics, previousStatus, -1);
        AdjustStringNarrationMetricCount(metrics, currentStatus, 1);

        ApplyStringNarrationStats(new StringNarrationFulfillmentStats
        {
            Metrics = metrics,
            TotalCount = StringNarrationFulfillmentStats.TotalCount,
            CalculatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
    }

    private static void AdjustStringNarrationMetricCount(
        ICollection<StringNarrationFulfillmentStatusMetric> metrics,
        string fulfillmentStatus,
        int delta)
    {
        if (string.IsNullOrWhiteSpace(fulfillmentStatus) || delta == 0)
        {
            return;
        }

        var metric = metrics.FirstOrDefault(item =>
            string.Equals(item.FulfillmentStatus, fulfillmentStatus, StringComparison.OrdinalIgnoreCase));
        if (metric is not null)
        {
            metric.Count = Math.Max(0, metric.Count + delta);
            return;
        }

        if (delta < 0)
        {
            return;
        }

        var definition = StringNarrationFulfillmentStatusCatalog.Resolve(fulfillmentStatus);
        metrics.Add(new StringNarrationFulfillmentStatusMetric
        {
            FulfillmentStatus = fulfillmentStatus,
            Label = definition.Label,
            SortOrder = definition.SortOrder,
            Count = delta,
            IsTerminal = definition.IsTerminal,
            IsException = definition.IsException,
            IsUnknown = definition.IsUnknown
        });
    }

    private readonly record struct StringNarrationSelectionSnapshot(string OrderNo, string TradeNo, string Id, bool ShouldOpenDetail)
    {
        public static readonly StringNarrationSelectionSnapshot Empty = new(string.Empty, string.Empty, string.Empty, false);

        public bool IsEmpty => string.IsNullOrWhiteSpace(OrderNo)
            && string.IsNullOrWhiteSpace(TradeNo)
            && string.IsNullOrWhiteSpace(Id);
    }

    partial void OnStringNarrationTrackingNoInputChanged(string value)
    {
        if (_isDetectingCarrier || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        _isDetectingCarrier = true;
        try
        {
            AutoDetectCarrier(value);
        }
        finally
        {
            _isDetectingCarrier = false;
        }
    }

    private void AutoDetectCarrier(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        string normalized = input.Trim();
        string? detectedCarrier = null;
        string? detectedCode = null;
        string cleanTrackingNo = normalized;

        if (normalized.Contains("顺丰"))
        {
            detectedCarrier = "顺丰速运";
            detectedCode = "SF";
            cleanTrackingNo = normalized.Replace("顺丰", "").Replace("速运", "").Trim();
        }
        else if (normalized.Contains("京东"))
        {
            detectedCarrier = "京东快递";
            detectedCode = "JD";
            cleanTrackingNo = normalized.Replace("京东", "").Replace("快递", "").Trim();
        }
        else if (normalized.Contains("圆通"))
        {
            detectedCarrier = "圆通速递";
            detectedCode = "YTO";
            cleanTrackingNo = normalized.Replace("圆通", "").Replace("速递", "").Replace("快递", "").Trim();
        }
        else if (normalized.Contains("中通"))
        {
            detectedCarrier = "中通快递";
            detectedCode = "ZTO";
            cleanTrackingNo = normalized.Replace("中通", "").Replace("快递", "").Trim();
        }
        else if (normalized.Contains("申通"))
        {
            detectedCarrier = "申通快递";
            detectedCode = "STO";
            cleanTrackingNo = normalized.Replace("申通", "").Replace("快递", "").Trim();
        }
        else if (normalized.Contains("韵达"))
        {
            detectedCarrier = "韵达速递";
            detectedCode = "YUNDA";
            cleanTrackingNo = normalized.Replace("韵达", "").Replace("速递", "").Replace("快递", "").Trim();
        }
        else if (normalized.Contains("邮政") || normalized.Contains("挂号信"))
        {
            detectedCarrier = "邮政快递包裹";
            detectedCode = "POST";
            cleanTrackingNo = normalized.Replace("邮政", "").Replace("快递", "").Replace("包裹", "").Trim();
        }
        else if (normalized.Contains("极兔"))
        {
            detectedCarrier = "极兔速递";
            detectedCode = "JT";
            cleanTrackingNo = normalized.Replace("极兔", "").Replace("速递", "").Replace("快递", "").Trim();
        }
        else
        {
            string upper = normalized.ToUpperInvariant();
            if (upper.StartsWith("SF"))
            {
                detectedCarrier = "顺丰速运";
                detectedCode = "SF";
            }
            else if (upper.StartsWith("JD"))
            {
                detectedCarrier = "京东快递";
                detectedCode = "JD";
            }
            else if (upper.StartsWith("YT"))
            {
                detectedCarrier = "圆通速递";
                detectedCode = "YTO";
            }
            else if (upper.StartsWith("ZT"))
            {
                detectedCarrier = "中通快递";
                detectedCode = "ZTO";
            }
            else if (upper.StartsWith("ST"))
            {
                detectedCarrier = "申通快递";
                detectedCode = "STO";
            }
            else if (upper.StartsWith("YD"))
            {
                detectedCarrier = "韵达速递";
                detectedCode = "YUNDA";
            }
            else if (upper.StartsWith("JT"))
            {
                detectedCarrier = "极兔速递";
                detectedCode = "JT";
            }
        }

        if (detectedCarrier != null)
        {
            StringNarrationCarrierInput = detectedCarrier;
            StringNarrationExpressCompanyCodeInput = detectedCode ?? string.Empty;

            cleanTrackingNo = System.Text.RegularExpressions.Regex.Replace(cleanTrackingNo, @"^[^\w]+", "");
            cleanTrackingNo = System.Text.RegularExpressions.Regex.Replace(cleanTrackingNo, @"[^\w]+$", "");

            if (cleanTrackingNo != StringNarrationTrackingNoInput)
            {
                StringNarrationTrackingNoInput = cleanTrackingNo;
            }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StringNarrationPageLabel))]
    [NotifyCanExecuteChangedFor(nameof(NavigateStringNarrationPrevPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NavigateStringNarrationNextPageCommand))]
    private int stringNarrationCurrentPage = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StringNarrationTotalPages))]
    [NotifyPropertyChangedFor(nameof(StringNarrationPageLabel))]
    [NotifyCanExecuteChangedFor(nameof(NavigateStringNarrationPrevPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NavigateStringNarrationNextPageCommand))]
    private int stringNarrationTotalCount = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StringNarrationTotalPages))]
    [NotifyPropertyChangedFor(nameof(StringNarrationPageLabel))]
    [NotifyCanExecuteChangedFor(nameof(NavigateStringNarrationPrevPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NavigateStringNarrationNextPageCommand))]
    private int stringNarrationPageSize = 20;

    [ObservableProperty]
    private bool isStringNarrationPageSizePopupOpen;

    [ObservableProperty]
    private bool isStringNarrationPageSizePopupExpandedOpen;

    public int StringNarrationTotalPages => Math.Max(1, (int)Math.Ceiling((double)StringNarrationTotalCount / StringNarrationPageSize));

    public string StringNarrationPageLabel => $"{StringNarrationCurrentPage} / {StringNarrationTotalPages}";

    private bool CanNavigateStringNarrationPrevPage()
    {
        return !IsStringNarrationBusy && StringNarrationCurrentPage > 1;
    }

    private bool CanNavigateStringNarrationNextPage()
    {
        return !IsStringNarrationBusy && StringNarrationCurrentPage < StringNarrationTotalPages;
    }

    [RelayCommand(CanExecute = nameof(CanNavigateStringNarrationPrevPage))]
    private async Task NavigateStringNarrationPrevPageAsync()
    {
        StringNarrationCurrentPage--;
        await LoadStringNarrationOrdersAsync();
    }

    [RelayCommand(CanExecute = nameof(CanNavigateStringNarrationNextPage))]
    private async Task NavigateStringNarrationNextPageAsync()
    {
        StringNarrationCurrentPage++;
        await LoadStringNarrationOrdersAsync();
    }

    [RelayCommand]
    private async Task ChangeStringNarrationPageSizeAsync(string pageSizeStr)
    {
        if (int.TryParse(pageSizeStr, out var pageSize))
        {
            StringNarrationPageSize = pageSize;
            StringNarrationCurrentPage = 1;
            IsStringNarrationPageSizePopupOpen = false;
            IsStringNarrationPageSizePopupExpandedOpen = false;
            await LoadStringNarrationOrdersAsync();
        }
    }
}
