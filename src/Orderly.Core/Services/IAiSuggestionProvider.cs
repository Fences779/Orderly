using Orderly.Core.Models;

namespace Orderly.Core.Services;

public interface IAiSuggestionProvider
{
    string Name { get; }

    Task<AiSuggestionProviderResult> GenerateAsync(AiSuggestionRequest request, CancellationToken cancellationToken = default);
}
