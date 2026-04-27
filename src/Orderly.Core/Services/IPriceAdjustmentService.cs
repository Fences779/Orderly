using Orderly.Core.Models;

namespace Orderly.Core.Services;

public interface IPriceAdjustmentService
{
    Task<IReadOnlyList<PriceAdjustment>> GetPendingAdjustmentsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PriceAdjustment>> GetOrderAdjustmentsAsync(int orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PriceAdjustment>> GetCustomerAdjustmentsAsync(int customerId, CancellationToken cancellationToken = default);
    Task<PriceAdjustment> SaveAdjustmentAsync(PriceAdjustment adjustment, CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(int id, PriceAdjustmentStatus status, CancellationToken cancellationToken = default);
}
