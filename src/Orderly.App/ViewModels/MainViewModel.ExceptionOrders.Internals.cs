using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    private async Task ExecuteExceptionOrdersReadActionAsync(string busyMessage, bool isListOperation, Func<Task> action)
    {
        if (IsExceptionOrdersBusy)
        {
            return;
        }

        try
        {
            SetExceptionOrdersBusyState(isListOperation, true);
            ExceptionError = string.Empty;
            ExceptionStatusMessage = busyMessage;
            await action();
        }
        catch (Exception ex)
        {
            ExceptionError = ex.Message;
            ExceptionStatusMessage = $"异常订单调用失败：{ex.Message}";
        }
        finally
        {
            SetExceptionOrdersBusyState(isListOperation, false);
            OnExceptionOrdersCollectionStateChanged();
        }
    }

    private async Task LoadExceptionOrderDetailByLookupAsync(string lookup)
    {
        await ExecuteExceptionOrdersReadActionAsync("正在查询异常订单详情...", false, async () =>
        {
            StringNarrationOrderDetail detail;
            try
            {
                detail = await _stringNarrationOrderService.GetOrderDetailAsync(orderNo: lookup);
            }
            catch (InvalidOperationException) when (!lookup.StartsWith("CS", StringComparison.OrdinalIgnoreCase))
            {
                detail = await _stringNarrationOrderService.GetOrderDetailAsync(orderNo: string.Empty, tradeNo: lookup);
            }

            ApplyExceptionDetail(detail);
            ExceptionStatusMessage = $"已加载异常详情：{detail.OrderNoText}";
        });
    }

    private async Task LoadExceptionOrderDetailAsync(string orderNo, string tradeNo, string id, long? selectionVersion = null)
    {
        while (true)
        {
            if (selectionVersion.HasValue && !IsCurrentExceptionSelection(orderNo, tradeNo, id, selectionVersion.Value))
            {
                return;
            }

            var started = false;
            await ExecuteExceptionOrdersReadActionAsync("正在加载异常订单详情...", false, async () =>
            {
                started = true;
                var detail = await _stringNarrationOrderService.GetOrderDetailAsync(orderNo, tradeNo, id);
                if (selectionVersion.HasValue && !IsCurrentExceptionSelection(orderNo, tradeNo, id, selectionVersion.Value))
                {
                    return;
                }

                ApplyExceptionDetail(detail);
                ExceptionStatusMessage = $"已加载异常详情：{detail.OrderNoText}";
            });

            if (started || !selectionVersion.HasValue)
            {
                return;
            }

            await Task.Delay(50);
        }
    }

    private void ApplyExceptionDetail(StringNarrationOrderDetail detail)
    {
        _isSynchronizingExceptionSelection = true;
        try
        {
            SetExceptionDetailPanelState(detail);
            UpsertExceptionOrder(detail);
            UpdateStringNarrationSummary(detail);
            SelectExceptionSummaryByDetail(detail);
        }
        finally
        {
            _isSynchronizingExceptionSelection = false;
        }
    }

    private void SetExceptionDetailPanelState(StringNarrationOrderDetail detail)
    {
        EnsureExceptionDetailKeepsListContext(detail);
        SelectedExceptionOrderDetail = detail;
        OnPropertyChanged(nameof(IsExceptionDetailPanelVisible));
        OnPropertyChanged(nameof(IsExceptionListExpanded));
    }

    private void ResetExceptionDetailForPageChange()
    {
        _selectedExceptionOrderLoadVersion++;
        _isSynchronizingExceptionSelection = true;
        try
        {
            SelectedExceptionOrder = null;
        }
        finally
        {
            _isSynchronizingExceptionSelection = false;
        }

        SelectedExceptionOrderDetail = null;
        OnExceptionOrdersCollectionStateChanged();
    }

    private void SetExceptionOrdersBusyState(bool isListOperation, bool isBusy)
    {
        if (isListOperation)
        {
            IsExceptionOrdersListLoading = isBusy;
            return;
        }

        IsExceptionOrderDetailLoading = isBusy;
    }

    private void EnsureExceptionDetailKeepsListContext(StringNarrationOrderDetail detail)
    {
        if (detail.HasException)
        {
            return;
        }

        var selectedSummary = SelectedExceptionOrder;
        if (selectedSummary is null || !selectedSummary.HasException)
        {
            return;
        }

        var isSameOrder = string.Equals(selectedSummary.OrderNo, detail.OrderNo, StringComparison.Ordinal)
            || string.Equals(selectedSummary.Id, detail.Id, StringComparison.Ordinal)
            || string.Equals(selectedSummary.WxOutTradeNo, detail.WxOutTradeNo, StringComparison.Ordinal);
        if (!isSameOrder)
        {
            return;
        }

        detail.HasException = true;
        detail.Exception = selectedSummary.Exception;
    }

    private void SyncExceptionOrdersFromOrders(IReadOnlyList<StringNarrationOrderSummary> orders)
    {
        _exceptionOrdersSource.Clear();
        foreach (var order in orders.Where(item => item.HasException))
        {
            _exceptionOrdersSource.Add(order);
        }

        ApplyExceptionOrdersFilter();
    }

    private void UpsertExceptionOrder(StringNarrationOrderSummary summary)
    {
        if (!summary.HasException)
        {
            var existingIndex = _exceptionOrdersSource.FindIndex(item =>
                string.Equals(item.Id, summary.Id, StringComparison.Ordinal)
                || string.Equals(item.OrderNo, summary.OrderNo, StringComparison.Ordinal));
            if (existingIndex >= 0)
            {
                _exceptionOrdersSource.RemoveAt(existingIndex);
            }

            ApplyExceptionOrdersFilter();
            return;
        }

        var index = _exceptionOrdersSource.FindIndex(item =>
            string.Equals(item.Id, summary.Id, StringComparison.Ordinal)
            || string.Equals(item.OrderNo, summary.OrderNo, StringComparison.Ordinal));
        if (index < 0)
        {
            _exceptionOrdersSource.Add(summary);
        }
        else
        {
            _exceptionOrdersSource[index] = summary;
        }

        ApplyExceptionOrdersFilter();
    }

    private void SelectExceptionSummaryByDetail(StringNarrationOrderDetail detail)
    {
        var match = ExceptionOrders.FirstOrDefault(item =>
            string.Equals(item.OrderNo, detail.OrderNo, StringComparison.Ordinal)
            || string.Equals(item.Id, detail.Id, StringComparison.Ordinal)
            || string.Equals(item.WxOutTradeNo, detail.WxOutTradeNo, StringComparison.Ordinal));
        if (match is null)
        {
            return;
        }

        _isSynchronizingExceptionSelection = true;
        try
        {
            SelectedExceptionOrder = match;
        }
        finally
        {
            _isSynchronizingExceptionSelection = false;
        }
    }

    private bool IsCurrentExceptionSelection(string orderNo, string tradeNo, string id, long selectionVersion)
    {
        if (selectionVersion != _selectedExceptionOrderLoadVersion)
        {
            return false;
        }

        var selectedSummary = SelectedExceptionOrder;
        if (selectedSummary is null)
        {
            return false;
        }

        return string.Equals(selectedSummary.OrderNo, orderNo, StringComparison.Ordinal)
            || string.Equals(selectedSummary.WxOutTradeNo, tradeNo, StringComparison.Ordinal)
            || string.Equals(selectedSummary.Id, id, StringComparison.Ordinal);
    }

    private void ApplyExceptionOrdersFilter()
    {
        var keyword = ExceptionKeyword.Trim();
        var statusFilter = SelectedExceptionStatusFilter;
        var filtered = _exceptionOrdersSource
            .Where(item => item.HasException)
            .Where(item => string.IsNullOrWhiteSpace(keyword) || MatchExceptionKeyword(item, keyword))
            .Where(item => statusFilter switch
            {
                "待处理" => !item.Exception.IsResolved,
                "已解决" => item.Exception.IsResolved,
                _ => true
            })
            .OrderBy(item => item.ExceptionResolvedSortOrder)
            .ThenBy(item => item.ExceptionSeveritySortOrder)
            .ThenBy(item => item.ExceptionSlaDueSortTimestamp > 0 ? item.ExceptionSlaDueSortTimestamp : long.MaxValue)
            .ThenBy(item => item.ExceptionResolutionStatusSortOrder)
            .ThenByDescending(item => item.ExceptionDetectedSortTimestamp)
            .ThenByDescending(item => item.PaidAt)
            .ToList();

        var selectedOrderNo = SelectedExceptionOrder?.OrderNo ?? SelectedExceptionOrderDetail?.OrderNo ?? string.Empty;
        ReplaceCollection(ExceptionOrders, filtered);
        _isSynchronizingExceptionSelection = true;
        try
        {
            SelectedExceptionOrder = string.IsNullOrWhiteSpace(selectedOrderNo)
                ? null
                : ExceptionOrders.FirstOrDefault(item => string.Equals(item.OrderNo, selectedOrderNo, StringComparison.Ordinal));
        }
        finally
        {
            _isSynchronizingExceptionSelection = false;
        }

        if (SelectedExceptionOrder is null)
        {
            SelectedExceptionOrderDetail = null;
        }

        OnExceptionOrdersCollectionStateChanged();
    }

    private static bool MatchExceptionKeyword(StringNarrationOrderSummary summary, string keyword)
    {
        return summary.OrderNo.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || summary.WxOutTradeNo.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || summary.TitleSnapshot.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || summary.ExceptionSummaryText.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || summary.ExceptionStatusText.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || summary.ExceptionLevelText.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || summary.Exception.Owner.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || summary.Exception.Assignee.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || summary.Exception.Priority.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || summary.Exception.NormalizedPriority.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || summary.Exception.PriorityLabel.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || summary.Exception.ResolutionStatus.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || summary.Exception.NormalizedResolutionStatus.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || summary.Exception.ResolutionStatusLabel.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || summary.Exception.ResolutionAction.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || summary.Exception.ResolvedBy.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || summary.Exception.AdminResolutionRemark.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || summary.Exception.EffectiveType.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || summary.Exception.EffectiveCode.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || summary.Exception.EffectiveReason.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || summary.Exception.ExceptionCategoryLabel.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private void OnExceptionOrdersCollectionStateChanged()
    {
        OnPropertyChanged(nameof(HasExceptionOrders));
        OnPropertyChanged(nameof(HasSelectedExceptionOrderDetail));
        OnPropertyChanged(nameof(IsExceptionDetailPanelVisible));
        OnPropertyChanged(nameof(IsExceptionListExpanded));
        OnPropertyChanged(nameof(HasExceptionAuditLogs));
        OnPropertyChanged(nameof(HasExceptionSampleReplayResults));
        OnPropertyChanged(nameof(IsExceptionOrdersBusy));
        OnPropertyChanged(nameof(IsExceptionOrdersListBusy));
        OnPropertyChanged(nameof(IsExceptionOrderDetailBusy));
        OnPropertyChanged(nameof(ExceptionOrdersCountText));
        OnPropertyChanged(nameof(ExceptionTotalCountText));
        OnPropertyChanged(nameof(ExceptionTotalPages));
        OnPropertyChanged(nameof(ExceptionPageLabel));
        OnPropertyChanged(nameof(ExceptionAuditLogsCountText));
        OnPropertyChanged(nameof(ExceptionOrdersEmptyStateText));
    }
}
