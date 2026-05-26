using System.ComponentModel;
using System.Linq;
using System.Windows;
using Orderly.App.ViewModels;

namespace Orderly.App.Views;

public partial class MainWindow : Window
{
    private System.Threading.CancellationTokenSource? _copyToastCts;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        System.Windows.Application.Current.MainWindow = this;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
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

    private async void ExceptionOrdersList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
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
            await vm.OpenExceptionOrderDetailAsync(summary);
        }
    }

    private async void ExceptionOrdersList_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox || DataContext is not MainViewModel vm)
        {
            return;
        }

        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if (FindAncestor<System.Windows.Controls.Button>(source) is not null)
        {
            return;
        }

        var listBoxItem = FindAncestor<System.Windows.Controls.ListBoxItem>(source);
        if (listBoxItem?.DataContext is Orderly.Core.Models.StringNarrationOrderSummary summary)
        {
            await vm.OpenExceptionOrderDetailAsync(summary);
        }
    }

    private async void ExceptionOrderCard_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not FrameworkElement element)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source && FindAncestor<System.Windows.Controls.Button>(source) is not null)
        {
            return;
        }

        if (element.DataContext is Orderly.Core.Models.StringNarrationOrderSummary summary)
        {
            await vm.OpenExceptionOrderDetailAsync(summary);
            e.Handled = true;
        }
    }

    private void SettingsTextInput_LostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not System.Windows.Controls.TextBox textBox)
        {
            return;
        }

        var binding = textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty);
        binding?.UpdateSource();

        if (System.Windows.Controls.Validation.GetHasError(textBox))
        {
            var validationError = System.Windows.Controls.Validation.GetErrors(textBox).FirstOrDefault()?.ErrorContent?.ToString();
            vm.ReportDeferredSettingsAutoSaveValidationError(
                string.IsNullOrWhiteSpace(validationError)
                    ? "当前输入无效，未自动保存。"
                    : $"当前输入无效，未自动保存：{validationError}");
            return;
        }

        vm.CommitDeferredSettingsAutoSave();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.StringNarrationStatusMessage))
        {
            if (DataContext is MainViewModel vm && !string.IsNullOrEmpty(vm.StringNarrationStatusMessage) && vm.StringNarrationStatusMessage.StartsWith("已复制"))
            {
                ShowCopyToast(vm.StringNarrationStatusMessage);
            }
        }
    }

    private void ShowCopyToast(string message)
    {
        _copyToastCts?.Cancel();
        _copyToastCts = new System.Threading.CancellationTokenSource();
        var token = _copyToastCts.Token;

        Text_CopyToast.Text = message;
        Popup_CopyToast.IsOpen = true;

        System.Threading.Tasks.Task.Delay(1500, token).ContinueWith(t =>
        {
            if (!token.IsCancellationRequested)
            {
                Dispatcher.Invoke(() =>
                {
                    Popup_CopyToast.IsOpen = false;
                });
            }
        }, token);
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

    private void CopyText_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string text && !string.IsNullOrEmpty(text))
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
                ShowCopyToast("已复制");
            }
            catch (System.Exception)
            {
                // ignore
            }
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
