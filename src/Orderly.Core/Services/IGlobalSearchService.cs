using Orderly.Core.Models;

namespace Orderly.Core.Services;

public interface IGlobalSearchService
{
    Task<SearchResultSet> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default);
}
