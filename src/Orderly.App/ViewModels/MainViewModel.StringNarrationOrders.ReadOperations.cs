using CommunityToolkit.Mvvm.Input;
using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

// Read-only string-narration operations: gateway probe, list/stats loads, filter/search/
// refresh, detail loads, paging navigation, and their CanExecute predicates / query builders.
// These are relocated verbatim from the core partial; no payment verification, paid-handling,
// shipping sync, or payment-success-to-fulfillment transaction behavior lives here.
public partial class MainViewModel
{
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
