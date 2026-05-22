using System.ComponentModel;
using System.Linq;
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
            if (element.DataContext is Orderly.Core.Models.StringNarrationOrderSummary orderSummary)
            {
                vm.SelectedStringNarrationOrder = orderSummary;
            }

            var targetStatus = element.Tag as string;
            if (!string.IsNullOrEmpty(targetStatus))
            {
                vm.StringNarrationFulfillmentStatusInput = targetStatus;
                vm.UpdateStringNarrationFulfillmentCommand.Execute(null);
            }
        }
    }

    private void ContactCustomer_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.MessageBox.Show("正在拉起客户沟通渠道...", "联系客户", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ModifyInfo_Click(object sender, RoutedEventArgs e)
    {
        var textBox = this.FindName("Input_FulfillmentCarrier") as System.Windows.Controls.TextBox;
        if (textBox != null)
        {
            textBox.Focus();
            textBox.BringIntoView();
        }
    }

    private void CancelOrder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var result = System.Windows.MessageBox.Show("是否确认将该订单设为异常以便后台协调取消？", "取消订单确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                vm.StringNarrationFulfillmentStatusInput = "exception";
                vm.UpdateStringNarrationFulfillmentCommand.Execute(null);
            }
        }
    }

    private void CloseDetails_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.DismissStringNarrationDetailsForSession();
        }
    }

    private async void StringNarrationOrdersList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox listBox || DataContext is not MainViewModel vm)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source && FindAncestor<System.Windows.Controls.Button>(source) is not null)
        {
            return;
        }

        if (listBox.SelectedItem is Orderly.Core.Models.StringNarrationOrderSummary summary)
        {
            await vm.OpenStringNarrationOrderDetailAsync(summary);
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

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }

            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        return null;
    }
}
