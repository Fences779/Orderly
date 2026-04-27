using Orderly.Core.Models;

namespace Orderly.Core.Repositories;

public interface IOrderRepository
{
    Task<IReadOnlyList<MerchantOrder>> GetRecentAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MerchantOrder>> ListByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default);
    Task<MerchantOrder?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<MerchantOrder> CreateAsync(MerchantOrder order, CancellationToken cancellationToken = default);
    Task UpdateAsync(MerchantOrder order, CancellationToken cancellationToken = default);
    Task SoftDeleteAsync(int id, CancellationToken cancellationToken = default);
}
