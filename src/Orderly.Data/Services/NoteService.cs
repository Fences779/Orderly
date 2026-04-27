using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class NoteService : INoteService
{
    private readonly ICustomerNoteRepository _noteRepository;
    private readonly IActivityLogRepository _activityLogRepository;

    public NoteService(ICustomerNoteRepository noteRepository, IActivityLogRepository activityLogRepository)
    {
        _noteRepository = noteRepository;
        _activityLogRepository = activityLogRepository;
    }

    public Task<IReadOnlyList<CustomerNote>> GetNotesAsync(CancellationToken cancellationToken = default)
    {
        return _noteRepository.ListAsync(cancellationToken);
    }

    public Task<IReadOnlyList<CustomerNote>> GetCustomerNotesAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return _noteRepository.ListByCustomerIdAsync(customerId, cancellationToken);
    }

    public Task<CustomerNote?> GetNoteAsync(int id, CancellationToken cancellationToken = default)
    {
        return _noteRepository.GetByIdAsync(id, cancellationToken);
    }

    public async Task<CustomerNote> SaveNoteAsync(CustomerNote note, string activityMetadataJson = "", CancellationToken cancellationToken = default)
    {
        if (note.Id <= 0)
        {
            var created = await _noteRepository.CreateAsync(note, cancellationToken);
            await AddActivityAsync(ActivityType.NoteCreated, created.CustomerId, created.DealId, created.OrderId, "新增客户备注", created.Content, activityMetadataJson, cancellationToken);
            return created;
        }

        await _noteRepository.UpdateAsync(note, cancellationToken);
        return note;
    }

    public Task DeleteNoteAsync(int id, CancellationToken cancellationToken = default)
    {
        return _noteRepository.SoftDeleteAsync(id, cancellationToken);
    }

    private Task AddActivityAsync(ActivityType type, int? customerId, int? dealId, int? orderId, string title, string description, string metadataJson, CancellationToken cancellationToken)
    {
        return _activityLogRepository.CreateAsync(new ActivityLog
        {
            Type = type,
            CustomerId = customerId,
            DealId = dealId,
            OrderId = orderId,
            Title = title,
            Description = description,
            Operator = "local",
            MetadataJson = metadataJson
        }, cancellationToken);
    }
}
