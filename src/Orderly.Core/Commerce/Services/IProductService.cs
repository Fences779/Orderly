namespace Orderly.Core.Commerce.Services;

/// <summary>
/// Universal product service (Req 4.1). Provides create/read/update/delete over <see cref="Product"/>
/// records of the Universal_Domain_Model. Reads return only active (non-soft-deleted) products;
/// <see cref="DeleteAsync"/> performs a recoverable soft-delete (Req 2.9).
///
/// <para>This contract is industry-agnostic and free of any Forbidden_Term, and reads/writes only
/// through the Commerce repositories so the P0_Security_System (C-2) is unaffected. It is the
/// Commerce Service Layer entry point the Products page sources its data from (Req 6.4, 7.3).</para>
/// </summary>
public interface IProductService
{
    /// <summary>Returns all active products.</summary>
    Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the active product with the given id, or <c>null</c> when none.</summary>
    Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Persists a new product and returns it.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="product"/> is null.</exception>
    Task<Product> CreateAsync(Product product, CancellationToken cancellationToken = default);

    /// <summary>Persists changes to an existing product.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="product"/> is null.</exception>
    Task UpdateAsync(Product product, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes the product with the given id so it is excluded from active queries but remains recoverable.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
