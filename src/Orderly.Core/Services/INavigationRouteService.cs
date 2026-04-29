using Orderly.Core.Models;

namespace Orderly.Core.Services;

public interface INavigationRouteService
{
    Task<NavigationRouteResult> ResolveAsync(SearchResultItem item, CancellationToken cancellationToken = default);
    Task<NavigationRouteResult> ResolveAsync(WorkbenchTask task, CancellationToken cancellationToken = default);
    Task<NavigationRouteResult> ResolveAsync(QuickAction action, CancellationToken cancellationToken = default);
}
