using CommunityToolkit.Mvvm.Input;
using Orderly.App.ViewModels.Helpers;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    [RelayCommand(CanExecute = nameof(CanChangeDealStage))]
    private async Task ChangeDealStageAsync()
    {
        var customer = SelectedCustomer;
        if (customer is null)
        {
            ShowNoSelectionMessage();
            return;
        }

        try
        {
            await ExecuteSaveActionAsync(
                busyMessage: "正在推进成交阶段...",
                successMessage: "成交阶段已更新",
                errorTitle: "更新成交阶段失败",
                errorStatusPrefix: "更新成交阶段失败",
                action: async () =>
                {
                    var deal = await EnsureSelectedDealAsync(customer);
                    var nextStage = StatusLabelHelper.GetNextStage(deal.Stage);
                    await _dealService.UpdateStageAsync(deal.Id, nextStage);
                    await ReloadListDataAsync(selectedCustomerId: customer.Id, selectedOrderId: SelectedOrder?.Id);
                    await ReloadSelectedCustomerDetailsAsync(customer);
                    StatusMessage = $"成交阶段已更新为 {StatusLabelHelper.GetDealStageLabel(nextStage)}";
                });
        }
        catch (Exception ex)
        {
            StatusMessage = $"更新成交阶段失败：{ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanChangeDealStage))]
    private Task AdvanceDealStageAsync()
    {
        return ChangeDealStageAsync();
    }
}
