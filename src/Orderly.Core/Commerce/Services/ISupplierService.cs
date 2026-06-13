namespace Orderly.Core.Commerce.Services;

/// <summary>
/// Universal supplier service (Req 4.1). Provides create/read/update/delete over
/// <see cref="Supplier"/> records owned by a workspace. Reads return only active (non-soft-deleted)
/// suppliers; <see cref="DeleteAsync"/> performs a recoverable soft-delete (Req 2.9).
///
/// <para>This contract is industry-agnostic and free of any Forbidden_Term, and reads/writes only
/// through the Commerce repositories so the P0_Security_System (C-2) is unaffected.</para>
/// </summary>
public interface ISupplierService
{
    /// <summary>Persists a new supplier and returns it.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="supplier"/> is null.</exception>
    Task<Supplier> CreateAsync(Supplier supplier, CancellationToken cancellationToken = default);

    /// <summary>Returns the active supplier with the given id, or <c>null</c> when none.</summary>
    Task<Supplier?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns all active suppliers.</summary>
    Task<IReadOnlyList<Supplier>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists changes to an existing supplier.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="supplier"/> is null.</exception>
    Task UpdateAsync(Supplier supplier, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes the supplier with the given id so it is excluded from active queries but remains recoverable.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
