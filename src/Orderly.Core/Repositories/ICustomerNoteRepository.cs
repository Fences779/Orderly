using Orderly.Core.Models;

namespace Orderly.Core.Repositories;

public interface ICustomerNoteRepository
{
    Task<CustomerNote> CreateAsync(CustomerNote note, CancellationToken cancellationToken = default);
    Task<CustomerNote?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomerNote>> ListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomerNote>> ListByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default);
    Task UpdateAsync(CustomerNote note, CancellationToken cancellationToken = default);
    Task SoftDeleteAsync(int id, CancellationToken cancellationToken = default);
}
