using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Orderly.App.Views;

public partial class LoginView : Window
{
    private void RootGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (IsInteractiveClickSource(e.OriginalSource))
        {
            return;
        }

        this.DragMove();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !_viewModel.HasPendingDeleteAccount)
        {
            return;
        }

        ClosePendingDeleteDialog();
        e.Handled = true;
    }

    private void MainCardContent_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (MainCardContent.ActualWidth <= 0 || MainCardContent.ActualHeight <= 0)
        {
            MainCardContent.Clip = null;
            return;
        }

        MainCardContent.Clip = new RectangleGeometry(
            new Rect(0, 0, MainCardContent.ActualWidth, MainCardContent.ActualHeight),
            24,
            24);
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Application.Current.Shutdown();
    }

    private static bool IsInteractiveClickSource(object? source)
    {
        for (var current = source as DependencyObject; current is not null; current = GetParent(current))
        {
            if (current is System.Windows.Controls.Primitives.TextBoxBase
                || current is System.Windows.Controls.PasswordBox
                || current is System.Windows.Controls.Primitives.ButtonBase
                || current is System.Windows.Controls.Primitives.Selector
                || current is System.Windows.Controls.Primitives.ScrollBar)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDescendantOf(DependencyObject? source, DependencyObject ancestor)
    {
        for (var current = source; current is not null; current = GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static DependencyObject? GetParent(DependencyObject current)
    {
        return current switch
        {
            Visual visual => VisualTreeHelper.GetParent(visual),
            FrameworkContentElement contentElement => contentElement.Parent,
            _ => LogicalTreeHelper.GetParent(current)
        };
    }
}
