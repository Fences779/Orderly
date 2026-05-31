using System.Windows;
using Orderly.App.ViewModels;

namespace Orderly.App.Views.Sections;

/// <summary>
/// Code-behind for the shared exception-list resource dictionary. Hosts the
/// per-item <c>MouseDoubleClick</c> handler referenced by the
/// <c>ExceptionOrderCardListItemStyle</c> EventSetter. The view-model is resolved
/// from the containing ListBox's DataContext, preserving the original behaviour
/// that previously lived on MainWindow (whose DataContext was the MainViewModel).
/// </summary>
public partial class ExceptionListResources : System.Windows.ResourceDictionary
{
    public ExceptionListResources()
    {
        InitializeComponent();
    }

    private async void ExceptionOrderCard_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBoxItem item)
        {
            return;
        }

        var vm = SectionVisualHelpers.FindAncestor<System.Windows.Controls.ListBox>(item)?.DataContext as MainViewModel;
        if (vm is null)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source && SectionVisualHelpers.FindAncestor<System.Windows.Controls.Button>(source) is not null)
        {
            return;
        }

        if (item.DataContext is Orderly.Core.Models.StringNarrationOrderSummary summary)
        {
            await vm.OpenExceptionOrderDetailAsync(summary);
            e.Handled = true;
        }
    }
}
