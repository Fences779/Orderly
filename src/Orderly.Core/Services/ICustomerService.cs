using Orderly.Core.Models;

namespace Orderly.Core.Services;

public interface ICustomerService
{
    Task<IReadOnlyList<Customer>> GetCustomersAsync(CancellationToken cancellationToken = default);
    Task<Customer?> GetCustomerAsync(int id, CancellationToken cancellationToken = default);
    Task<Customer> SaveCustomerAsync(Customer customer, CancellationToken cancellationToken = default);
    Task DeleteCustomerAsync(int id, CancellationToken cancellationToken = default);
}
