using Orderly.Core.Models;

namespace Orderly.Core.Repositories;

public interface IOcrResultRepository
{
    Task<OcrResult> CreateAsync(OcrResult result, CancellationToken cancellationToken = default);
    Task UpdateAsync(OcrResult result, CancellationToken cancellationToken = default);
    Task<OcrResult?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OcrResult>> ListByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default);
}
