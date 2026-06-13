using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Core.Commerce.Services;

namespace Orderly.Data.Commerce.Services;

/// <summary>
/// Commerce Service Layer implementation of <see cref="ISupplierService"/> over the
/// Universal_Domain_Model (Req 4.1). Provides create/read/update/delete over <see cref="Supplier"/>
/// records owned by a workspace. Reads return only active (non-soft-deleted) suppliers and
/// <see cref="DeleteAsync"/> performs a recoverable soft-delete (Req 2.9), both delegated to the
/// underlying repository.
///
/// <para>This type is industry-agnostic and free of any Forbidden_Term, and reads/writes only
/// through the Commerce repositories so the P0_Security_System (C-2) is unaffected.</para>
/// </summary>
public sealed class CommerceSupplierService : ISupplierService
{
    private readonly ISupplierRepository _supplierRepository;

    /// <summary>Creates the service over the Commerce supplier repository.</summary>
    /// <exception cref="ArgumentNullException">Thrown when the repository is null.</exception>
    public CommerceSupplierService(ISupplierRepository supplierRepository)
    {
        _supplierRepository = supplierRepository ?? throw new ArgumentNullException(nameof(supplierRepository));
    }

    /// <inheritdoc />
    public async Task<Supplier> CreateAsync(Supplier supplier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(supplier);
        return await _supplierRepository.CreateAsync(supplier, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Supplier?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _supplierRepository.GetByIdAsync(id, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<Supplier>> GetAllAsync(CancellationToken cancellationToken = default)
        => _supplierRepository.GetAllAsync(cancellationToken);

    /// <inheritdoc />
    public async Task UpdateAsync(Supplier supplier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(supplier);
        await _supplierRepository.UpdateAsync(supplier, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => _supplierRepository.DeleteAsync(id, cancellationToken);
}
