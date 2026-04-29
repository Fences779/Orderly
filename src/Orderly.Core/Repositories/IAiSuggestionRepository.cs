using Orderly.Core.Models;

namespace Orderly.Core.Repositories;

public interface IAiSuggestionRepository
{
    Task<AiSuggestion> CreateAsync(AiSuggestion suggestion, CancellationToken cancellationToken = default);
    Task UpdateAsync(AiSuggestion suggestion, CancellationToken cancellationToken = default);
    Task<AiSuggestion?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiSuggestion>> ListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiSuggestion>> ListByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiSuggestion>> ListByOrderIdAsync(int orderId, CancellationToken cancellationToken = default);
}
