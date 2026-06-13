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

    // Exposed so extracted section UserControls can surface the shared window-level
    // copy toast without owning the Popup. Behaviour is identical to the inline path.
    internal void ShowCopyToastMessage(string message) => ShowCopyToast(message);
}
