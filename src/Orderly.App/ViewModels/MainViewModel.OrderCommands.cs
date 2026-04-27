using CommunityToolkit.Mvvm.Input;
using System.Windows;
using Orderly.App.Views;
using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    [RelayCommand(CanExecute = nameof(CanAddOrder))]
    private async Task AddOrderAsync()
    {
        if (_allCustomers.Count == 0)
        {
            System.Windows.MessageBox.Show(GetDialogOwner(), "请先新增客户。", "创建订单", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            StatusMessage = "正在创建订单...";
            var (dialog, result) = await ShowDialogAsync(() => new AddOrderDialog(_allCustomers, SelectedCustomer));

            if (result != true || dialog.SelectedCustomer is null)
            {
                StatusMessage = "已取消创建订单";
                return;
            }

            await ExecuteSaveActionAsync(
                busyMessage: "正在保存订单...",
                successMessage: "订单已创建",
                errorTitle: "创建订单失败",
                errorStatusPrefix: "保存订单失败",
                action: async () =>
                {
                    var customer = dialog.SelectedCustomer;
                    var created = await _orderService.SaveOrderAsync(new MerchantOrder
                    {
                        CustomerId = customer.Id,
                        Title = dialog.OrderTitle,
                        Requirement = dialog.Requirement,
                        Amount = dialog.Amount,
                        Status = dialog.Status,
                        NextFollowUpAt = dialog.NextFollowUpAt,
                        SourcePlatform = customer.SourcePlatform,
                        Channel = customer.Channel
                    });

                    if (!string.IsNullOrWhiteSpace(dialog.Remark))
                    {
                        await _noteService.SaveNoteAsync(new CustomerNote
                        {
                            CustomerId = customer.Id,
                            OrderId = created.Id,
                            Type = NoteType.General,
                            Content = dialog.Remark
                        });
                    }

                    await ReloadListDataAsync(selectedCustomerId: customer.Id, selectedOrderId: created.Id);
                    SelectOrderById(created.Id);
                });
        }
        catch (Exception ex)
        {
            StatusMessage = $"创建订单失败：{ex.Message}";
            ShowErrorMessage("创建订单失败", ex);
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddPriceAdjustment))]
    private async Task AddPriceAdjustmentAsync()
    {
        var customer = SelectedCustomer;
        var order = SelectedOrder;
        if (customer is null || order is null)
        {
            ShowNoSelectionMessage();
            return;
        }

        try
        {
            StatusMessage = "正在新增改价...";
            var (dialog, result) = await ShowDialogAsync(() => new AddPriceAdjustmentDialog(order.Amount));

            if (result != true)
            {
                StatusMessage = "已取消新增改价";
                return;
            }

            await ExecuteSaveActionAsync(
                busyMessage: "正在保存改价...",
                successMessage: "改价记录已保存",
                errorTitle: "新增改价失败",
                errorStatusPrefix: "保存改价失败",
                action: async () =>
                {
                    await _priceAdjustmentService.SaveAdjustmentAsync(new PriceAdjustment
                    {
                        CustomerId = customer.Id,
                        DealId = SelectedDeal?.Id,
                        OrderId = order.Id,
                        OriginalAmount = dialog.OriginalAmount,
                        AdjustedAmount = dialog.AdjustedAmount,
                        Reason = dialog.Reason,
                        Status = dialog.Status
                    });

                    await ReloadSelectedCustomerDetailsAsync(customer);
                });
        }
        catch (Exception ex)
        {
            StatusMessage = $"新增改价失败：{ex.Message}";
            ShowErrorMessage("新增改价失败", ex);
        }
    }

    [RelayCommand(CanExecute = nameof(CanChangeOrderStatus))]
    private async Task ChangeOrderStatusAsync()
    {
        var order = SelectedOrder;
        if (order is null)
        {
            ShowNoSelectionMessage();
            return;
        }

        var status = SelectedOrderStatusInput;
        if (order.Status == status)
        {
            StatusMessage = "订单状态未变化";
            return;
        }

        await ExecuteSaveActionAsync(
            busyMessage: "正在更新订单状态...",
            successMessage: "订单状态已更新",
            errorTitle: "更新订单状态失败",
            errorStatusPrefix: "更新订单状态失败",
            action: async () =>
            {
                await _orderService.UpdateStatusAsync(order.Id, status);
                await ReloadListDataAsync(selectedCustomerId: order.CustomerId, selectedOrderId: order.Id);
                SelectOrderById(order.Id);
                await ReloadSelectedCustomerDetailsAsync(SelectedCustomer);
            });
    }
}
