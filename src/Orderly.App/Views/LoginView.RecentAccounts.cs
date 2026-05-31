using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Orderly.App.Views;

public partial class LoginView : Window
{
    private void RequestOpenRecentAccountsPopup()
    {
        Dispatcher.BeginInvoke(
            TryOpenRecentAccountsPopup,
            DispatcherPriority.Input);
    }

    private void TryOpenRecentAccountsPopup()
    {
        if (_currentSurface != LoginSurface.SignIn
            || _viewModel.IsBusy
            || _viewModel.IsSignInAccountConfirmed
            || !_viewModel.HasFilteredSignInAccounts)
        {
            CloseRecentAccountsPopup();
            return;
        }

        if (_isRecentAccountsPopupOpen)
        {
            return;
        }

        _isRecentAccountsPopupOpen = true;
        RecentAccountsDropdown.Visibility = Visibility.Visible;
        RecentAccountsDropdown.IsHitTestVisible = true;
        AnimateDouble(
            SignInAccountBlockTranslateTransform,
            TranslateTransform.YProperty,
            SignInAccountLiftOffset,
            RecentAccountsDropdownTransitionDuration);
        AnimateDouble(
            RecentAccountsDropdown,
            FrameworkElement.MaxHeightProperty,
            RecentAccountsDropdownExpandedMaxHeight,
            RecentAccountsDropdownTransitionDuration);
        AnimateDouble(
            RecentAccountsDropdown,
            UIElement.OpacityProperty,
            1d,
            RecentAccountsDropdownTransitionDuration);
        AnimateDouble(
            RecentAccountsDropdownScaleTransform,
            ScaleTransform.ScaleYProperty,
            1d,
            RecentAccountsDropdownTransitionDuration);
        AnimateDouble(
            RecentAccountsDropdownTranslateTransform,
            TranslateTransform.YProperty,
            0d,
            RecentAccountsDropdownTransitionDuration);
    }

    private void CloseRecentAccountsPopup()
    {
        if (!_isRecentAccountsPopupOpen && RecentAccountsDropdown.Visibility != Visibility.Visible)
        {
            return;
        }

        _isRecentAccountsPopupOpen = false;
        RecentAccountsDropdown.IsHitTestVisible = false;
        AnimateDouble(
            SignInAccountBlockTranslateTransform,
            TranslateTransform.YProperty,
            0d,
            RecentAccountsDropdownTransitionDuration);
        AnimateDouble(
            RecentAccountsDropdown,
            FrameworkElement.MaxHeightProperty,
            0d,
            RecentAccountsDropdownTransitionDuration);
        AnimateDouble(
            RecentAccountsDropdown,
            UIElement.OpacityProperty,
            0d,
            RecentAccountsDropdownTransitionDuration,
            () =>
            {
                if (_isRecentAccountsPopupOpen)
                {
                    return;
                }

                RecentAccountsDropdown.Visibility = Visibility.Collapsed;
            });
        AnimateDouble(
            RecentAccountsDropdownScaleTransform,
            ScaleTransform.ScaleYProperty,
            0.96d,
            RecentAccountsDropdownTransitionDuration);
        AnimateDouble(
            RecentAccountsDropdownTranslateTransform,
            TranslateTransform.YProperty,
            -8d,
            RecentAccountsDropdownTransitionDuration);

        if (RecentAccountsDropdown.Visibility == Visibility.Visible)
        {
            LstRecentSignInAccounts.SelectedItem = null;
        }
    }

    private bool TryConfirmCurrentSignInAccount()
    {
        var confirmed = _viewModel.TryConfirmSignInAccount(TxtSignInUsername.Text);
        if (confirmed && _viewModel.ConfirmedSignInAccount is not null)
        {
            _suppressRecentAccountPopup = true;
            TxtSignInUsername.Text = _viewModel.ConfirmedSignInAccount.Username;
            TxtSignInUsername.CaretIndex = TxtSignInUsername.Text.Length;
            _suppressRecentAccountPopup = false;
            CloseRecentAccountsPopup();
        }

        return confirmed;
    }
}
