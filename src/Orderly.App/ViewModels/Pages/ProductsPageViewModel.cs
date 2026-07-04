using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Services;

namespace Orderly.App.ViewModels.Pages;

/// <summary>One row of the Products page, projected from a Universal_Domain_Model <see cref="Product"/>.</summary>
public sealed record ProductRow(
    Guid Id,
    string Name,
    string? Code,
    ProductType ProductType,
    string DefaultPrice,
    string? DefaultCost);

/// <summary>
/// Dedicated ViewModel for the Products (商品) page. Product data is sourced exclusively from the
/// Commerce <see cref="IProductService"/> (Req 6.4, 7.3); no legacy remote service is invoked.
/// </summary>
public sealed partial class ProductsPageViewModel : CommercePageViewModel
{
    private readonly IProductService _productService;
    private readonly bool _canViewCosts;

    /// <summary>Creates the Products page ViewModel over the product service.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="productService"/> is null.</exception>
    public ProductsPageViewModel(IProductService productService, bool canViewCosts = true)
    {
        _productService = productService ?? throw new ArgumentNullException(nameof(productService));
        _canViewCosts = canViewCosts;
    }

    /// <inheritdoc />
    public override string PageKey => MainViewModel.SectionProducts;

    public bool CanViewCosts => _canViewCosts;

    /// <summary>The active products displayed on the page.</summary>
    public ObservableCollection<ProductRow> Products { get; } = new();

    /// <inheritdoc />
    protected override bool HasNoData => Products.Count == 0;

    /// <inheritdoc />
    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Product> products = await _productService
            .GetAllAsync(cancellationToken)
            .ConfigureAwait(true);

        Products.Clear();
        foreach (Product product in products)
        {
            Products.Add(new ProductRow(
                product.Id,
                product.Name,
                product.Code,
                product.ProductType,
                product.DefaultPrice.ToString(),
                _canViewCosts ? product.DefaultCost.ToString() : null));
        }

        NotifyEmptyStateChanged();
    }
}
