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

        SelectedSearchResult = target;
        var route = await _navigationRouteService.ResolveAsync(target.Result);
        await ApplyNavigationRouteAsync(route, target.Title, SectionWorkbench);
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
}
