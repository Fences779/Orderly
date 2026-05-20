using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    private readonly List<StringNarrationOrderSummary> _exceptionOrdersSource = [];
    private bool _isSynchronizingExceptionSelection;

    public ObservableCollection<StringNarrationOrderSummary> ExceptionOrders { get; } = new();
    public ObservableCollection<string> ExceptionStatusFilterOptions { get; } = new(new[] { "全部", "待处理", "已解决" });

    public bool HasExceptionOrders => ExceptionOrders.Count > 0;
    public bool HasSelectedExceptionOrderDetail => SelectedExceptionOrderDetail is not null;
    public bool IsExceptionOrdersBusy => IsExceptionOrdersLoading;
    public string ExceptionOrdersCountText => $"{ExceptionOrders.Count} 单";
    public string ExceptionOrdersEmptyStateText => string.IsNullOrWhiteSpace(ExceptionError)
        ? "暂无异常订单，点击刷新拉取。"
        : ExceptionError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExceptionOrdersBusy))]
    [NotifyCanExecuteChangedFor(nameof(LoadExceptionOrdersCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshExceptionOrderDetailCommand))]
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
    [NotifyPropertyChangedFor(nameof(HasSelectedExceptionOrderDetail))]
    [NotifyCanExecuteChangedFor(nameof(RefreshExceptionOrderDetailCommand))]
    private StringNarrationOrderSummary? selectedExceptionOrder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedExceptionOrderDetail))]
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

        _ = LoadExceptionOrderDetailAsync(value.OrderNo, value.WxOutTradeNo, value.Id);
    }

    [RelayCommand(CanExecute = nameof(CanRunExceptionOrdersReadAction))]
    private async Task LoadExceptionOrdersAsync()
    {
        await ExecuteExceptionOrdersReadActionAsync("正在加载异常订单...", async () =>
        {
            var query = BuildStringNarrationQuery();
            query.Page = 1;
            query.PageSize = 50;
            ValidateTimeRangeOrThrow(query);
            var result = await _stringNarrationOrderService.GetOrdersAsync(query);
            SyncExceptionOrdersFromOrders(result.Orders);
            ExceptionStatusMessage = $"已加载异常订单 {ExceptionOrders.Count} 单（原始列表 {result.Orders.Count} 单）";
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
                SelectedExceptionOrderDetail.Id);
            return;
        }

        if (SelectedExceptionOrder is not null)
        {
            await LoadExceptionOrderDetailAsync(
                SelectedExceptionOrder.OrderNo,
                SelectedExceptionOrder.WxOutTradeNo,
                SelectedExceptionOrder.Id);
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

    private bool CanRunExceptionOrdersReadAction()
    {
        return !IsExceptionOrdersBusy;
    }

    private bool CanRefreshExceptionOrderDetail()
    {
        return !IsExceptionOrdersBusy && (SelectedExceptionOrder is not null || SelectedExceptionOrderDetail is not null || !string.IsNullOrWhiteSpace(ExceptionLookupInput));
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

    private async Task LoadExceptionOrderDetailAsync(string orderNo, string tradeNo, string id)
    {
        await ExecuteExceptionOrdersReadActionAsync("正在加载异常订单详情...", async () =>
        {
            var detail = await _stringNarrationOrderService.GetOrderDetailAsync(orderNo, tradeNo, id);
            ApplyExceptionDetail(detail);
            ExceptionStatusMessage = $"已加载异常详情：{detail.OrderNoText}";
        });
    }

    private void ApplyExceptionDetail(StringNarrationOrderDetail detail)
    {
        SelectedExceptionOrderDetail = detail;
        UpsertExceptionOrder(detail);
        UpdateStringNarrationSummary(detail);
        SelectExceptionSummaryByDetail(detail);
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
            .OrderByDescending(item => item.Exception.DetectedAt > 0 ? item.Exception.DetectedAt : item.FulfillmentUpdatedAt)
            .ThenByDescending(item => item.PaidAt)
            .ToList();

        var selectedOrderNo = SelectedExceptionOrder?.OrderNo ?? SelectedExceptionOrderDetail?.OrderNo ?? string.Empty;
        ReplaceCollection(ExceptionOrders, filtered);
        _isSynchronizingExceptionSelection = true;
        try
        {
            SelectedExceptionOrder = ExceptionOrders.FirstOrDefault(item => string.Equals(item.OrderNo, selectedOrderNo, StringComparison.Ordinal))
                ?? ExceptionOrders.FirstOrDefault();
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
            || summary.ExceptionLevelText.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private void OnExceptionOrdersCollectionStateChanged()
    {
        OnPropertyChanged(nameof(HasExceptionOrders));
        OnPropertyChanged(nameof(HasSelectedExceptionOrderDetail));
        OnPropertyChanged(nameof(IsExceptionOrdersBusy));
        OnPropertyChanged(nameof(ExceptionOrdersCountText));
        OnPropertyChanged(nameof(ExceptionOrdersEmptyStateText));
    }
}
