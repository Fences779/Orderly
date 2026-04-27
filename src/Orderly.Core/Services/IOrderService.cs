using Orderly.Core.Models;

namespace Orderly.Core.Services;

public interface IOrderService
{
    Task<IReadOnlyList<Order>> GetOrdersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetCustomerOrdersAsync(int customerId, CancellationToken cancellationToken = default);
    Task<Order?> GetOrderAsync(int id, CancellationToken cancellationToken = default);
    Task<Order> SaveOrderAsync(Order order, CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(int id, OrderStatus status, CancellationToken cancellationToken = default);
}
