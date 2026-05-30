using System.Windows;
using Orderly.App.ViewModels;

namespace Orderly.App.Views;

public partial class MainWindow : Window
{
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
            if (DetailPanel != null && DetailPanel.RenderTransform is System.Windows.Media.TranslateTransform tt)
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
                System.Windows.Media.Animation.Storyboard.SetTarget(opacityAnim, DetailPanel);
                System.Windows.Media.Animation.Storyboard.SetTargetProperty(opacityAnim, new PropertyPath("Opacity"));

                System.Windows.Media.Animation.Storyboard.SetTarget(translateAnim, DetailPanel);
                System.Windows.Media.Animation.Storyboard.SetTargetProperty(translateAnim, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.X)"));

                sb.Children.Add(opacityAnim);
                sb.Children.Add(translateAnim);

                sb.Completed += (s, ev) =>
                {
                    vm.DismissStringNarrationDetailsForSession();
                    DetailPanel.BeginAnimation(UIElement.OpacityProperty, null);
                    tt.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
                };

                sb.Begin();
            }
            else
            {
                vm.DismissStringNarrationDetailsForSession();
            }
        }
    }

    private void StepperNode_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && DataContext is MainViewModel vm)
        {
            var targetStatus = element.Tag as string;
            if (!string.IsNullOrEmpty(targetStatus) && vm.SelectedStringNarrationOrderDetail is not null)
            {
                vm.StringNarrationFulfillmentStatusInput = targetStatus;
                vm.UpdateStringNarrationFulfillmentCommand.Execute(null);
            }
        }
    }

    private void FulfillmentInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            if (DataContext is MainViewModel vm && vm.UpdateStringNarrationFulfillmentCommand.CanExecute(null))
            {
                if (sender is System.Windows.Controls.TextBox textBox)
                {
                    var binding = textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty);
                    binding?.UpdateSource();
                }
                vm.UpdateStringNarrationFulfillmentCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
