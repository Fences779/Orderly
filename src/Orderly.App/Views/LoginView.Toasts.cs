using System.Windows;

namespace Orderly.App.Views;

public partial class LoginView : Window
{
    private void ShowSuccessToast(string message)
    {
        ToastOverlay.ShowSuccess(message);
    }

    private void ShowErrorToast(string message)
    {
        ToastOverlay.ShowError(message);
    }
}
