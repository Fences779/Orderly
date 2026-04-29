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
        var tasks = await _workbenchTaskService.GetTasksAsync(WorkbenchTaskFilter);
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

        var route = await _navigationRouteService.ResolveAsync(target.Task);
        await ApplyNavigationRouteAsync(route, target.Title, "工作台");
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
}
