using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Orderly.App.ViewModels;
using Orderly.Core.Models;

namespace Orderly.App.Views;

public partial class LoginSignInPanel : System.Windows.Controls.UserControl
{
    private const double SignInAccountLiftOffset = -8d;
    private const double RecentAccountsDropdownExpandedMaxHeight = 188d;
    private const double SignInCredentialExpandedMaxHeight = 310d;
    private static readonly TimeSpan RecentAccountsDropdownTransitionDuration = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan SignInCredentialTransitionDuration = TimeSpan.FromMilliseconds(180);

    private LoginViewModel? _viewModel;
    private bool _suppressRecentAccountPopup;
    private bool _suppressNextSignInFocusPopup;
    private bool _isSignInCredentialSectionExpanded;
    private bool _isRecentAccountsPopupOpen;
    private bool _isSignInSurfaceActive;

    public LoginSignInPanel()
    {
        InitializeComponent();
    }

    public event EventHandler? OpenRecoveryRequested;
    public event EventHandler? OpenAccountManagementRequested;

    public void Initialize(LoginViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    public void FocusPrimary()
    {
        CloseRecentAccountsPopup();
        _suppressNextSignInFocusPopup = true;
        TxtSignInUsername.Focus();
        Keyboard.Focus(TxtSignInUsername);
        TxtSignInUsername.CaretIndex = TxtSignInUsername.Text.Length;
    }

    public void CloseRecentAccountsPopupExternally()
    {
        CloseRecentAccountsPopup();
    }

    public void SetAccountHint(string text, bool visible)
    {
        TxtSignInAccountHint.Text = text;
        TxtSignInAccountHint.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    // Called from the window-level preview-mouse handler so an outside click dismisses the
    // recent-accounts dropdown exactly as before.
    public void HandleWindowPreviewMouseDown(DependencyObject? originalSource)
    {
        if (_viewModel is null || RecentAccountsDropdown.Visibility != Visibility.Visible)
        {
            return;
        }

        if (IsDescendantOf(originalSource, TxtSignInUsername)
            || IsDescendantOf(originalSource, RecentAccountsDropdown))
        {
            return;
        }

        if (!_viewModel.IsSignInAccountConfirmed)
        {
            TryConfirmCurrentSignInAccount();
        }

        CloseRecentAccountsPopup();
    }

    public void UpdateCredentialSectionState(bool isSignInSurface, bool animate)
    {
        _isSignInSurfaceActive = isSignInSurface;
        if (_viewModel is null)
        {
            return;
        }

        var shouldExpand = isSignInSurface && _viewModel.IsSignInPasswordStepVisible;
        if (!animate)
        {
            ApplySignInCredentialSectionState(shouldExpand);
            return;
        }

        if (_isSignInCredentialSectionExpanded == shouldExpand)
        {
            return;
        }

        _isSignInCredentialSectionExpanded = shouldExpand;
        SignInCredentialSection.IsHitTestVisible = shouldExpand;
        AnimateDouble(
            SignInCredentialSection,
            FrameworkElement.MaxHeightProperty,
            shouldExpand ? SignInCredentialExpandedMaxHeight : 0d,
            SignInCredentialTransitionDuration);
        AnimateDouble(
            SignInCredentialSection,
            UIElement.OpacityProperty,
            shouldExpand ? 1d : 0d,
            SignInCredentialTransitionDuration);
    }

    private void ApplySignInCredentialSectionState(bool isExpanded)
    {
        _isSignInCredentialSectionExpanded = isExpanded;
        SignInCredentialSection.MaxHeight = isExpanded ? SignInCredentialExpandedMaxHeight : 0d;
        SignInCredentialSection.Opacity = isExpanded ? 1d : 0d;
        SignInCredentialSection.IsHitTestVisible = isExpanded;
    }

    private async void BtnSignIn_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        await _viewModel.SignInAsync(TxtSignInUsername.Text, TxtSignInPassword.Password);
    }

    private async void BtnQuickLoginPin_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            await _viewModel.SignInWithPinAsync(TxtQuickLoginPin.Password);
            TxtQuickLoginPin.Password = string.Empty;
        }
    }

    private void TxtQuickLoginPin_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            BtnQuickLoginPin_Click(sender, e);
        }
    }

    private async void BtnWindowsHello_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            await _viewModel.SignInWithWindowsHelloAsync();
        }
    }

    private void BtnUsePassword_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.UsePasswordLogin();
        TxtSignInPassword.Focus();
    }

    private void BtnUseQuickLogin_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.UseQuickLogin();
        FocusQuickLoginInput();
    }

    private void BtnUsePinQuickLogin_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.UsePinQuickLogin();
        TxtQuickLoginPin.Focus();
    }

    private void BtnUseWindowsHelloQuickLogin_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.UseWindowsHelloQuickLogin();
    }

    private void FocusQuickLoginInput()
    {
        if (_viewModel?.IsPinQuickLoginMode == true)
        {
            TxtQuickLoginPin.Focus();
        }
    }

    private async void TxtSignInPassword_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _viewModel is null)
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
        if (_suppressRecentAccountPopup || _viewModel is null)
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
        if (_viewModel is null)
        {
            return;
        }

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
        if (_viewModel is null || LstRecentSignInAccounts.SelectedItem is not LocalAccountSummary account)
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

    private void BtnOpenPasswordRecovery_Click(object sender, RoutedEventArgs e)
    {
        OpenRecoveryRequested?.Invoke(this, EventArgs.Empty);
    }

    private void BtnOpenAccountManagement_Click(object sender, RoutedEventArgs e)
    {
        OpenAccountManagementRequested?.Invoke(this, EventArgs.Empty);
    }
}
