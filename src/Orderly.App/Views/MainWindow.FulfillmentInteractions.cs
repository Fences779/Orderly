using System.Windows;
using Orderly.App.ViewModels;

namespace Orderly.App.Views;

public partial class MainWindow : Window
{
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
}
