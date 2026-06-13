using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Core.Commerce.Services;

namespace Orderly.Data.Commerce.Services;

/// <summary>
/// Commerce Service Layer implementation of <see cref="IWorkspaceService"/> over the
/// Universal_Domain_Model (Req 4.1). Provides create/read/update/delete over
/// <see cref="BusinessWorkspace"/>, the scoping root of the model. Reads return only active
/// (non-soft-deleted) workspaces and <see cref="DeleteAsync"/> performs a recoverable soft-delete
/// (Req 2.9), both delegated to the underlying repository.
///
/// <para>This type is industry-agnostic and free of any Forbidden_Term, and reads/writes only
/// through the Commerce repositories so the P0_Security_System (C-2) is unaffected.</para>
/// </summary>
public sealed class CommerceWorkspaceService : IWorkspaceService
{
    private readonly IBusinessWorkspaceRepository _workspaceRepository;

    /// <summary>Creates the service over the Commerce workspace repository.</summary>
    /// <exception cref="ArgumentNullException">Thrown when the repository is null.</exception>
    public CommerceWorkspaceService(IBusinessWorkspaceRepository workspaceRepository)
    {
        _workspaceRepository = workspaceRepository ?? throw new ArgumentNullException(nameof(workspaceRepository));
    }

    /// <inheritdoc />
    public async Task<BusinessWorkspace> CreateAsync(BusinessWorkspace workspace, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return await _workspaceRepository.CreateAsync(workspace, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<BusinessWorkspace?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _workspaceRepository.GetByIdAsync(id, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<BusinessWorkspace>> GetAllAsync(CancellationToken cancellationToken = default)
        => _workspaceRepository.GetAllAsync(cancellationToken);

    /// <inheritdoc />
    public async Task UpdateAsync(BusinessWorkspace workspace, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        await _workspaceRepository.UpdateAsync(workspace, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => _workspaceRepository.DeleteAsync(id, cancellationToken);
}
