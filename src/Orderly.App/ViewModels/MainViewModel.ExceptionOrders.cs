using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    private readonly List<StringNarrationOrderSummary> _exceptionOrdersSource = [];
    private bool _isSynchronizingExceptionSelection;
    private long _selectedExceptionOrderLoadVersion;

    public ObservableCollection<StringNarrationOrderSummary> ExceptionOrders { get; } = new();
    public ObservableCollection<string> ExceptionStatusFilterOptions { get; } = new(new[] { "全部", "待处理", "已解决" });
    public ObservableCollection<StringNarrationExceptionAuditEntry> ExceptionAuditLogs { get; } = new();
    public ObservableCollection<StringNarrationExceptionSampleReplayItem> ExceptionSampleReplayResults { get; } = new();

    public bool HasExceptionOrders => ExceptionOrders.Count > 0;
    public bool HasSelectedExceptionOrderDetail => SelectedExceptionOrderDetail is not null;
    public bool IsExceptionDetailPanelVisible => SelectedExceptionOrderDetail is not null;
    public bool IsExceptionListExpanded => SelectedExceptionOrderDetail is null;
    public bool HasExceptionAuditLogs => ExceptionAuditLogs.Count > 0;
    public bool HasExceptionSampleReplayResults => ExceptionSampleReplayResults.Count > 0;
    public bool IsExceptionOrdersBusy => IsExceptionOrdersLoading;
    public string ExceptionOrdersCountText => $"{ExceptionOrders.Count} 单";
    public string ExceptionTotalCountText => $"{ExceptionTotalCount} 单";
    public string ExceptionAuditLogsCountText => $"{ExceptionAuditLogs.Count} 条";
    public string ExceptionOrdersEmptyStateText => string.IsNullOrWhiteSpace(ExceptionError)
        ? "暂无异常订单，点击刷新拉取。"
        : ExceptionError;
    public int ExceptionTotalPages => Math.Max(1, (int)Math.Ceiling((double)ExceptionTotalCount / ExceptionPageSize));
    public string ExceptionPageLabel => $"{ExceptionCurrentPage} / {ExceptionTotalPages}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExceptionOrdersBusy))]
    [NotifyCanExecuteChangedFor(nameof(LoadExceptionOrdersCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshExceptionOrderDetailCommand))]
    [NotifyCanExecuteChangedFor(nameof(ForceOpenExceptionDetailCommand))]
    private bool isExceptionOrdersLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExceptionOrdersEmptyStateText))]
    private string exceptionError = string.Empty;

    [ObservableProperty]
    private string exceptionStatusMessage = "异常订单未加载";

    [ObservableProperty]
    private string exceptionLookupInput = string.Empty;

    [ObservableProperty]
    private string exceptionKeyword = string.Empty;

    [ObservableProperty]
    private string selectedExceptionStatusFilter = "全部";

    [ObservableProperty]
    private string exceptionActionRemarkInput = string.Empty;

    [ObservableProperty]
    private string exceptionActionAssigneeInput = string.Empty;

    [ObservableProperty]
    private string exceptionActionPriorityInput = string.Empty;

    [ObservableProperty]
    private string exceptionActionResolutionActionInput = string.Empty;

    [ObservableProperty]
    private string exceptionActionOperatorIdInput = string.Empty;

    [ObservableProperty]
    private string exceptionSampleReplayJson = string.Empty;

    [ObservableProperty]
    private string exceptionSampleReplayStatusMessage = "异常样本未回放";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExceptionPageLabel))]
    [NotifyCanExecuteChangedFor(nameof(NavigateExceptionPrevPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NavigateExceptionNextPageCommand))]
    private int exceptionCurrentPage = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExceptionTotalCountText))]
    [NotifyPropertyChangedFor(nameof(ExceptionTotalPages))]
    [NotifyPropertyChangedFor(nameof(ExceptionPageLabel))]
    [NotifyCanExecuteChangedFor(nameof(NavigateExceptionPrevPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NavigateExceptionNextPageCommand))]
    private int exceptionTotalCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExceptionTotalPages))]
    [NotifyPropertyChangedFor(nameof(ExceptionPageLabel))]
    [NotifyCanExecuteChangedFor(nameof(NavigateExceptionPrevPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NavigateExceptionNextPageCommand))]
    private int exceptionPageSize = 20;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedExceptionOrderDetail))]
    [NotifyPropertyChangedFor(nameof(IsExceptionDetailPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsExceptionListExpanded))]
    [NotifyCanExecuteChangedFor(nameof(RefreshExceptionOrderDetailCommand))]
    private StringNarrationOrderSummary? selectedExceptionOrder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedExceptionOrderDetail))]
    [NotifyPropertyChangedFor(nameof(IsExceptionDetailPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsExceptionListExpanded))]
    [NotifyCanExecuteChangedFor(nameof(RefreshExceptionOrderDetailCommand))]
    private StringNarrationOrderDetail? selectedExceptionOrderDetail;

    partial void OnExceptionKeywordChanged(string value)
    {
        ApplyExceptionOrdersFilter();
    }

    partial void OnSelectedExceptionStatusFilterChanged(string value)
    {
        ApplyExceptionOrdersFilter();
    }

    partial void OnSelectedExceptionOrderChanged(StringNarrationOrderSummary? value)
    {
        if (_isSynchronizingExceptionSelection)
        {
            return;
        }

        if (value is null)
        {
            SelectedExceptionOrderDetail = null;
            OnExceptionOrdersCollectionStateChanged();
            return;
        }

        var currentDetail = SelectedExceptionOrderDetail;
        if (currentDetail is not null
            && (string.Equals(currentDetail.OrderNo, value.OrderNo, StringComparison.Ordinal)
                || string.Equals(currentDetail.Id, value.Id, StringComparison.Ordinal)
                || string.Equals(currentDetail.WxOutTradeNo, value.WxOutTradeNo, StringComparison.Ordinal)))
        {
            return;
        }
    }

    public async Task OpenExceptionOrderDetailAsync(StringNarrationOrderSummary? summary)
    {
        if (summary is null)
        {
            return;
        }

        if (!ReferenceEquals(SelectedExceptionOrder, summary))
        {
            _isSynchronizingExceptionSelection = true;
            try
            {
                SelectedExceptionOrder = summary;
            }
            finally
            {
                _isSynchronizingExceptionSelection = false;
            }
        }

        var selectionVersion = ++_selectedExceptionOrderLoadVersion;
        await LoadExceptionOrderDetailAsync(summary.OrderNo, summary.WxOutTradeNo, summary.Id, selectionVersion);
    }

    public void DismissExceptionDetailsForSession()
    {
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
        ExceptionStatusMessage = "已关闭异常详情";
        OnExceptionOrdersCollectionStateChanged();
    }

    partial void OnSelectedExceptionOrderDetailChanged(StringNarrationOrderDetail? value)
    {
        IEnumerable<StringNarrationExceptionAuditEntry> auditLogs = value is null
            ? Array.Empty<StringNarrationExceptionAuditEntry>()
            : value.Exception.AuditLogs.OrderByDescending(log => log.At);
        ReplaceCollection(ExceptionAuditLogs, auditLogs);
        if (value is not null)
        {
            ExceptionActionAssigneeInput = value.Exception.OwnerText == "未分配" ? string.Empty : value.Exception.OwnerText;
            ExceptionActionPriorityInput = value.Exception.NormalizedPriority;
            ExceptionActionResolutionActionInput = value.Exception.ResolutionAction;
            ExceptionActionRemarkInput = value.Exception.AdminResolutionRemark;
        }

        OnExceptionOrdersCollectionStateChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRunExceptionOrdersReadAction))]
    private async Task LoadExceptionOrdersAsync()
    {
        await ExecuteExceptionOrdersReadActionAsync("正在加载异常订单...", async () =>
        {
            var query = BuildStringNarrationQuery();
            query.Page = ExceptionCurrentPage;
            query.PageSize = ExceptionPageSize;
            ValidateTimeRangeOrThrow(query);
            var result = await _stringNarrationOrderService.GetOrdersAsync(query);
            ExceptionTotalCount = result.PageInfo.Total;
            SyncExceptionOrdersFromOrders(result.Orders);
            ExceptionStatusMessage = $"已加载异常订单 {ExceptionOrders.Count} 单，第 {ExceptionCurrentPage} 页 / 共 {ExceptionTotalPages} 页（总数 {result.PageInfo.Total} 单）";
        });
    }

    [RelayCommand(CanExecute = nameof(CanRefreshExceptionOrderDetail))]
    private async Task RefreshExceptionOrderDetailAsync()
    {
        if (SelectedExceptionOrderDetail is not null)
        {
            await LoadExceptionOrderDetailAsync(
                SelectedExceptionOrderDetail.OrderNo,
                SelectedExceptionOrderDetail.WxOutTradeNo,
                SelectedExceptionOrderDetail.Id,
                _selectedExceptionOrderLoadVersion);
            return;
        }

        if (SelectedExceptionOrder is not null)
        {
            await LoadExceptionOrderDetailAsync(
                SelectedExceptionOrder.OrderNo,
                SelectedExceptionOrder.WxOutTradeNo,
                SelectedExceptionOrder.Id,
                _selectedExceptionOrderLoadVersion);
            return;
        }

        var lookup = ExceptionLookupInput.Trim();
        if (string.IsNullOrWhiteSpace(lookup))
        {
            return;
        }

        await LoadExceptionOrderDetailByLookupAsync(lookup);
    }

    [RelayCommand(CanExecute = nameof(CanRunExceptionOrdersReadAction))]
    private async Task ForceOpenExceptionDetailAsync()
    {
        var summary = SelectedExceptionOrder;
        if (summary is null)
        {
            summary = ExceptionOrders.FirstOrDefault();
            if (summary is null)
            {
                ExceptionStatusMessage = "异常列表为空，无法强制打开异常详情。";
                return;
            }

            SelectedExceptionOrder = summary;
        }

        await ExecuteExceptionOrdersReadActionAsync("正在强制打开异常详情...", async () =>
        {
            var detail = await _stringNarrationOrderService.GetOrderDetailAsync(summary.OrderNo, summary.WxOutTradeNo, summary.Id);
            SetExceptionDetailPanelState(detail);
            ExceptionStatusMessage = $"已强制打开异常详情：{detail.OrderNoText}";
        });
    }

    [RelayCommand]
    private void ClearExceptionFilters()
    {
        ExceptionLookupInput = string.Empty;
        ExceptionKeyword = string.Empty;
        SelectedExceptionStatusFilter = "全部";
        ApplyExceptionOrdersFilter();
        ExceptionStatusMessage = "异常筛选已清空";
    }

    [RelayCommand]
    private async Task StartSelectedExceptionProcessingAsync()
    {
        await ApplySelectedExceptionActionAsync(StringNarrationExceptionFieldCatalog.ActionStartProcessing);
    }

    [RelayCommand]
    private async Task ResolveSelectedExceptionAsync()
    {
        await ApplySelectedExceptionActionAsync(StringNarrationExceptionFieldCatalog.ActionResolve);
    }

    [RelayCommand]
    private async Task IgnoreSelectedExceptionAsync()
    {
        await ApplySelectedExceptionActionAsync(StringNarrationExceptionFieldCatalog.ActionIgnore);
    }

    [RelayCommand]
    private async Task ReopenSelectedExceptionAsync()
    {
        await ApplySelectedExceptionActionAsync(StringNarrationExceptionFieldCatalog.ActionReopen);
    }

    [RelayCommand]
    private async Task AssignSelectedExceptionAsync()
    {
        await ApplySelectedExceptionActionAsync(StringNarrationExceptionFieldCatalog.ActionAssign);
    }

    [RelayCommand]
    private async Task ReplayExceptionSampleAsync()
    {
        var payload = ExceptionSampleReplayJson.Trim();
        if (string.IsNullOrWhiteSpace(payload))
        {
            ExceptionSampleReplayStatusMessage = "请先提供异常样本 JSON。";
            return;
        }

        await ExecuteExceptionOrdersReadActionAsync("正在回放异常样本...", async () =>
        {
            var result = await _stringNarrationOrderService.ReplayExceptionSamplesAsync(new StringNarrationExceptionSampleReplayRequest
            {
                Samples =
                [
                    new StringNarrationExceptionSample
                    {
                        Name = "manual-sample",
                        PayloadJson = payload
                    }
                ]
            });

            ReplaceCollection(ExceptionSampleReplayResults, result.Items);
            ExceptionSampleReplayStatusMessage = result.SummaryText;
            ExceptionStatusMessage = $"异常样本回放完成：{result.SummaryText}";
        });
    }

    private bool CanRunExceptionOrdersReadAction()
    {
        return !IsExceptionOrdersBusy;
    }

    private bool CanNavigateExceptionPrevPage()
    {
        return !IsExceptionOrdersBusy && ExceptionCurrentPage > 1;
    }

    private bool CanNavigateExceptionNextPage()
    {
        return !IsExceptionOrdersBusy && ExceptionCurrentPage < ExceptionTotalPages;
    }

    [RelayCommand(CanExecute = nameof(CanNavigateExceptionPrevPage))]
    private async Task NavigateExceptionPrevPageAsync()
    {
        ExceptionCurrentPage--;
        await LoadExceptionOrdersAsync();
    }

    [RelayCommand(CanExecute = nameof(CanNavigateExceptionNextPage))]
    private async Task NavigateExceptionNextPageAsync()
    {
        ExceptionCurrentPage++;
        await LoadExceptionOrdersAsync();
    }

    private bool CanRefreshExceptionOrderDetail()
    {
        return !IsExceptionOrdersBusy && (SelectedExceptionOrder is not null || SelectedExceptionOrderDetail is not null || !string.IsNullOrWhiteSpace(ExceptionLookupInput));
    }

    private async Task ApplySelectedExceptionActionAsync(string action)
    {
        var detail = SelectedExceptionOrderDetail;
        var summary = SelectedExceptionOrder;
        if (detail is null && summary is null)
        {
            ExceptionStatusMessage = "请先选择异常订单。";
            return;
        }

        await ExecuteExceptionOrdersReadActionAsync("正在提交异常处理动作...", async () =>
        {
            var result = await _stringNarrationOrderService.ApplyExceptionActionAsync(new StringNarrationExceptionActionRequest
            {
                Id = detail?.Id ?? summary?.Id ?? string.Empty,
                OrderNo = detail?.OrderNo ?? summary?.OrderNo ?? string.Empty,
                TradeNo = detail?.WxOutTradeNo ?? summary?.WxOutTradeNo ?? string.Empty,
                Action = action,
                ResolutionAction = ExceptionActionResolutionActionInput,
                AdminResolutionRemark = ExceptionActionRemarkInput,
                Assignee = ExceptionActionAssigneeInput,
                Owner = ExceptionActionAssigneeInput,
                Priority = ExceptionActionPriorityInput,
                ResolvedBy = ExceptionActionOperatorIdInput,
                OperatorId = ExceptionActionOperatorIdInput,
                LastCheckedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ActionAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            ApplyExceptionDetail(result.Detail);
            ExceptionAuditLogs.Insert(0, result.AuditEntry);
            ExceptionStatusMessage = result.Message;
        });
    }

    private async Task ExecuteExceptionOrdersReadActionAsync(string busyMessage, Func<Task> action)
    {
        if (IsExceptionOrdersBusy)
        {
            return;
        }

        try
        {
            IsExceptionOrdersLoading = true;
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
            IsExceptionOrdersLoading = false;
            OnExceptionOrdersCollectionStateChanged();
        }
    }

    private async Task LoadExceptionOrderDetailByLookupAsync(string lookup)
    {
        await ExecuteExceptionOrdersReadActionAsync("正在查询异常订单详情...", async () =>
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
            await ExecuteExceptionOrdersReadActionAsync("正在加载异常订单详情...", async () =>
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
            .ThenBy(item => item.ExceptionPrioritySortOrder)
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

        if (SelectedExceptionOrder is null && SelectedExceptionOrderDetail is null)
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
        OnPropertyChanged(nameof(ExceptionOrdersCountText));
        OnPropertyChanged(nameof(ExceptionTotalCountText));
        OnPropertyChanged(nameof(ExceptionTotalPages));
        OnPropertyChanged(nameof(ExceptionPageLabel));
        OnPropertyChanged(nameof(ExceptionAuditLogsCountText));
        OnPropertyChanged(nameof(ExceptionOrdersEmptyStateText));
    }
}
