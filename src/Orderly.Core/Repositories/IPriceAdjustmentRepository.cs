using Orderly.Core.Models;

namespace Orderly.Core.Repositories;

public interface IPriceAdjustmentRepository
{
    Task<PriceAdjustment> CreateAsync(PriceAdjustment adjustment, CancellationToken cancellationToken = default);
    Task<PriceAdjustment?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PriceAdjustment>> ListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PriceAdjustment>> ListByOrderIdAsync(int orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PriceAdjustment>> ListByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PriceAdjustment>> ListPendingAsync(CancellationToken cancellationToken = default);
    Task UpdateAsync(PriceAdjustment adjustment, CancellationToken cancellationToken = default);
    Task SoftDeleteAsync(int id, CancellationToken cancellationToken = default);
}
