namespace Orderly.Core.Commerce.Services;

/// <summary>
/// Universal business-task service (Req 4.1). Provides create/read/update/delete over
/// <see cref="BusinessTask"/> records plus explicit task status transitions. Reads return only active
/// (non-soft-deleted) tasks; <see cref="DeleteAsync"/> performs a recoverable soft-delete (Req 2.9).
///
/// <para><b>Status transitions.</b> <see cref="ChangeStatusAsync"/> moves a task to a new
/// <see cref="TaskStatus"/> and keeps the task's <see cref="BusinessTask.CompletedAt"/> consistent
/// with that status: transitioning to <see cref="TaskStatus.Completed"/> stamps the completion time,
/// and transitioning away from <see cref="TaskStatus.Completed"/> clears it. Re-applying the current
/// status is a no-op transition that leaves the task unchanged.</para>
///
/// <para>This contract is industry-agnostic and free of any Forbidden_Term, and reads/writes only
/// through the Commerce repositories so the P0_Security_System (C-2) is unaffected.</para>
/// </summary>
public interface IBusinessTaskService
{
    /// <summary>Persists a new business task and returns it.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="task"/> is null.</exception>
    Task<BusinessTask> CreateAsync(BusinessTask task, CancellationToken cancellationToken = default);

    /// <summary>Returns the active business task with the given id, or <c>null</c> when none.</summary>
    Task<BusinessTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns all active business tasks.</summary>
    Task<IReadOnlyList<BusinessTask>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists changes to an existing business task.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="task"/> is null.</exception>
    Task UpdateAsync(BusinessTask task, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes the business task with the given id so it is excluded from active queries but remains recoverable.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transitions the task identified by <paramref name="taskId"/> to <paramref name="newStatus"/>
    /// and persists the change, returning the updated task. When the target status is
    /// <see cref="TaskStatus.Completed"/> the task's <see cref="BusinessTask.CompletedAt"/> is set to
    /// <paramref name="completedAtUtc"/> (or the current UTC time when omitted); when the target status
    /// is any other value, <see cref="BusinessTask.CompletedAt"/> is cleared. Re-applying the current
    /// status leaves the task unchanged.
    /// </summary>
    /// <param name="taskId">The id of the task to transition.</param>
    /// <param name="newStatus">The status to move the task to.</param>
    /// <param name="completedAtUtc">
    /// The completion timestamp to record when transitioning to <see cref="TaskStatus.Completed"/>;
    /// defaults to the current UTC time when null. Ignored for non-completed target statuses.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The updated task.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no active task with <paramref name="taskId"/> exists.</exception>
    Task<BusinessTask> ChangeStatusAsync(
        Guid taskId,
        TaskStatus newStatus,
        DateTime? completedAtUtc = null,
        CancellationToken cancellationToken = default);
}
