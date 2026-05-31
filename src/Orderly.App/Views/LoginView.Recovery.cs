using System.Threading.Tasks;
using System.Windows;

namespace Orderly.App.Views;

public partial class LoginView : Window
{
    private void BtnContinueAfterRecovery_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ConfirmRecoveryKeyAndContinue();
    }

    private void ChkRecoverySaved_OnChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.IsRecoveryKeyConfirmed = ChkRecoverySaved.IsChecked == true;
    }

    private async void BtnResetOwnerPassword_Click(object sender, RoutedEventArgs e)
    {
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
                TxtRecoveryNewPassword.Text);
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
                TxtRecoveryNewPassword.Text);
            return;
        }

        _viewModel.ErrorMessage = "请先输入有效账号。";
    }

    private async void BtnOpenPasswordRecovery_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.EnterPasswordRecoveryMode();
        await WaitForSurfaceAsync(LoginSurface.PasswordRecovery);
        FocusPrimaryField();
    }

    private async void BtnBackToSignIn_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ExitPasswordRecoveryMode();
        TxtRecoveryAdminUsername.Text = string.Empty;
        TxtRecoveryAdminPassword.Password = string.Empty;
        TxtRecoveryAdminPin.Password = string.Empty;
        TxtRecoveryKeyInput.Text = string.Empty;
        TxtRecoveryOwnerPin.Password = string.Empty;
        TxtRecoveryNewPassword.Text = string.Empty;
        await WaitForSurfaceAsync(LoginSurface.SignIn);
        FocusPrimaryField();
    }

    private void TxtRecoveryOwnerUsername_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
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

        TxtRecoveryNewPassword.Text = string.Empty;
    }
}
