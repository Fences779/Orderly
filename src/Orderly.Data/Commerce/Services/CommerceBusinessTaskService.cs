using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Core.Commerce.Services;

namespace Orderly.Data.Commerce.Services;

/// <summary>
/// Commerce Service Layer implementation of <see cref="IBusinessTaskService"/> over the
/// Universal_Domain_Model (Req 4.1). Provides create/read/update/delete over
/// <see cref="BusinessTask"/> records plus explicit status transitions. Reads return only active
/// (non-soft-deleted) tasks and <see cref="DeleteAsync"/> performs a recoverable soft-delete
/// (Req 2.9), both delegated to the underlying repository.
///
/// <para><b>Status transitions.</b> <see cref="ChangeStatusAsync"/> moves a task to a new
/// <see cref="TaskStatus"/> and keeps <see cref="BusinessTask.CompletedAt"/> consistent with that
/// status: transitioning to <see cref="TaskStatus.Completed"/> stamps the completion time, and
/// transitioning away from it clears the timestamp. Re-applying the current status is a no-op that
/// leaves the task unchanged.</para>
///
/// <para>This type is industry-agnostic and free of any Forbidden_Term, and reads/writes only
/// through the Commerce repositories so the P0_Security_System (C-2) is unaffected.</para>
/// </summary>
public sealed class CommerceBusinessTaskService : IBusinessTaskService
{
    private readonly IBusinessTaskRepository _taskRepository;

    /// <summary>Creates the service over the Commerce business-task repository.</summary>
    /// <exception cref="ArgumentNullException">Thrown when the repository is null.</exception>
    public CommerceBusinessTaskService(IBusinessTaskRepository taskRepository)
    {
        _taskRepository = taskRepository ?? throw new ArgumentNullException(nameof(taskRepository));
    }

    /// <inheritdoc />
    public async Task<BusinessTask> CreateAsync(BusinessTask task, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        return await _taskRepository.CreateAsync(task, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<BusinessTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _taskRepository.GetByIdAsync(id, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<BusinessTask>> GetAllAsync(CancellationToken cancellationToken = default)
        => _taskRepository.GetAllAsync(cancellationToken);

    /// <inheritdoc />
    public async Task UpdateAsync(BusinessTask task, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        await _taskRepository.UpdateAsync(task, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => _taskRepository.DeleteAsync(id, cancellationToken);

    /// <inheritdoc />
    public async Task<BusinessTask> ChangeStatusAsync(
        Guid taskId,
        Orderly.Core.Commerce.TaskStatus newStatus,
        DateTime? completedAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        BusinessTask task = await _taskRepository.GetByIdAsync(taskId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Business task '{taskId}' was not found.");

        // Re-applying the current status is a no-op transition that leaves the task unchanged.
        if (task.Status == newStatus)
        {
            return task;
        }

        task.Status = newStatus;
        task.CompletedAt = newStatus == Orderly.Core.Commerce.TaskStatus.Completed
            ? (completedAtUtc ?? DateTime.UtcNow)
            : null;

        await _taskRepository.UpdateAsync(task, cancellationToken).ConfigureAwait(false);
        return task;
    }
}
