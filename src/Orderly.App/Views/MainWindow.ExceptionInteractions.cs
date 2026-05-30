using System.Windows;
using Orderly.App.ViewModels;

namespace Orderly.App.Views;

public partial class MainWindow : Window
{
    private async void ExceptionOrderCard_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBoxItem item || DataContext is not MainViewModel vm)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source && FindAncestor<System.Windows.Controls.Button>(source) is not null)
        {
            return;
        }

        if (item.DataContext is Orderly.Core.Models.StringNarrationOrderSummary summary)
        {
            await vm.OpenExceptionOrderDetailAsync(summary);
            e.Handled = true;
        }
    }

    private void CloseExceptionDetails_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            if (ExceptionDetailPanel != null && ExceptionDetailPanel.RenderTransform is System.Windows.Media.TranslateTransform tt)
            {
                var opacityAnim = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 1.0,
                    To = 0.0,
                    Duration = new Duration(System.TimeSpan.FromSeconds(0.2)),
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
                };

                var translateAnim = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0.0,
                    To = 40.0,
                    Duration = new Duration(System.TimeSpan.FromSeconds(0.2)),
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
                };

                var sb = new System.Windows.Media.Animation.Storyboard();
                System.Windows.Media.Animation.Storyboard.SetTarget(opacityAnim, ExceptionDetailPanel);
                System.Windows.Media.Animation.Storyboard.SetTargetProperty(opacityAnim, new PropertyPath("Opacity"));

                System.Windows.Media.Animation.Storyboard.SetTarget(translateAnim, ExceptionDetailPanel);
                System.Windows.Media.Animation.Storyboard.SetTargetProperty(translateAnim, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.X)"));

                sb.Children.Add(opacityAnim);
                sb.Children.Add(translateAnim);

                sb.Completed += (s, ev) =>
                {
                    vm.DismissExceptionDetailsForSession();
                    ExceptionDetailPanel.BeginAnimation(UIElement.OpacityProperty, null);
                    tt.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
                };

                sb.Begin();
            }
            else
            {
                vm.DismissExceptionDetailsForSession();
            }
        }
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
