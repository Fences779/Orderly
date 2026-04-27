using Orderly.Core.Models;

namespace Orderly.Core.Services;

public interface IDealService
{
    Task<IReadOnlyList<Deal>> GetDealsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Deal>> GetCustomerDealsAsync(int customerId, CancellationToken cancellationToken = default);
    Task<Deal?> GetDealAsync(int id, CancellationToken cancellationToken = default);
    Task<Deal> SaveDealAsync(Deal deal, CancellationToken cancellationToken = default);
    Task UpdateStageAsync(int id, DealStage stage, CancellationToken cancellationToken = default);
}
