using System.Windows;
using Orderly.App.ViewModels;

namespace Orderly.App.Views;

public partial class MainWindow : Window
{
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
}
