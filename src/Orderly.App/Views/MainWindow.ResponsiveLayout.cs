using System.Windows;
using System.Windows.Controls;

namespace Orderly.App.Views;

public partial class MainWindow : Window
{
    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCashflowTrendCardVisibility();
    }

    private void UpdateCashflowTrendCardVisibility()
    {
        bool shouldShow = this.WindowState == WindowState.Maximized || this.ActualWidth >= 1600;

        if (CashflowTrendCard != null)
        {
            CashflowTrendCard.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
        }

        if (CashflowFirstRow != null)
        {
            CashflowFirstRow.Height = shouldShow ? new GridLength(0, GridUnitType.Auto) : new GridLength(2.2, GridUnitType.Star);
        }

        if (shouldShow)
        {
            if (CashflowTrendRow != null)
                CashflowTrendRow.Height = new GridLength(1, GridUnitType.Star);
            if (CashflowBreakdownRow != null)
                CashflowBreakdownRow.Height = new GridLength(0, GridUnitType.Auto);
        }
        else
        {
            if (CashflowTrendRow != null)
                CashflowTrendRow.Height = new GridLength(1, GridUnitType.Star);
            if (CashflowBreakdownRow != null)
                CashflowBreakdownRow.Height = new GridLength(3, GridUnitType.Star);
        }
    }
}
