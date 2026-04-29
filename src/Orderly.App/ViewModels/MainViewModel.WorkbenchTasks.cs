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
        var tasks = await _workbenchTaskService.GetTasksAsync();
        ReplaceCollection(WorkbenchTasks, tasks.Select(task => new WorkbenchTaskListItem(task)));
        SelectedWorkbenchTask ??= WorkbenchTasks.FirstOrDefault();
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
}
