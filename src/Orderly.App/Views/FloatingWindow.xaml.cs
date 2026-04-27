using System.ComponentModel;
using System.Windows;
using Orderly.App.ViewModels;

namespace Orderly.App.Views;

public partial class FloatingWindow : Window
{
    public FloatingWindow(FloatingWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (System.Windows.Application.Current is App { IsExiting: false })
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }
}
