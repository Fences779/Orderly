using Orderly.Core.Models;

namespace Orderly.Core.Repositories;

public interface IDealRepository
{
    Task<Deal> CreateAsync(Deal deal, CancellationToken cancellationToken = default);
    Task<Deal?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Deal>> ListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Deal>> ListByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default);
    Task UpdateAsync(Deal deal, CancellationToken cancellationToken = default);
    Task SoftDeleteAsync(int id, CancellationToken cancellationToken = default);
}
