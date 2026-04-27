using CommunityToolkit.Mvvm.Input;
using Orderly.App.Views;
using Orderly.App.ViewModels.Helpers;
using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private void ClearSearchAndFilters()
    {
        SearchKeyword = string.Empty;
        SelectedStatusFilter = SearchFilterOption.All;
        SelectedQuickFilter = QuickFilterOption.All;
        ApplyFilters();
        StatusMessage = "已清空搜索和筛选";
    }

    [RelayCommand(CanExecute = nameof(CanAddFollowUp))]
    private async Task AddFollowUpAsync()
    {
        var customer = SelectedCustomer;
        if (customer is null)
        {
            ShowNoSelectionMessage();
            return;
        }

        try
        {
            StatusMessage = "正在新增跟进...";
            var (dialog, result) = await ShowDialogAsync(() => new AddFollowUpDialog());

            if (result != true)
            {
                StatusMessage = "已取消新增跟进";
                return;
            }

            await ExecuteSaveActionAsync(
                busyMessage: "正在保存跟进...",
                successMessage: "跟进已保存",
                errorTitle: "新增跟进失败",
                errorStatusPrefix: "保存跟进失败",
                action: async () =>
                {
                    await _followUpService.SaveFollowUpAsync(new FollowUp
                    {
                        CustomerId = customer.Id,
                        DealId = SelectedDeal?.Id,
                        OrderId = SelectedOrder?.Id,
                        Title = dialog.FollowUpTitle,
                        Content = dialog.FollowUpContent,
                        Status = FollowUpStatus.Pending,
                        ScheduledAt = dialog.ScheduledAt
                    });

                    await ReloadSelectedCustomerDetailsAsync(customer);
                });
        }
        catch (Exception ex)
        {
            StatusMessage = $"新增跟进失败：{ex.Message}";
            ShowErrorMessage("新增跟进失败", ex);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCompleteFollowUp))]
    private async Task CompleteFollowUpAsync(FollowUp? followUp)
    {
        if (followUp is null || SelectedCustomer is null)
        {
            return;
        }

        await ExecuteSaveActionAsync(
            busyMessage: "正在完成跟进...",
            successMessage: "跟进已完成",
            errorTitle: "完成跟进失败",
            errorStatusPrefix: "完成跟进失败",
            action: async () =>
            {
                await _followUpService.CompleteFollowUpAsync(followUp.Id, DateTime.Now);
                await ReloadListDataAsync(selectedCustomerId: followUp.CustomerId, selectedOrderId: SelectedOrder?.Id);
                await ReloadSelectedCustomerDetailsAsync(SelectedCustomer);
            });
    }

    [RelayCommand(CanExecute = nameof(CanSnoozeFollowUp))]
    private async Task SnoozeFollowUpAsync(FollowUp? followUp)
    {
        if (followUp is null || SelectedCustomer is null)
        {
            return;
        }

        var (dialog, result) = await ShowDialogAsync(() => new SnoozeFollowUpDialog(followUp.ScheduledAt));
        if (result != true)
        {
            StatusMessage = "已取消延期跟进";
            return;
        }

        await ExecuteSaveActionAsync(
            busyMessage: "正在延期跟进...",
            successMessage: "跟进已延期",
            errorTitle: "延期跟进失败",
            errorStatusPrefix: "延期跟进失败",
            action: async () =>
            {
                await _followUpService.SnoozeFollowUpAsync(followUp.Id, dialog.ScheduledAt);
                await ReloadListDataAsync(selectedCustomerId: followUp.CustomerId, selectedOrderId: SelectedOrder?.Id);
                await ReloadSelectedCustomerDetailsAsync(SelectedCustomer);
            });
    }

    [RelayCommand(CanExecute = nameof(CanCancelFollowUp))]
    private async Task CancelFollowUpAsync(FollowUp? followUp)
    {
        if (followUp is null || SelectedCustomer is null)
        {
            return;
        }

        await ExecuteSaveActionAsync(
            busyMessage: "正在取消跟进...",
            successMessage: "跟进已取消",
            errorTitle: "取消跟进失败",
            errorStatusPrefix: "取消跟进失败",
            action: async () =>
            {
                await _followUpService.CancelFollowUpAsync(followUp.Id);
                await ReloadListDataAsync(selectedCustomerId: followUp.CustomerId, selectedOrderId: SelectedOrder?.Id);
                await ReloadSelectedCustomerDetailsAsync(SelectedCustomer);
            });
    }

    private bool CanAddFollowUp()
    {
        return SelectedCustomer is not null && !IsBusy;
    }

    private bool CanCompleteFollowUp(FollowUp? followUp)
    {
        return followUp is not null && FollowUpDateHelper.CanTransitionFollowUp(followUp.Status) && !IsBusy;
    }

    private bool CanSnoozeFollowUp(FollowUp? followUp)
    {
        return followUp is not null && FollowUpDateHelper.CanTransitionFollowUp(followUp.Status) && !IsBusy;
    }

    private bool CanCancelFollowUp(FollowUp? followUp)
    {
        return followUp is not null && FollowUpDateHelper.CanTransitionFollowUp(followUp.Status) && !IsBusy;
    }
}
