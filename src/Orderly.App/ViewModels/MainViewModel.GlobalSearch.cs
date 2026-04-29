using CommunityToolkit.Mvvm.Input;
using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    partial void OnWorkbenchTaskFilterChanged(WorkbenchTaskFilter value)
    {
        if (!IsBusy)
        {
            _ = RefreshWorkbenchTasksAsync();
        }
    }

    partial void OnSearchQueryChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length < 2)
        {
            ClearSearchResults();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunSearch))]
    private async Task RunSearchAsync()
    {
        await RefreshSearchResultsAsync(updateStatus: true);
    }

    [RelayCommand(CanExecute = nameof(CanRunSearch))]
    private async Task RefreshSearchAsync()
    {
        await RefreshSearchResultsAsync(updateStatus: true);
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchQuery = string.Empty;
        ClearSearchResults();
        StatusMessage = "搜索结果已清空";
    }

    [RelayCommand(CanExecute = nameof(CanOpenSearchResult))]
    private async Task OpenSearchResultAsync(SearchResultListItem? item)
    {
        var target = item ?? SelectedSearchResult;
        if (target is null)
        {
            return;
        }

        _isSynchronizingSelection = true;
        try
        {
            SelectedSection = "客户/订单";

            if (target.OrderId is int orderId)
            {
                SelectOrderById(orderId);
            }

            if (target.CustomerId is int customerId)
            {
                SelectCustomerById(customerId);
            }
        }
        finally
        {
            _isSynchronizingSelection = false;
        }

        if (target.CustomerId is int selectedCustomerId)
        {
            var customer = SelectedCustomer?.Id == selectedCustomerId
                ? SelectedCustomer
                : Customers.FirstOrDefault(candidate => candidate.Id == selectedCustomerId) ?? _allCustomers.FirstOrDefault(candidate => candidate.Id == selectedCustomerId);
            if (customer is not null)
            {
                SelectedCustomer = customer;
                await ReloadSelectedCustomerDetailsAsync(customer);
                await SyncSearchResultSelectionAsync(target, customer.Id);
            }
        }

        SelectedSearchResult = target;
        StatusMessage = $"已定位搜索结果：{target.Title}";
    }

    private bool CanRunSearch()
    {
        return !IsBusy && SearchQuery.Trim().Length >= 2;
    }

    private bool CanOpenSearchResult()
    {
        return SelectedSearchResult is not null && !IsBusy;
    }

    private async Task RefreshSearchResultsAsync(bool updateStatus, CancellationToken cancellationToken = default)
    {
        var query = SearchQuery.Trim();
        if (query.Length < 2)
        {
            ClearSearchResults();
            if (updateStatus)
            {
                StatusMessage = "搜索关键字至少需要 2 个字符";
            }

            return;
        }

        var previousSelectionId = SelectedSearchResult?.Id;
        var resultSet = await _globalSearchService.SearchAsync(new SearchRequest
        {
            Query = query,
            Limit = 50
        }, cancellationToken);

        ReplaceCollection(SearchResults, resultSet.Items.Select(item => new SearchResultListItem(item)));
        SelectedSearchResult = SearchResults.FirstOrDefault(item => item.Id == previousSelectionId) ?? SearchResults.FirstOrDefault();

        if (updateStatus)
        {
            StatusMessage = SearchResults.Count == 0
                ? $"未找到与“{query}”相关的结果"
                : $"搜索完成，共 {resultSet.TotalCount} 条，当前显示 {SearchResults.Count} 条";
        }
    }

    private void ClearSearchResults()
    {
        ReplaceCollection(SearchResults, Array.Empty<SearchResultListItem>());
        SelectedSearchResult = null;
    }

    private async Task SyncSearchResultSelectionAsync(SearchResultListItem target, int customerId)
    {
        if (target.Type == SearchResultType.AiSuggestion && target.RelatedEntityId is int aiSuggestionId)
        {
            SelectedAiSuggestion = AiSuggestions.FirstOrDefault(item => item.Id == aiSuggestionId) ?? SelectedAiSuggestion;
        }

        if (target.Type == SearchResultType.OcrResult && target.RelatedEntityId is int ocrResultId && CurrentOcrResult?.Id != ocrResultId)
        {
            var ocrResults = await _ocrService.ListByCustomerAsync(customerId);
            var matched = ocrResults.FirstOrDefault(item => item.Id == ocrResultId);
            if (matched is not null)
            {
                CurrentOcrResult = matched;
            }
        }
    }
}
