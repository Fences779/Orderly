using CommunityToolkit.Mvvm.Input;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    partial void OnSelectedWorkbenchTaskChanged(WorkbenchTaskListItem? value)
    {
        if (value is null || _isSynchronizingSelection)
        {
            return;
        }

        _ = OpenWorkbenchTaskAsync(value);
    }

    [RelayCommand(CanExecute = nameof(CanRefreshWorkbenchTasks))]
    private async Task RefreshWorkbenchTasksAsync()
    {
        var previousSelection = SelectedWorkbenchTask;
        var tasks = await _workbenchTaskService.GetTasksAsync();
        ReplaceCollection(WorkbenchTasks, tasks.Select(task => new WorkbenchTaskListItem(task)));
        SelectedWorkbenchTask = ResolveWorkbenchTaskSelection(previousSelection);
        StatusMessage = $"今日行动已刷新，共 {WorkbenchTasks.Count} 项";
        OnPropertyChanged(nameof(HasWorkbenchTasks));
    }

    [RelayCommand]
    private void SelectWorkbenchTask(WorkbenchTaskListItem? task)
    {
        if (task is not null)
        {
            SelectedWorkbenchTask = task;
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenWorkbenchTask))]
    private async Task OpenWorkbenchTaskAsync(WorkbenchTaskListItem? task)
    {
        var target = task ?? SelectedWorkbenchTask;
        if (target is null)
        {
            return;
        }

        _isSynchronizingSelection = true;
        try
        {
            SelectedSection = "工作台";

            if (target.OrderId is int orderId)
            {
                SelectOrderById(orderId);
            }

            if (target.CustomerId is int customerId)
            {
                SelectCustomerById(customerId);
            }
        }
        finally
        {
            _isSynchronizingSelection = false;
        }

        if (target.CustomerId is int selectedCustomerId)
        {
            var customer = SelectedCustomer?.Id == selectedCustomerId
                ? SelectedCustomer
                : Customers.FirstOrDefault(item => item.Id == selectedCustomerId) ?? _allCustomers.FirstOrDefault(item => item.Id == selectedCustomerId);
            if (customer is not null)
            {
                SelectedCustomer = customer;
                await ReloadSelectedCustomerDetailsAsync(customer);
                await SyncWorkbenchTaskSelectionAsync(target, customer.Id);
            }
        }

        StatusMessage = $"已定位今日行动：{target.Title}";
    }

    private bool CanRefreshWorkbenchTasks()
    {
        return !IsBusy;
    }

    private bool CanOpenWorkbenchTask()
    {
        return SelectedWorkbenchTask is not null && !IsBusy;
    }

    private WorkbenchTaskListItem? ResolveWorkbenchTaskSelection(
        WorkbenchTaskListItem? previousSelection,
        int? preferredCustomerId = null,
        int? preferredOrderId = null)
    {
        var previousId = previousSelection?.Id;
        var previousDedupeKey = previousSelection?.DedupeKey;
        var previousType = previousSelection?.Type;
        var customerId = preferredCustomerId ?? previousSelection?.CustomerId ?? SelectedCustomer?.Id;
        var orderId = preferredOrderId ?? previousSelection?.OrderId ?? SelectedOrder?.Id;

        return WorkbenchTasks.FirstOrDefault(item => !string.IsNullOrWhiteSpace(previousId) && item.Id == previousId)
            ?? WorkbenchTasks.FirstOrDefault(item => !string.IsNullOrWhiteSpace(previousDedupeKey) && item.DedupeKey == previousDedupeKey)
            ?? WorkbenchTasks.FirstOrDefault(item => item.CustomerId == customerId && item.OrderId == orderId && item.Type == previousType)
            ?? WorkbenchTasks.FirstOrDefault(item => item.CustomerId == customerId && item.OrderId == orderId)
            ?? WorkbenchTasks.FirstOrDefault(item => item.CustomerId == customerId)
            ?? WorkbenchTasks.FirstOrDefault();
    }

    private async Task SyncWorkbenchTaskSelectionAsync(WorkbenchTaskListItem target, int customerId)
    {
        if (target.AiSuggestionId is int aiSuggestionId)
        {
            SelectedAiSuggestion = AiSuggestions.FirstOrDefault(item => item.Id == aiSuggestionId) ?? SelectedAiSuggestion;
        }

        if (target.OcrResultId is not int ocrResultId || CurrentOcrResult?.Id == ocrResultId)
        {
            return;
        }

        var ocrResults = await _ocrService.ListByCustomerAsync(customerId);
        var matched = ocrResults.FirstOrDefault(item => item.Id == ocrResultId);
        if (matched is not null)
        {
            CurrentOcrResult = matched;
        }
    }
}
