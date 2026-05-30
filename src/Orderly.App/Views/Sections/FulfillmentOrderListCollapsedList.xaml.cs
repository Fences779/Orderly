using System.Windows;
using Orderly.App.ViewModels;

namespace Orderly.App.Views.Sections;

public partial class FulfillmentOrderListCollapsedList : System.Windows.Controls.UserControl
{
    public FulfillmentOrderListCollapsedList()
    {
        InitializeComponent();
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
