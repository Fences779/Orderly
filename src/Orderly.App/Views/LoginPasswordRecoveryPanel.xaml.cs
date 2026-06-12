using System;
using System.Windows;
using Orderly.App.ViewModels;

namespace Orderly.App.Views;

public partial class LoginPasswordRecoveryPanel : System.Windows.Controls.UserControl
{
    private LoginViewModel? _viewModel;

    public LoginPasswordRecoveryPanel()
    {
        InitializeComponent();
    }

    public event EventHandler? BackRequested;

    public void Initialize(LoginViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    public void FocusPrimary()
    {
        TxtRecoveryOwnerUsername.Focus();
    }

    public void ClearInputs()
    {
        TxtRecoveryAdminUsername.Text = string.Empty;
        TxtRecoveryAdminPassword.Password = string.Empty;
        TxtRecoveryAdminPin.Password = string.Empty;
        TxtRecoveryKeyInput.Text = string.Empty;
        TxtRecoveryOwnerPin.Password = string.Empty;
        TxtRecoveryNewPassword.Password = string.Empty;
    }

    // Mirrors the parent's prior inline write of the recovery notice text/visibility.
    public void SetNotice(string text, bool visible)
    {
        TxtNotice.Text = text;
        TxtNotice.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnBackToSignIn_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.ExitPasswordRecoveryMode();
        ClearInputs();
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void TxtRecoveryOwnerUsername_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.UpdatePasswordRecoveryAccountInput(TxtRecoveryOwnerUsername.Text);
        if (!_viewModel.CanEditRecoveryKeyInput)
        {
            TxtRecoveryKeyInput.Text = string.Empty;
        }

        if (!_viewModel.CanEditRecoveryOwnerVerificationInput)
        {
            TxtRecoveryAdminUsername.Text = string.Empty;
            TxtRecoveryAdminPassword.Password = string.Empty;
            TxtRecoveryAdminPin.Password = string.Empty;
        }

        TxtRecoveryNewPassword.Password = string.Empty;
    }

    private async void BtnResetOwnerPassword_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (_viewModel.CanEditRecoveryKeyInput)
        {
            if (!_viewModel.IsRecoveryVerificationConfirmed)
            {
                await _viewModel.VerifyOwnerPasswordRecoveryAsync(
                    TxtRecoveryOwnerUsername.Text,
                    TxtRecoveryOwnerPin.Password,
                    TxtRecoveryKeyInput.Text);

                if (_viewModel.IsRecoveryVerificationConfirmed)
                {
                    TxtRecoveryNewPassword.Focus();
                }

                return;
            }

            await _viewModel.ResetOwnerPasswordWithRecoveryKeyAsync(
                TxtRecoveryOwnerUsername.Text,
                TxtRecoveryOwnerPin.Password,
                TxtRecoveryKeyInput.Text,
                TxtRecoveryNewPassword.Password);
            return;
        }

        if (_viewModel.CanEditRecoveryOwnerVerificationInput)
        {
            if (!_viewModel.IsRecoveryVerificationConfirmed)
            {
                await _viewModel.VerifyMemberPasswordResetAsync(
                    TxtRecoveryOwnerUsername.Text,
                    TxtRecoveryOwnerPin.Password,
                    TxtRecoveryAdminUsername.Text,
                    TxtRecoveryAdminPassword.Password,
                    TxtRecoveryAdminPin.Password);

                if (_viewModel.IsRecoveryVerificationConfirmed)
                {
                    TxtRecoveryNewPassword.Focus();
                }

                return;
            }

            await _viewModel.ResetMemberPasswordWithOwnerVerificationAsync(
                TxtRecoveryOwnerUsername.Text,
                TxtRecoveryOwnerPin.Password,
                TxtRecoveryAdminUsername.Text,
                TxtRecoveryAdminPassword.Password,
                TxtRecoveryAdminPin.Password,
                TxtRecoveryNewPassword.Password);
            return;
        }

        _viewModel.ErrorMessage = "请先输入有效账号。";
    }
}
