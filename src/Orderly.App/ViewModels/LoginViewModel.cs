using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Orderly.App.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    public event EventHandler? LoginSucceeded;

    [ObservableProperty]
    private string username = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private bool rememberMe;

    [ObservableProperty]
    private bool isPasswordVisible;

    [RelayCommand]
    private void Login()
    {
        LoginSucceeded?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void TogglePasswordVisibility()
    {
        IsPasswordVisible = !IsPasswordVisible;
    }
}
