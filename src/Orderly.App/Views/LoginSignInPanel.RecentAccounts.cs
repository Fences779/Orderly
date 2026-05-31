using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Orderly.App.Views;

// Recent-accounts dropdown popup state machine and shared animation/visual-tree helpers
// for the sign-in surface. Relocated verbatim from the control's main code-behind file
// to keep each file within the UI line budget; behavior is unchanged.
public partial class LoginSignInPanel
{
    private void RequestOpenRecentAccountsPopup()
    {
        Dispatcher.BeginInvoke(
            TryOpenRecentAccountsPopup,
            DispatcherPriority.Input);
    }

    private void TryOpenRecentAccountsPopup()
    {
        if (!_isSignInSurfaceActive
            || _viewModel is null
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
        if (_viewModel is null)
        {
            return false;
        }

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

    private static bool IsDescendantOf(DependencyObject? source, DependencyObject ancestor)
    {
        for (var current = source; current is not null; current = GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static DependencyObject? GetParent(DependencyObject current)
    {
        return current switch
        {
            Visual visual => VisualTreeHelper.GetParent(visual),
            FrameworkContentElement contentElement => contentElement.Parent,
            _ => LogicalTreeHelper.GetParent(current)
        };
    }

    private static void AnimateDouble(
        DependencyObject target,
        DependencyProperty property,
        double to,
        TimeSpan duration,
        Action? completed = null)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = new Duration(duration),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        if (completed is not null)
        {
            animation.Completed += (_, _) => completed();
        }

        switch (target)
        {
            case Animatable animatable:
                animatable.BeginAnimation(property, animation);
                break;
            case UIElement element:
                element.BeginAnimation(property, animation);
                break;
        }
    }
}
