using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Effects;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Orderly.App.ViewModels;
using Orderly.Core.Models;
using Orderly.Core.Security;

namespace Orderly.App.Views;

public partial class LoginView : Window
{
    private async void BtnSignIn_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.SignInAsync(TxtSignInUsername.Text, TxtSignInPassword.Password);
    }

    private async void TxtSignInPassword_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        await _viewModel.SignInAsync(TxtSignInUsername.Text, TxtSignInPassword.Password);
    }

    private void TxtSignInUsername_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!TxtSignInUsername.IsKeyboardFocusWithin)
        {
            TxtSignInUsername.Focus();
            e.Handled = true;
        }

        RequestOpenRecentAccountsPopup();
    }

    private void TxtSignInUsername_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_suppressNextSignInFocusPopup)
        {
            _suppressNextSignInFocusPopup = false;
            return;
        }

        RequestOpenRecentAccountsPopup();
    }

    private void TxtSignInUsername_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        if (TryConfirmCurrentSignInAccount())
        {
            TxtSignInPassword.Focus();
        }
    }

    private void TxtSignInUsername_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_suppressRecentAccountPopup)
        {
            return;
        }

        _viewModel.UpdateSignInUsernameInput(TxtSignInUsername.Text);
        if (!_viewModel.IsSignInAccountConfirmed)
        {
            TxtSignInPassword.Password = string.Empty;
        }

        if (_viewModel.HasFilteredSignInAccounts)
        {
            TryOpenRecentAccountsPopup();
            return;
        }

        CloseRecentAccountsPopup();
    }

    private async void TxtSignInUsername_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        await Task.Delay(120);
        if (!TxtSignInUsername.IsKeyboardFocusWithin
            && !LstRecentSignInAccounts.IsKeyboardFocusWithin
            && !RecentAccountsDropdown.IsKeyboardFocusWithin
            && !RecentAccountsDropdown.IsMouseOver)
        {
            if (!_viewModel.IsSignInAccountConfirmed)
            {
                TryConfirmCurrentSignInAccount();
            }

            CloseRecentAccountsPopup();
        }
    }

    private void LstRecentSignInAccounts_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (LstRecentSignInAccounts.SelectedItem is not LocalAccountSummary account)
        {
            return;
        }

        _suppressRecentAccountPopup = true;
        TxtSignInUsername.Text = account.Username;
        TxtSignInUsername.CaretIndex = TxtSignInUsername.Text.Length;
        _suppressRecentAccountPopup = false;
        _viewModel.TryConfirmSignInAccount(account.Username);

        CloseRecentAccountsPopup();
        LstRecentSignInAccounts.SelectedItem = null;
        TxtSignInPassword.Focus();
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (RecentAccountsDropdown.Visibility != Visibility.Visible)
        {
            return;
        }

        if (IsDescendantOf(e.OriginalSource as DependencyObject, TxtSignInUsername)
            || IsDescendantOf(e.OriginalSource as DependencyObject, RecentAccountsDropdown))
        {
            return;
        }

        if (!_viewModel.IsSignInAccountConfirmed)
        {
            TryConfirmCurrentSignInAccount();
        }

        CloseRecentAccountsPopup();
    }
}
