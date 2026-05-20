using System.ComponentModel;
using System.Windows;
using Orderly.App.ViewModels;
using Orderly.Core.Models;

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

    // ——— 快捷筛选 Chip（UI 层转发，不承载业务逻辑）———

    private void QuickFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string kindValue } ||
            !Enum.TryParse<QuickFilterKind>(kindValue, out var kind))
        {
            return;
        }

        SetQuickFilter(kind);
    }

    private void SetQuickFilter(QuickFilterKind kind)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var option = viewModel.QuickFilterOptions.FirstOrDefault(opt => opt.Kind == kind);
        if (option is not null)
        {
            viewModel.SelectedQuickFilter = option;
        }
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
}
