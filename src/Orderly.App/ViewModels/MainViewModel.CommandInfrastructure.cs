using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using Orderly.App.Views;
using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    private static Window? GetDialogOwner()
    {
        var mainWindow = System.Windows.Application.Current.MainWindow;
        if (mainWindow is MainWindow && mainWindow.IsVisible)
        {
            return mainWindow;
        }

        return System.Windows.Application.Current.Windows
            .OfType<MainWindow>()
            .FirstOrDefault(window => window.IsVisible);
    }

    private static async Task<(TDialog Dialog, bool? Result)> ShowDialogAsync<TDialog>(Func<TDialog> dialogFactory)
        where TDialog : Window
    {
        var dispatcher = System.Windows.Application.Current.MainWindow?.Dispatcher
            ?? System.Windows.Application.Current.Dispatcher;

        (TDialog Dialog, bool? Result) ShowOnDispatcher()
        {
            var dialog = dialogFactory();
            dialog.Owner = GetDialogOwner();
            return (dialog, dialog.ShowDialog());
        }

        return dispatcher.CheckAccess()
            ? ShowOnDispatcher()
            : await dispatcher.InvokeAsync(ShowOnDispatcher);
    }

    private async Task ExecuteSaveActionAsync(
        string busyMessage,
        string successMessage,
        string errorTitle,
        string errorStatusPrefix,
        Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsSaving = true;
            StatusMessage = busyMessage;
            await action();
            StatusMessage = successMessage;
        }
        catch (Exception ex)
        {
            StatusMessage = $"{errorStatusPrefix}：{ex.Message}";
            ShowErrorMessage(errorTitle, ex);
        }
        finally
        {
            IsSaving = false;
        }
    }

    private bool CanRefresh()
    {
        return !IsBusy;
    }

    private bool CanAddCustomer()
    {
        return !IsBusy;
    }

    private bool CanAddOrder()
    {
        return _allCustomers.Count > 0 && !IsBusy;
    }

    private bool CanAddNote()
    {
        return SelectedCustomer is not null && !IsBusy;
    }

    private bool CanAddPriceAdjustment()
    {
        return SelectedCustomer is not null && SelectedOrder is not null && !IsBusy;
    }

    private bool CanAddConversationMessage()
    {
        return SelectedCustomer is not null
            && !IsBusy
            && !string.IsNullOrWhiteSpace(ConversationMessageInput);
    }

    private bool CanChangeDealStage()
    {
        return SelectedCustomer is not null && !IsBusy;
    }

    private bool CanChangeCustomerStatus()
    {
        return SelectedCustomer is not null && !IsBusy;
    }

    private bool CanChangeOrderStatus()
    {
        return SelectedOrder is not null && !IsBusy;
    }

    private static string CreateNoteActivityMetadataJson(ReplyTemplate? insertedTemplate)
    {
        return insertedTemplate is null
            ? string.Empty
            : JsonSerializer.Serialize(new
            {
                templateId = insertedTemplate.Id,
                templateTitle = insertedTemplate.Title,
                templateScene = insertedTemplate.Scene
            });
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private void ShowNoSelectionMessage()
    {
        const string message = "请先选择一个客户或订单";
        StatusMessage = message;
        System.Windows.MessageBox.Show(GetDialogOwner(), message, "Orderly", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShowErrorMessage(string title, Exception ex)
    {
        System.Windows.MessageBox.Show(GetDialogOwner(), ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
