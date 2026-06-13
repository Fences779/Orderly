using Orderly.Core.Models;
using Orderly.Core.Services;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    private sealed class EmptyWorkbenchTaskService : IWorkbenchTaskService
    {
        public static EmptyWorkbenchTaskService Instance { get; } = new();

        public Task<IReadOnlyList<WorkbenchTask>> GetTasksAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<WorkbenchTask>>([]);
        }

        public Task<IReadOnlyList<WorkbenchTask>> GetTasksAsync(WorkbenchTaskFilter filter, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<WorkbenchTask>>([]);
        }

        public Task<IReadOnlyList<WorkbenchTask>> GetTasksAsync(WorkbenchTaskQuery query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<WorkbenchTask>>([]);
        }
    }

    private sealed class EmptyGlobalSearchService : IGlobalSearchService
    {
        public static EmptyGlobalSearchService Instance { get; } = new();

        public Task<SearchResultSet> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SearchResultSet
            {
                Query = request?.Query?.Trim() ?? string.Empty,
                Limit = request?.Limit ?? 50,
                TotalCount = 0,
                Items = []
            });
        }
    }

    private sealed class EmptyNavigationRouteService : INavigationRouteService
    {
        public static EmptyNavigationRouteService Instance { get; } = new();

        public Task<NavigationRouteResult> ResolveAsync(SearchResultItem item, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new NavigationRouteResult
            {
                CanNavigate = false,
                DisabledReason = "Navigation route service is not configured.",
                StatusMessage = "Navigation route service is not configured."
            });
        }

        public Task<NavigationRouteResult> ResolveAsync(WorkbenchTask task, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new NavigationRouteResult
            {
                CanNavigate = false,
                DisabledReason = "Navigation route service is not configured.",
                StatusMessage = "Navigation route service is not configured."
            });
        }

        public Task<NavigationRouteResult> ResolveAsync(QuickAction action, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new NavigationRouteResult
            {
                CanNavigate = false,
                DisabledReason = "Navigation route service is not configured.",
                StatusMessage = "Navigation route service is not configured."
            });
        }
    }
}
