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
    private const double SignInAccountLiftOffset = -8d;
    private const double RecentAccountsDropdownExpandedMaxHeight = 188d;
    private const double SignInCredentialExpandedMaxHeight = 182d;
    private static readonly TimeSpan SurfaceTransitionDuration = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan RecentAccountsDropdownTransitionDuration = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan SignInCredentialTransitionDuration = TimeSpan.FromMilliseconds(180);

    private readonly LoginViewModel _viewModel;
    private bool _hasLoaded;
    private LoginSurface _currentSurface;
    private string _lastShownErrorMessage = string.Empty;
    private int _transitionVersion;
    private int _errorToastVersion;
    private int _successToastVersion;
    private bool _suppressRecentAccountPopup;
    private bool _suppressNextSignInFocusPopup;
    private bool _isSignInCredentialSectionExpanded;
    private bool _isRecentAccountsPopupOpen;

    public LoginView(LoginViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ApplyViewModelState();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
        _hasLoaded = true;
        FocusPrimaryField();
        _ = Dispatcher.BeginInvoke(FocusPrimaryField, DispatcherPriority.Input);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LoginViewModel.SuccessToastMessage)
            && !string.IsNullOrWhiteSpace(_viewModel.SuccessToastMessage))
        {
            ShowSuccessToast(_viewModel.SuccessToastMessage);
            _viewModel.ConsumeSuccessToast();
        }
        else if (e.PropertyName == nameof(LoginViewModel.ErrorMessage))
        {
            if (string.IsNullOrWhiteSpace(_viewModel.ErrorMessage))
            {
                _lastShownErrorMessage = string.Empty;
            }
            else if (!string.Equals(_lastShownErrorMessage, _viewModel.ErrorMessage, StringComparison.Ordinal))
            {
                _lastShownErrorMessage = _viewModel.ErrorMessage;
                ShowErrorToast(_viewModel.ErrorMessage);
            }
        }

        ApplyViewModelState();
    }

    private void ApplyViewModelState()
    {
        RootGrid.IsEnabled = !_viewModel.IsBusy;

        var targetSurface = ResolveCurrentSurface();
        if (!_hasLoaded)
        {
            ApplySurfaceStateImmediately(targetSurface);
        }
        else if (targetSurface != _currentSurface)
        {
            _ = TransitionToSurfaceAsync(targetSurface);
        }

        TxtError.Text = string.Empty;
        TxtError.Visibility = Visibility.Collapsed;
        TxtNotice.Text = _viewModel.NoticeMessage;
        TxtNotice.Visibility = _viewModel.HasNoticeMessage ? Visibility.Visible : Visibility.Collapsed;
        TxtSignInAccountHint.Text = _viewModel.SignInAccountErrorMessage;
        TxtSignInAccountHint.Visibility = _viewModel.HasSignInAccountErrorMessage ? Visibility.Visible : Visibility.Collapsed;

        RecoveryPanel.Visibility = _viewModel.IsRecoveryStepVisible ? Visibility.Visible : Visibility.Collapsed;
        TxtRecoveryKeyValue.Text = _viewModel.GeneratedRecoveryKey;
        ChkRecoverySaved.IsChecked = _viewModel.IsRecoveryKeyConfirmed;
        BtnContinueAfterRecovery.IsEnabled = _viewModel.IsRecoveryConfirmationReady;
        UpdateSignInCredentialSectionState(_hasLoaded);
    }

    private void FocusPrimaryField()
    {
        CloseRecentAccountsPopup();

        switch (_currentSurface)
        {
            case LoginSurface.OwnerCreate:
                TxtOwnerUsername.Focus();
                return;
            case LoginSurface.PasswordRecovery:
                TxtRecoveryOwnerUsername.Focus();
                return;
            case LoginSurface.AccountManagement:
                BtnBackFromAccountManagement.Focus();
                return;
            case LoginSurface.CreateMember:
                TxtCreateOwnerUsername.Focus();
                return;
            default:
                _suppressNextSignInFocusPopup = true;
                TxtSignInUsername.Focus();
                Keyboard.Focus(TxtSignInUsername);
                TxtSignInUsername.CaretIndex = TxtSignInUsername.Text.Length;
                return;
        }
    }

    private async void BtnSignIn_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.SignInAsync(TxtSignInUsername.Text, TxtSignInPassword.Password);
    }

    private async void BtnCreateOwner_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.CreateFirstOwnerAsync(
            TxtOwnerUsername.Text,
            TxtOwnerDisplayName.Text,
            TxtOwnerPassword.Password,
            TxtOwnerPin.Password);
    }

    private void BtnContinueAfterRecovery_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ConfirmRecoveryKeyAndContinue();
    }

    private void ChkRecoverySaved_OnChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.IsRecoveryKeyConfirmed = ChkRecoverySaved.IsChecked == true;
    }

    private async void TxtSignInPassword_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        await _viewModel.SignInAsync(TxtSignInUsername.Text, TxtSignInPassword.Password);
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

    private async void BtnOpenAccountManagement_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.EnterAccountManagementModeAsync();
        await WaitForSurfaceAsync(LoginSurface.AccountManagement);
        FocusPrimaryField();
    }

    private async void BtnBackFromAccountManagement_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ExitAccountManagementMode();
        ClearDeleteAccountInputs();
        await WaitForSurfaceAsync(LoginSurface.SignIn);
        FocusPrimaryField();
    }

    private void BtnRequestDeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string accountId })
        {
            return;
        }

        _viewModel.BeginDeleteAccount(accountId);
        TxtDeleteOwnerUsername.Focus();
    }

    private void BtnCancelDeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        ClosePendingDeleteDialog();
    }

    private async void BtnConfirmDeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.DeletePendingAccountAsync(
            TxtDeleteOwnerUsername.Text,
            TxtDeleteOwnerPassword.Password,
            TxtDeleteOwnerPin.Password);

        if (!_viewModel.HasPendingDeleteAccount)
        {
            ClearDeleteAccountInputs();
        }
    }

    private void DeleteAccountDialog_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void BtnCreateManagedAccount_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.EnterCreateManagedAccountMode();
        _ = WaitAndFocusCreateManagedAccountAsync();
    }

    private async Task WaitAndFocusCreateManagedAccountAsync()
    {
        await WaitForSurfaceAsync(LoginSurface.CreateMember);
        FocusPrimaryField();
    }

    private async void BtnBackFromCreateManagedAccount_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ExitCreateManagedAccountMode();
        ClearCreateManagedAccountInputs();
        await WaitForSurfaceAsync(LoginSurface.AccountManagement);
        FocusPrimaryField();
    }

    private async void BtnSubmitCreateManagedAccount_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.IsCreateManagedAccountOwnerVerified)
        {
            await _viewModel.VerifyCreateManagedAccountOwnerAsync(
                TxtCreateOwnerUsername.Text,
                TxtCreateOwnerPassword.Password,
                TxtCreateOwnerPin.Password);

            if (_viewModel.IsCreateManagedAccountOwnerVerified)
            {
                TxtCreateMemberUsername.Focus();
            }

            return;
        }

        await _viewModel.CreateManagedAccountAsync(
            TxtCreateOwnerUsername.Text,
            TxtCreateOwnerPassword.Password,
            TxtCreateOwnerPin.Password,
            TxtCreateMemberUsername.Text,
            TxtCreateMemberDisplayName.Text,
            TxtCreateMemberPassword.Password,
            TxtCreateMemberPin.Password);

        if (_viewModel.IsCreateManagedAccountMode)
        {
            return;
        }

        ClearCreateManagedAccountInputs();
        await WaitForSurfaceAsync(LoginSurface.AccountManagement);
        FocusPrimaryField();
    }

    private void BtnAcknowledgeAccountManagementNotice_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AcknowledgeAccountManagementNotice();
        FocusPrimaryField();
    }

    private void RootGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (IsInteractiveClickSource(e.OriginalSource))
        {
            return;
        }

        this.DragMove();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !_viewModel.HasPendingDeleteAccount)
        {
            return;
        }

        ClosePendingDeleteDialog();
        e.Handled = true;
    }

    private void MainCardContent_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (MainCardContent.ActualWidth <= 0 || MainCardContent.ActualHeight <= 0)
        {
            MainCardContent.Clip = null;
            return;
        }

        MainCardContent.Clip = new RectangleGeometry(
            new Rect(0, 0, MainCardContent.ActualWidth, MainCardContent.ActualHeight),
            24,
            24);
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Application.Current.Shutdown();
    }

    private void ClearDeleteAccountInputs()
    {
        TxtDeleteOwnerUsername.Text = string.Empty;
        TxtDeleteOwnerPassword.Password = string.Empty;
        TxtDeleteOwnerPin.Password = string.Empty;
    }

    private void ClosePendingDeleteDialog()
    {
        _viewModel.CancelDeleteAccount();
        ClearDeleteAccountInputs();
    }

    private void ClearCreateManagedAccountInputs()
    {
        TxtCreateOwnerUsername.Text = string.Empty;
        TxtCreateOwnerPassword.Password = string.Empty;
        TxtCreateOwnerPin.Password = string.Empty;
        TxtCreateMemberUsername.Text = string.Empty;
        TxtCreateMemberDisplayName.Text = string.Empty;
        TxtCreateMemberPassword.Password = string.Empty;
        TxtCreateMemberPin.Password = string.Empty;
    }

    private void InvalidateCreateManagedAccountOwnerVerification()
    {
        _viewModel.ResetCreateManagedAccountOwnerVerification();
    }

    private void TxtCreateOwnerUsername_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        InvalidateCreateManagedAccountOwnerVerification();
    }

    private void TxtCreateOwnerPassword_PasswordChanged(object sender, RoutedEventArgs e)
    {
        InvalidateCreateManagedAccountOwnerVerification();
    }

    private void TxtCreateOwnerPin_PasswordChanged(object sender, RoutedEventArgs e)
    {
        InvalidateCreateManagedAccountOwnerVerification();
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

    private void UpdateSignInCredentialSectionState(bool animate)
    {
        var shouldExpand = _currentSurface == LoginSurface.SignIn && _viewModel.IsSignInPasswordStepVisible;
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

    private static bool IsInteractiveClickSource(object? source)
    {
        for (var current = source as DependencyObject; current is not null; current = GetParent(current))
        {
            if (current is System.Windows.Controls.Primitives.TextBoxBase
                || current is System.Windows.Controls.PasswordBox
                || current is System.Windows.Controls.Primitives.ButtonBase
                || current is System.Windows.Controls.Primitives.Selector
                || current is System.Windows.Controls.Primitives.ScrollBar)
            {
                return true;
            }
        }

        return false;
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

    private LoginSurface ResolveCurrentSurface()
    {
        if (_viewModel.IsFirstRunMode)
        {
            return LoginSurface.OwnerCreate;
        }

        if (_viewModel.IsPasswordRecoveryMode)
        {
            return LoginSurface.PasswordRecovery;
        }

        if (_viewModel.IsAccountManagementMode)
        {
            return LoginSurface.AccountManagement;
        }

        if (_viewModel.IsCreateManagedAccountMode)
        {
            return LoginSurface.CreateMember;
        }

        return LoginSurface.SignIn;
    }

    private void ApplySurfaceStateImmediately(LoginSurface surface)
    {
        _currentSurface = surface;
        ApplyPanelState(SignInPanel, surface == LoginSurface.SignIn);
        ApplyPanelState(PasswordRecoveryPanel, surface == LoginSurface.PasswordRecovery);
        ApplyPanelState(AccountManagementPanel, surface == LoginSurface.AccountManagement);
        ApplyPanelState(CreateManagedAccountPanel, surface == LoginSurface.CreateMember);
        OwnerCreatePanel.Visibility = surface == LoginSurface.OwnerCreate ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task TransitionToSurfaceAsync(LoginSurface targetSurface)
    {
        var transitionVersion = ++_transitionVersion;
        var previousSurface = _currentSurface;

        if (targetSurface == LoginSurface.OwnerCreate || previousSurface == LoginSurface.OwnerCreate)
        {
            ApplySurfaceStateImmediately(targetSurface);
            return;
        }

        var outgoingPanel = GetPanel(previousSurface);
        var incomingPanel = GetPanel(targetSurface);

        if (incomingPanel is null)
        {
            ApplySurfaceStateImmediately(targetSurface);
            return;
        }

        _currentSurface = targetSurface;
        OwnerCreatePanel.Visibility = Visibility.Collapsed;
        PrepareIncomingPanel(incomingPanel);

        var outgoingTask = outgoingPanel is null
            ? Task.CompletedTask
            : AnimatePanelAsync(outgoingPanel, 0d);
        var incomingTask = AnimatePanelAsync(incomingPanel, 1d);

        await Task.WhenAll(outgoingTask, incomingTask);

        if (transitionVersion != _transitionVersion)
        {
            return;
        }

        if (outgoingPanel is not null)
        {
            ApplyPanelState(outgoingPanel, false);
        }

        ApplyPanelState(incomingPanel, true);
    }

    private async Task WaitForSurfaceAsync(LoginSurface surface)
    {
        for (var i = 0; i < 20; i++)
        {
            if (_currentSurface == surface)
            {
                return;
            }

            await Task.Delay(15);
        }
    }

    private static void PrepareIncomingPanel(FrameworkElement panel)
    {
        panel.Visibility = Visibility.Visible;
        panel.Opacity = 0d;
        EnsureTranslateTransform(panel).X = 0d;
    }

    private static void ApplyPanelState(FrameworkElement panel, bool isVisible)
    {
        panel.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        panel.Opacity = isVisible ? 1d : 0d;
        EnsureTranslateTransform(panel).X = 0d;
    }

    private static TranslateTransform EnsureTranslateTransform(UIElement element)
    {
        if (element.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            element.RenderTransform = transform;
        }

        return transform;
    }

    private static Task AnimatePanelAsync(FrameworkElement panel, double targetOpacity)
    {
        var storyboard = new Storyboard();
        var completion = new TaskCompletionSource<object?>();

        var opacityAnimation = new DoubleAnimation
        {
            To = targetOpacity,
            Duration = new Duration(SurfaceTransitionDuration),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        Storyboard.SetTarget(opacityAnimation, panel);
        Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath(UIElement.OpacityProperty));

        storyboard.Children.Add(opacityAnimation);
        storyboard.Completed += (_, _) => completion.TrySetResult(null);
        storyboard.Begin();

        return completion.Task;
    }

    private async void ShowSuccessToast(string message)
    {
        var toastVersion = ++_successToastVersion;
        TxtSuccessToast.Text = message;
        SuccessToastOverlay.Visibility = Visibility.Visible;
        SuccessToastOverlay.Opacity = 0d;
        SuccessToastTranslateTransform.Y = 10d;

        AnimateDouble(
            SuccessToastOverlay,
            UIElement.OpacityProperty,
            1d,
            TimeSpan.FromMilliseconds(180));
        AnimateDouble(
            SuccessToastTranslateTransform,
            TranslateTransform.YProperty,
            0d,
            TimeSpan.FromMilliseconds(180));

        await Task.Delay(2000);
        if (toastVersion != _successToastVersion)
        {
            return;
        }

        AnimateDouble(
            SuccessToastOverlay,
            UIElement.OpacityProperty,
            0d,
            TimeSpan.FromMilliseconds(240),
            () =>
            {
                if (toastVersion != _successToastVersion)
                {
                    return;
                }

                SuccessToastOverlay.Visibility = Visibility.Collapsed;
            });
        AnimateDouble(
            SuccessToastTranslateTransform,
            TranslateTransform.YProperty,
            -6d,
            TimeSpan.FromMilliseconds(240));
    }

    private async void ShowErrorToast(string message)
    {
        var toastVersion = ++_errorToastVersion;
        TxtErrorToast.Text = FormatErrorToastMessage(message);
        ErrorToastOverlay.Visibility = Visibility.Visible;
        ErrorToastOverlay.Opacity = 0d;
        ErrorToastTranslateTransform.Y = 10d;

        AnimateDouble(
            ErrorToastOverlay,
            UIElement.OpacityProperty,
            1d,
            TimeSpan.FromMilliseconds(180));
        AnimateDouble(
            ErrorToastTranslateTransform,
            TranslateTransform.YProperty,
            0d,
            TimeSpan.FromMilliseconds(180));

        await Task.Delay(2800);
        if (toastVersion != _errorToastVersion)
        {
            return;
        }

        AnimateDouble(
            ErrorToastOverlay,
            UIElement.OpacityProperty,
            0d,
            TimeSpan.FromMilliseconds(240),
            () =>
            {
                if (toastVersion != _errorToastVersion)
                {
                    return;
                }

                ErrorToastOverlay.Visibility = Visibility.Collapsed;
            });
        AnimateDouble(
            ErrorToastTranslateTransform,
            TranslateTransform.YProperty,
            -6d,
            TimeSpan.FromMilliseconds(240));
    }

    private static string FormatErrorToastMessage(string message)
    {
        if (string.Equals(message, MasterPasswordPolicy.ValidationMessage, StringComparison.Ordinal))
        {
            return "新密码不符合要求\n至少 8 位，且必须同时包含大小写字母和数字\n不能包含空白字符";
        }

        return message;
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

    private FrameworkElement? GetPanel(LoginSurface surface)
    {
        return surface switch
        {
            LoginSurface.SignIn => SignInPanel,
            LoginSurface.PasswordRecovery => PasswordRecoveryPanel,
            LoginSurface.AccountManagement => AccountManagementPanel,
            LoginSurface.CreateMember => CreateManagedAccountPanel,
            _ => null
        };
    }

    private enum LoginSurface
    {
        SignIn,
        PasswordRecovery,
        AccountManagement,
        CreateMember,
        OwnerCreate
    }
}
