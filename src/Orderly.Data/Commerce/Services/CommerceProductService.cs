using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Core.Commerce.Services;

namespace Orderly.Data.Commerce.Services;

/// <summary>
/// Commerce Service Layer implementation of <see cref="IProductService"/> over the
/// Universal_Domain_Model (Req 4.1). A thin pass-through over <see cref="IProductRepository"/> that
/// exposes product CRUD to the Products page so it sources data only through the Commerce Service
/// Layer (Req 6.4, 7.3) with no legacy remote call. Industry-agnostic and free of any Forbidden_Term;
/// reads/writes only through the Commerce repository so the P0_Security_System (C-2) is unaffected.
/// </summary>
public sealed class CommerceProductService : IProductService
{
    private readonly IProductRepository _productRepository;

    /// <summary>Creates the service over the Commerce product repository.</summary>
    /// <exception cref="ArgumentNullException">Thrown when the repository is null.</exception>
    public CommerceProductService(IProductRepository productRepository)
    {
        _productRepository = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken = default)
        => _productRepository.GetAllAsync(cancellationToken);

    /// <inheritdoc />
    public Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _productRepository.GetByIdAsync(id, cancellationToken);

    /// <inheritdoc />
    public Task<Product> CreateAsync(Product product, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);
        return _productRepository.CreateAsync(product, cancellationToken);
    }

    /// <inheritdoc />
    public Task UpdateAsync(Product product, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);
        return _productRepository.UpdateAsync(product, cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => _productRepository.DeleteAsync(id, cancellationToken);
}
