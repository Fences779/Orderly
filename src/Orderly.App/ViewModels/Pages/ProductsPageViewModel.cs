using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Services;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

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
    private readonly IPriceChangeRequestService? _priceChangeRequestService;
    private readonly bool _canViewCosts;

    [ObservableProperty]
    private ProductRow? _selectedProduct;

    /// <summary>Creates the Products page ViewModel over the product service.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="productService"/> is null.</exception>
    public ProductsPageViewModel(
        IProductService productService,
        bool canViewCosts = true,
        IPriceChangeRequestService? priceChangeRequestService = null)
    {
        _productService = productService ?? throw new ArgumentNullException(nameof(productService));
        _canViewCosts = canViewCosts;
        _priceChangeRequestService = priceChangeRequestService;
    }

    /// <inheritdoc />
    public override string PageKey => MainViewModel.SectionProducts;

    public bool CanViewCosts => _canViewCosts;

    /// <summary>Whether the current user can submit price-change requests (Employee only; Admin edits directly).</summary>
    public bool CanSubmitPriceChangeRequest => !_canViewCosts && _priceChangeRequestService is not null;

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

    [RelayCommand(CanExecute = nameof(CanSubmitPriceChangeRequest))]
    private async Task SubmitPriceChangeRequestAsync(ProductRow? product)
    {
        var target = product ?? SelectedProduct;
        if (target is null || _priceChangeRequestService is null)
            return;

        if (!decimal.TryParse(target.DefaultPrice, out var currentPrice))
            currentPrice = 0m;

        var dialog = new Views.SubmitPriceChangeRequestDialog(currentPrice)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            await _priceChangeRequestService.SubmitAsync(target.Id, dialog.ProposedPrice, dialog.Reason);
            System.Windows.MessageBox.Show("改价申请已提交，等待管理员审批。", "提交改价申请", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"提交改价申请失败：{ex.Message}", "提交改价申请", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
