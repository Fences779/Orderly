using System;
using System.Windows;
using Orderly.App.ViewModels;

namespace Orderly.App.Views;

public partial class LoginAccountManagementPanel : System.Windows.Controls.UserControl
{
    private LoginViewModel? _viewModel;

    public LoginAccountManagementPanel()
    {
        InitializeComponent();
    }

    public event EventHandler? BackRequested;
    public event EventHandler? CreateAccountRequested;
    public event EventHandler<string>? DeleteAccountRequested;

    public void Initialize(LoginViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    public void FocusPrimary()
    {
        BtnBackFromAccountManagement.Focus();
    }

    private void BtnBackFromAccountManagement_Click(object sender, RoutedEventArgs e)
    {
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void BtnCreateManagedAccount_Click(object sender, RoutedEventArgs e)
    {
        CreateAccountRequested?.Invoke(this, EventArgs.Empty);
    }

    private void BtnRequestDeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string accountId })
        {
            return;
        }

        DeleteAccountRequested?.Invoke(this, accountId);
    }
}
