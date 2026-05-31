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
    public bool IsExceptionOrdersBusy => IsExceptionOrdersListLoading || IsExceptionOrderDetailLoading;
    public bool IsExceptionOrdersListBusy => IsExceptionOrdersListLoading;
    public bool IsExceptionOrderDetailBusy => IsExceptionOrderDetailLoading;
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
    [NotifyPropertyChangedFor(nameof(IsExceptionOrdersListBusy))]
    [NotifyCanExecuteChangedFor(nameof(LoadExceptionOrdersCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshExceptionOrderDetailCommand))]
    private bool isExceptionOrdersListLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExceptionOrdersBusy))]
    [NotifyPropertyChangedFor(nameof(IsExceptionOrderDetailBusy))]
    [NotifyCanExecuteChangedFor(nameof(LoadExceptionOrdersCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshExceptionOrderDetailCommand))]
    private bool isExceptionOrderDetailLoading;

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
    private bool isExceptionPageSizePopupOpen1;

    [ObservableProperty]
    private bool isExceptionPageSizePopupOpen2;

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

        if (currentDetail is not null)
        {
            _ = OpenExceptionOrderDetailAsync(value);
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
        await ExecuteExceptionOrdersReadActionAsync("正在同步订单信息", true, async () =>
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

        await ExecuteExceptionOrdersReadActionAsync("正在回放异常样本...", false, async () =>
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
        ResetExceptionDetailForPageChange();
        ExceptionCurrentPage--;
        await LoadExceptionOrdersAsync();
    }

    [RelayCommand(CanExecute = nameof(CanNavigateExceptionNextPage))]
    private async Task NavigateExceptionNextPageAsync()
    {
        ResetExceptionDetailForPageChange();
        ExceptionCurrentPage++;
        await LoadExceptionOrdersAsync();
    }

    [RelayCommand]
    private async Task ChangeExceptionPageSizeAsync(string sizeStr)
    {
        if (int.TryParse(sizeStr, out var size))
        {
            ExceptionPageSize = size;
            ExceptionCurrentPage = 1;
            IsExceptionPageSizePopupOpen1 = false;
            IsExceptionPageSizePopupOpen2 = false;
            await LoadExceptionOrdersAsync();
        }
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

        await ExecuteExceptionOrdersReadActionAsync("正在提交异常处理动作...", false, async () =>
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
}
