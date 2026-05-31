using System;
using System.Threading.Tasks;
using System.Windows;

namespace Orderly.App.Views;

public partial class LoginView : Window
{
    private async void OnPasswordRecoveryBackRequested(object? sender, EventArgs e)
    {
        await WaitForSurfaceAsync(LoginSurface.SignIn);
        FocusPrimaryField();
    }
}
