using System;
using System.Windows;
using Orderly.App.ViewModels;

namespace Orderly.App.Views;

public partial class LoginCreateAccountPanel : System.Windows.Controls.UserControl
{
    private LoginViewModel? _viewModel;

    public LoginCreateAccountPanel()
    {
        InitializeComponent();
    }

    // Raised when the user requests to leave this surface (back button, or after a
    // successful creation). The parent owns the surface transition and re-focus, exactly
    // as before; this control only signals intent.
    public event EventHandler? BackRequested;

    public void Initialize(LoginViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    public void FocusPrimary()
    {
        TxtCreateOwnerUsername.Focus();
    }

    public void FocusMemberUsername()
    {
        TxtCreateMemberUsername.Focus();
    }

    public void ClearInputs()
    {
        TxtCreateOwnerUsername.Text = string.Empty;
        TxtCreateOwnerPassword.Password = string.Empty;
        TxtCreateOwnerPin.Password = string.Empty;
        TxtCreateMemberUsername.Text = string.Empty;
        TxtCreateMemberDisplayName.Text = string.Empty;
        TxtCreateMemberPassword.Password = string.Empty;
        TxtCreateMemberPin.Password = string.Empty;
    }

    private void BtnBackFromCreateManagedAccount_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.ExitCreateManagedAccountMode();
        ClearInputs();
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void BtnSubmitCreateManagedAccount_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (!_viewModel.IsCreateManagedAccountOwnerVerified)
        {
            await _viewModel.VerifyCreateManagedAccountOwnerAsync(
                TxtCreateOwnerUsername.Text,
                TxtCreateOwnerPassword.Password,
                TxtCreateOwnerPin.Password);

            if (_viewModel.IsCreateManagedAccountOwnerVerified)
            {
                TxtCreateMemberUsername.Focus();
            }

            return;
        }

        await _viewModel.CreateManagedAccountAsync(
            TxtCreateOwnerUsername.Text,
            TxtCreateOwnerPassword.Password,
            TxtCreateOwnerPin.Password,
            TxtCreateMemberUsername.Text,
            TxtCreateMemberDisplayName.Text,
            TxtCreateMemberPassword.Password,
            TxtCreateMemberPin.Password);

        if (_viewModel.IsCreateManagedAccountMode)
        {
            return;
        }

        ClearInputs();
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void TxtCreateOwnerUsername_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _viewModel?.ResetCreateManagedAccountOwnerVerification();
    }

    private void TxtCreateOwnerPassword_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel?.ResetCreateManagedAccountOwnerVerification();
    }

    private void TxtCreateOwnerPin_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel?.ResetCreateManagedAccountOwnerVerification();
    }
}
