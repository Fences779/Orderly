using System.Windows;
using Orderly.App.ViewModels;

namespace Orderly.App.Views.Sections;

public partial class ExceptionDetailActions : System.Windows.Controls.UserControl
{
    public ExceptionDetailActions()
    {
        InitializeComponent();
    }

    private void ContactCustomer_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.MessageBox.Show("正在拉起客户沟通渠道...", "联系客户", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void JumpToOrderFulfillment_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.SelectedExceptionOrderDetail is not null)
        {
            var targetOrder = vm.SelectedExceptionOrderDetail;
            vm.SelectedSection = MainViewModel.SectionFulfillment;
            await vm.OpenStringNarrationOrderDetailAsync(targetOrder);
        }
    }
}
