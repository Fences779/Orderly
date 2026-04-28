using Orderly.Core.Models;

namespace Orderly.Core.Services;

public interface IAiAssistantService
{
    Task<AiSuggestion> GenerateReplySuggestionAsync(int customerId, int? orderId = null, int? messageId = null, CancellationToken cancellationToken = default);
    Task<AiSuggestion> GenerateAndSaveReplySuggestionAsync(int customerId, int? orderId = null, int? dealId = null, int? messageId = null, CancellationToken cancellationToken = default);
    Task<AiSuggestion> SaveSuggestionAsync(AiSuggestion suggestion, CancellationToken cancellationToken = default);
    Task<AiSuggestion> UpdateSuggestionStatusAsync(int suggestionId, AiSuggestionStatus status, int? dealId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiSuggestion>> ListSuggestionsAsync(int customerId, int? orderId = null, CancellationToken cancellationToken = default);
}
