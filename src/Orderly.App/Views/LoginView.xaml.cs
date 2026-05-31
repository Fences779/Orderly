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
    private bool _suppressRecentAccountPopup;
    private bool _suppressNextSignInFocusPopup;
    private bool _isSignInCredentialSectionExpanded;
    private bool _isRecentAccountsPopupOpen;

    public LoginView(LoginViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        OwnerCreatePanel.Initialize(viewModel);
        CreateManagedAccountPanel.Initialize(viewModel);
        CreateManagedAccountPanel.BackRequested += OnCreateManagedAccountBackRequested;
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

        OwnerCreatePanel.ApplyRecoveryState();
        UpdateSignInCredentialSectionState(_hasLoaded);
    }

    private void FocusPrimaryField()
    {
        CloseRecentAccountsPopup();

        switch (_currentSurface)
        {
            case LoginSurface.OwnerCreate:
                OwnerCreatePanel.FocusPrimary();
                return;
            case LoginSurface.PasswordRecovery:
                TxtRecoveryOwnerUsername.Focus();
                return;
            case LoginSurface.AccountManagement:
                BtnBackFromAccountManagement.Focus();
                return;
            case LoginSurface.CreateMember:
                CreateManagedAccountPanel.FocusPrimary();
                return;
            default:
                _suppressNextSignInFocusPopup = true;
                TxtSignInUsername.Focus();
                Keyboard.Focus(TxtSignInUsername);
                TxtSignInUsername.CaretIndex = TxtSignInUsername.Text.Length;
                return;
        }
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
}
