using System.ComponentModel;
using System.Windows;
using Orderly.App.ViewModels;
namespace Orderly.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        System.Windows.Application.Current.MainWindow = this;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (System.Windows.Application.Current is App { IsExiting: false, IsSwitchingSession: false })
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private void Btn_SelectDateRange_Click(object sender, RoutedEventArgs e)
    {
        Popup_DateRangePicker.IsOpen = true;
    }

    private void Btn_ClearDateRange_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.StartAt = 0;
            vm.EndAt = 0;
            vm.LoadOrders.Execute(null);
        }
        Popup_DateRangePicker.IsOpen = false;
    }

    private void Btn_ApplyDateRange_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.LoadOrders.Execute(null);
        }
        Popup_DateRangePicker.IsOpen = false;
    }

    private void QuickFulfillmentUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && DataContext is MainViewModel vm)
        {
            var targetStatus = element.Tag as string;
            if (!string.IsNullOrEmpty(targetStatus))
            {
                vm.StringNarrationFulfillmentStatusInput = targetStatus;
                vm.UpdateStringNarrationFulfillmentCommand.Execute(null);
            }
        }
    }

    private void CloseDetails_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.SelectedStringNarrationOrder = null;
            vm.SelectedStringNarrationOrderDetail = null;
        }
    }

    private void SettingsTextInput_LostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not System.Windows.Controls.TextBox textBox)
        {
            return;
        }

        var binding = textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty);
        binding?.UpdateSource();

        if (System.Windows.Controls.Validation.GetHasError(textBox))
        {
            var validationError = System.Windows.Controls.Validation.GetErrors(textBox).FirstOrDefault()?.ErrorContent?.ToString();
            vm.ReportDeferredSettingsAutoSaveValidationError(
                string.IsNullOrWhiteSpace(validationError)
                    ? "当前输入无效，未自动保存。"
                    : $"当前输入无效，未自动保存：{validationError}");
            return;
        }

        vm.CommitDeferredSettingsAutoSave();
    }
}
