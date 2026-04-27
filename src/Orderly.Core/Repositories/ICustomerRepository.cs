using Orderly.Core.Models;

namespace Orderly.Core.Repositories;

public interface ICustomerRepository
{
    Task<IReadOnlyList<Customer>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Customer?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Customer> CreateAsync(Customer customer, CancellationToken cancellationToken = default);
    Task UpdateAsync(Customer customer, CancellationToken cancellationToken = default);
    Task SoftDeleteAsync(int id, CancellationToken cancellationToken = default);
}
