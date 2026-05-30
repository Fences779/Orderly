using System.ComponentModel;
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
        this.SizeChanged += MainWindow_SizeChanged;
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

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.StringNarrationStatusMessage))
        {
            if (DataContext is MainViewModel vm && !string.IsNullOrEmpty(vm.StringNarrationStatusMessage) && vm.StringNarrationStatusMessage.StartsWith("已复制"))
            {
                ShowCopyToast(vm.StringNarrationStatusMessage);
            }
        }
        else if (e.PropertyName == "CashflowHealthDashboardResult" || (e.PropertyName == nameof(MainViewModel.SelectedSection) && DataContext is MainViewModel vm && string.Equals(vm.SelectedSection, "现金流")))
        {
            Dispatcher.InvokeAsync(() =>
            {
                UpdateCashflowTrendChart();
                UpdateDonutCharts();
            });
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
