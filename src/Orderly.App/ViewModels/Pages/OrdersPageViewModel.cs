using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Core.Commerce.Services;

namespace Orderly.App.ViewModels.Pages;

/// <summary>One row of the Orders page, projected from a Universal_Domain_Model <see cref="Order"/>.</summary>
public sealed record OrderRow(
    Guid Id,
    string? OrderNo,
    OrderSalesStage SalesStage,
    OrderPaymentStage PaymentStage,
    OrderFulfillmentStage FulfillmentStage,
    string Total,
    string PaidAmount,
    string ReceivableAmount,
    DateTime OrderedAt);

/// <summary>
/// Dedicated ViewModel for the Orders (订单) page. Order operations route through
/// <see cref="IOrderService"/> and order rows are read through the Commerce
/// <see cref="ICommerceOrderRepository"/> (Req 6.3, 7.3); no legacy remote service is invoked.
/// </summary>
public sealed partial class OrdersPageViewModel : CommercePageViewModel
{
    private readonly ICommerceOrderRepository _orderRepository;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ArchiveOrderCommand))]
    private OrderRow? _selectedOrder;

    /// <summary>Creates the Orders page ViewModel over the order service and the Commerce order repository.</summary>
    /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
    public OrdersPageViewModel(IOrderService orderService, ICommerceOrderRepository orderRepository)
    {
        OrderService = orderService ?? throw new ArgumentNullException(nameof(orderService));
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
    }

    /// <inheritdoc />
    public override string PageKey => MainViewModel.SectionOrders;

    /// <summary>
    /// The Commerce order service used for order recalculation, stage transitions, and completion.
    /// Exposed so task 19.3 can wire order commands without reaching past the Commerce Service Layer.
    /// </summary>
    public IOrderService OrderService { get; }

    /// <summary>The active orders displayed on the page.</summary>
    public ObservableCollection<OrderRow> Orders { get; } = new();

    /// <inheritdoc />
    protected override bool HasNoData => Orders.Count == 0;

    /// <inheritdoc />
    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Order> orders = await _orderRepository
            .GetAllAsync(cancellationToken)
            .ConfigureAwait(true);

        Orders.Clear();
        foreach (Order order in orders)
        {
            Orders.Add(new OrderRow(
                order.Id,
                order.OrderNo,
                order.SalesStage,
                order.PaymentStage,
                order.FulfillmentStage,
                order.Total.ToString(),
                order.PaidAmount.ToString(),
                order.ReceivableAmount.ToString(),
                order.OrderedAt));
        }

        NotifyEmptyStateChanged();
    }

    private bool CanArchiveOrder(OrderRow? row) => row is not null;

    [RelayCommand(CanExecute = nameof(CanArchiveOrder))]
    private async Task ArchiveOrderAsync(OrderRow? row)
    {
        var target = row ?? SelectedOrder;
        if (target is null)
            return;

        var dialog = new Views.ArchiveReasonDialog($"订单 {target.OrderNo}")
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            await _orderRepository.DeleteAsync(target.Id, dialog.Reason).ConfigureAwait(true);
            Orders.Remove(target);
            NotifyEmptyStateChanged();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"归档失败：{ex.Message}", "归档", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }
}
