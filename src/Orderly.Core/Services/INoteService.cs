using Orderly.Core.Models;

namespace Orderly.Core.Services;

public interface INoteService
{
    Task<IReadOnlyList<CustomerNote>> GetNotesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomerNote>> GetCustomerNotesAsync(int customerId, CancellationToken cancellationToken = default);
    Task<CustomerNote?> GetNoteAsync(int id, CancellationToken cancellationToken = default);
    Task<CustomerNote> SaveNoteAsync(CustomerNote note, string activityMetadataJson = "", CancellationToken cancellationToken = default);
    Task DeleteNoteAsync(int id, CancellationToken cancellationToken = default);
}
