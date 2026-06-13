namespace Orderly.Core.Commerce.Services;

/// <summary>
/// Universal workspace service (Req 4.1). Provides create/read/update/delete over
/// <see cref="BusinessWorkspace"/>, the scoping root of the Universal_Domain_Model. Reads return only
/// active (non-soft-deleted) workspaces; <see cref="DeleteAsync"/> performs a recoverable soft-delete
/// (Req 2.9).
///
/// <para>This contract is industry-agnostic and free of any Forbidden_Term, and reads/writes only
/// through the Commerce repositories so the P0_Security_System (C-2) is unaffected.</para>
/// </summary>
public interface IWorkspaceService
{
    /// <summary>Persists a new workspace and returns it.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="workspace"/> is null.</exception>
    Task<BusinessWorkspace> CreateAsync(BusinessWorkspace workspace, CancellationToken cancellationToken = default);

    /// <summary>Returns the active workspace with the given id, or <c>null</c> when none.</summary>
    Task<BusinessWorkspace?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns all active workspaces.</summary>
    Task<IReadOnlyList<BusinessWorkspace>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists changes to an existing workspace.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="workspace"/> is null.</exception>
    Task UpdateAsync(BusinessWorkspace workspace, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes the workspace with the given id so it is excluded from active queries but remains recoverable.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
