using Orderly.Core.Models;

namespace Orderly.Core.Repositories;

public interface IOrderRepository
{
    Task<IReadOnlyList<MerchantOrder>> GetRecentAsync(CancellationToken cancellationToken = default);
    Task<MerchantOrder?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
}
