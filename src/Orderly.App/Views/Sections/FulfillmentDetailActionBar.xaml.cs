using System.Windows;
using Orderly.App.ViewModels;

namespace Orderly.App.Views.Sections;

public partial class FulfillmentDetailActionBar : System.Windows.Controls.UserControl
{
    public FulfillmentDetailActionBar()
    {
        InitializeComponent();
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
}
