using System.Windows;

namespace Orderly.App.Views.Sections;

public partial class CashflowView : System.Windows.Controls.UserControl
{
    private Window? _hostWindow;

    public CashflowView()
    {
        InitializeComponent();
        Loaded += CashflowView_Loaded;
        Unloaded += CashflowView_Unloaded;
    }

    private void CashflowView_Loaded(object sender, RoutedEventArgs e)
    {
        AttachHostWindow(Window.GetWindow(this));
        UpdateCashflowTrendCardVisibility();
    }

    private void CashflowView_Unloaded(object sender, RoutedEventArgs e)
    {
        AttachHostWindow(null);
    }

    private void AttachHostWindow(Window? hostWindow)
    {
        if (ReferenceEquals(_hostWindow, hostWindow))
        {
            return;
        }

        if (_hostWindow is not null)
        {
            _hostWindow.SizeChanged -= HostWindow_SizeChanged;
        }

        _hostWindow = hostWindow;

        if (_hostWindow is not null)
        {
            _hostWindow.SizeChanged += HostWindow_SizeChanged;
        }
    }

    private void HostWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCashflowTrendCardVisibility();
    }

    private void UpdateCashflowTrendCardVisibility()
    {
        if (_hostWindow is null)
        {
            return;
        }

        bool shouldShow = _hostWindow.WindowState == WindowState.Maximized || _hostWindow.ActualWidth >= 1600;

        CashflowTrendCard.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
        CashflowFirstRow.Height = shouldShow ? new GridLength(0, GridUnitType.Auto) : new GridLength(2.2, GridUnitType.Star);

        if (shouldShow)
        {
            CashflowTrendRow.Height = new GridLength(1, GridUnitType.Star);
            CashflowBreakdownRow.Height = new GridLength(0, GridUnitType.Auto);
        }
        else
        {
            CashflowTrendRow.Height = new GridLength(1, GridUnitType.Star);
            CashflowBreakdownRow.Height = new GridLength(3, GridUnitType.Star);
        }
    }
}
