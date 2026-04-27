using CommunityToolkit.Mvvm.Input;
using Orderly.App.Views;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    [RelayCommand(CanExecute = nameof(CanAddCustomer))]
    private async Task AddCustomerAsync()
    {
        try
        {
            StatusMessage = "正在新增客户...";
            var (dialog, result) = await ShowDialogAsync(() => new AddCustomerDialog());

            if (result != true)
            {
                StatusMessage = "已取消新增客户";
                return;
            }

            await ExecuteSaveActionAsync(
                busyMessage: "正在保存客户...",
                successMessage: "客户已保存",
                errorTitle: "新增客户失败",
                errorStatusPrefix: "保存客户失败",
                action: async () =>
                {
                    var created = await _customerService.SaveCustomerAsync(dialog.Customer);
                    await ReloadListDataAsync(selectedCustomerId: created.Id);
                    SelectCustomerById(created.Id);
                });
        }
        catch (Exception ex)
        {
            StatusMessage = $"新增客户失败：{ex.Message}";
            ShowErrorMessage("新增客户失败", ex);
        }
    }

    [RelayCommand(CanExecute = nameof(CanChangeCustomerStatus))]
    private async Task ChangeCustomerStatusAsync()
    {
        var customer = SelectedCustomer;
        if (customer is null)
        {
            ShowNoSelectionMessage();
            return;
        }

        var status = SelectedCustomerStatusInput;
        if (customer.Status == status)
        {
            StatusMessage = "客户状态未变化";
            return;
        }

        await ExecuteSaveActionAsync(
            busyMessage: "正在更新客户状态...",
            successMessage: "客户状态已更新",
            errorTitle: "更新客户状态失败",
            errorStatusPrefix: "更新客户状态失败",
            action: async () =>
            {
                await _customerService.UpdateStatusAsync(customer.Id, status);
                await ReloadListDataAsync(selectedCustomerId: customer.Id, selectedOrderId: SelectedOrder?.Id);
                SelectCustomerById(customer.Id);
                await ReloadSelectedCustomerDetailsAsync(SelectedCustomer);
            });
    }
}
