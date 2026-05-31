using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace Orderly.App.Views;

public partial class LoginView : Window
{
    private async void BtnOpenAccountManagement_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.EnterAccountManagementModeAsync();
        await WaitForSurfaceAsync(LoginSurface.AccountManagement);
        FocusPrimaryField();
    }

    private async void BtnBackFromAccountManagement_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ExitAccountManagementMode();
        ClearDeleteAccountInputs();
        await WaitForSurfaceAsync(LoginSurface.SignIn);
        FocusPrimaryField();
    }

    private void BtnRequestDeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string accountId })
        {
            return;
        }

        _viewModel.BeginDeleteAccount(accountId);
        TxtDeleteOwnerUsername.Focus();
    }

    private void BtnCancelDeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        ClosePendingDeleteDialog();
    }

    private async void BtnConfirmDeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.DeletePendingAccountAsync(
            TxtDeleteOwnerUsername.Text,
            TxtDeleteOwnerPassword.Password,
            TxtDeleteOwnerPin.Password);

        if (!_viewModel.HasPendingDeleteAccount)
        {
            ClearDeleteAccountInputs();
        }
    }

    private void DeleteAccountDialog_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void BtnCreateManagedAccount_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.EnterCreateManagedAccountMode();
        _ = WaitAndFocusCreateManagedAccountAsync();
    }

    private async Task WaitAndFocusCreateManagedAccountAsync()
    {
        await WaitForSurfaceAsync(LoginSurface.CreateMember);
        CreateManagedAccountPanel.FocusPrimary();
    }

    private async void OnCreateManagedAccountBackRequested(object? sender, EventArgs e)
    {
        await WaitForSurfaceAsync(LoginSurface.AccountManagement);
        FocusPrimaryField();
    }

    private void BtnAcknowledgeAccountManagementNotice_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AcknowledgeAccountManagementNotice();
        FocusPrimaryField();
    }

    private void ClearDeleteAccountInputs()
    {
        TxtDeleteOwnerUsername.Text = string.Empty;
        TxtDeleteOwnerPassword.Password = string.Empty;
        TxtDeleteOwnerPin.Password = string.Empty;
    }

    private void ClosePendingDeleteDialog()
    {
        _viewModel.CancelDeleteAccount();
        ClearDeleteAccountInputs();
    }
}
