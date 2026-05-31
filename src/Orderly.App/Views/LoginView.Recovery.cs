using System;
using System.Threading.Tasks;
using System.Windows;

namespace Orderly.App.Views;

public partial class LoginView : Window
{
    private async void BtnOpenPasswordRecovery_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.EnterPasswordRecoveryMode();
        await WaitForSurfaceAsync(LoginSurface.PasswordRecovery);
        FocusPrimaryField();
    }

    private async void OnPasswordRecoveryBackRequested(object? sender, EventArgs e)
    {
        await WaitForSurfaceAsync(LoginSurface.SignIn);
        FocusPrimaryField();
    }
}
