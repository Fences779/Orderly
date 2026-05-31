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

    [ObservableProperty]
    private string stringNarrationWorkbenchInventoryTestStatus = "需注意";

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

    private readonly record struct StringNarrationSelectionSnapshot(string OrderNo, string TradeNo, string Id, bool ShouldOpenDetail)
    {
        public static readonly StringNarrationSelectionSnapshot Empty = new(string.Empty, string.Empty, string.Empty, false);

        public bool IsEmpty => string.IsNullOrWhiteSpace(OrderNo)
            && string.IsNullOrWhiteSpace(TradeNo)
            && string.IsNullOrWhiteSpace(Id);
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
