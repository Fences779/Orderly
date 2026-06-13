using CommunityToolkit.Mvvm.Input;
using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    partial void OnSelectedOrderItemChanged(OrderListItem? value)
    {
        OnPropertyChanged(nameof(SelectedOrder));
        OnPropertyChanged(nameof(SelectedStatusLabel));
        OnPropertyChanged(nameof(HasSelectedOrder));
        OnPropertyChanged(nameof(OrderDetailsEmptyMessage));
        SelectedOrderStatusInput = value?.Order.Status ?? OrderStatus.PendingCommunication;

        if (_isSynchronizingSelection)
        {
            return;
        }

        _ = SelectCustomerForOrderAsync(value?.Order);
    }

    partial void OnSelectedCustomerChanged(Customer? value)
    {
        StatusMessage = value is null ? "未选择客户" : $"已选择客户：{value.Name}";
        SelectedCustomerStatusInput = value?.Status ?? CustomerStatus.Active;

        if (!_isSynchronizingSelection)
        {
            SyncSelectedOrderForCustomer(value);
        }

        _ = LoadSelectedCustomerDetailsAsync(value);
    }

    partial void OnSelectedDealChanged(Deal? value)
    {
        OnPropertyChanged(nameof(CurrentDealStage));
    }

    [RelayCommand]
    private async Task SelectSectionAsync(string section)
    {
        if (!string.IsNullOrWhiteSpace(section))
        {
            var normalized = NormalizeSection(section);
            SelectedSection = normalized;
            await EnsureCommercePageLoadedAsync(normalized);
        }
    }

    [RelayCommand]
    private void SelectOrder(OrderListItem? order)
    {
        if (order is not null)
        {
            SelectedOrderItem = order;
        }
    }

    private async Task SelectCustomerForOrderAsync(MerchantOrder? order)
    {
        try
        {
            if (order is null)
            {
                SelectedCustomer = null;
                return;
            }

            Customer? customer = null;
            if (order.CustomerId > 0)
            {
                customer = Customers.FirstOrDefault(item => item.Id == order.CustomerId)
                    ?? order.Customer
                    ?? await _customerRepository.GetByIdAsync(order.CustomerId);
            }
            else if (order.Customer is not null)
            {
                customer = order.Customer;
            }

            if (SelectedOrder?.Id != order.Id)
            {
                return;
            }

            if (customer is null)
            {
                SelectedCustomer = null;
                StatusMessage = "未选择客户";
                return;
            }

            if (SelectedCustomer?.Id == customer.Id)
            {
                SelectedCustomer = customer;
                await ReloadSelectedCustomerDetailsAsync(customer);
                return;
            }

            SelectedCustomer = customer;
        }
        catch (Exception ex)
        {
            SelectedCustomer = null;
            StatusMessage = $"选择客户失败：{ex.Message}";
        }
    }

    private void SyncSelectedOrderForCustomer(Customer? customer)
    {
        if (customer is null)
        {
            return;
        }

        var matchingOrder = Orders.FirstOrDefault(item => item.Order.CustomerId == customer.Id);
        if (SelectedOrderItem?.Order.CustomerId == customer.Id && matchingOrder is not null)
        {
            return;
        }

        _isSynchronizingSelection = true;
        try
        {
            SelectedOrderItem = matchingOrder;
        }
        finally
        {
            _isSynchronizingSelection = false;
        }
    }

    private async Task<Deal> EnsureSelectedDealAsync(Customer customer)
    {
        if (SelectedDeal is not null)
        {
            return SelectedDeal;
        }

        var order = SelectedOrder;
        var deal = new Deal
        {
            CustomerId = customer.Id,
            Title = order?.Title ?? $"{customer.Name} 成交机会",
            Stage = DealStage.New,
            EstimatedAmount = order?.Amount ?? 0,
            Requirement = order?.Requirement ?? string.Empty,
            SourcePlatform = order?.SourcePlatform ?? customer.SourcePlatform,
            Channel = order?.Channel ?? customer.Channel
        };

        return await _dealService.SaveDealAsync(deal);
    }

    private void SelectCustomerById(int customerId)
    {
        SelectedCustomer = Customers.FirstOrDefault(customer => customer.Id == customerId)
            ?? _allCustomers.FirstOrDefault(customer => customer.Id == customerId);
    }

    private void SelectOrderById(int orderId)
    {
        SelectedOrderItem = Orders.FirstOrDefault(order => order.Id == orderId)
            ?? _allOrders.FirstOrDefault(order => order.Id == orderId);
    }
}
