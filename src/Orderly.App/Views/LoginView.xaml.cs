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
    private static readonly TimeSpan SurfaceTransitionDuration = TimeSpan.FromMilliseconds(120);

    private readonly LoginViewModel _viewModel;
    private bool _hasLoaded;
    private LoginSurface _currentSurface;
    private string _lastShownErrorMessage = string.Empty;
    private int _transitionVersion;

    public LoginView(LoginViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        SignInPanel.Initialize(viewModel);
        SignInPanel.OpenRecoveryRequested += OnSignInOpenRecoveryRequested;
        SignInPanel.OpenAccountManagementRequested += OnSignInOpenAccountManagementRequested;
        OwnerCreatePanel.Initialize(viewModel);
        CreateManagedAccountPanel.Initialize(viewModel);
        CreateManagedAccountPanel.BackRequested += OnCreateManagedAccountBackRequested;
        AccountManagementPanel.Initialize(viewModel);
        AccountManagementPanel.BackRequested += OnAccountManagementBackRequested;
        AccountManagementPanel.CreateAccountRequested += OnAccountManagementCreateRequested;
        AccountManagementPanel.DeleteAccountRequested += OnAccountManagementDeleteRequested;
        PasswordRecoveryPanel.Initialize(viewModel);
        PasswordRecoveryPanel.BackRequested += OnPasswordRecoveryBackRequested;
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

    private async void OnSignInOpenRecoveryRequested(object? sender, EventArgs e)
    {
        _viewModel.EnterPasswordRecoveryMode();
        await WaitForSurfaceAsync(LoginSurface.PasswordRecovery);
        FocusPrimaryField();
    }

    private async void OnSignInOpenAccountManagementRequested(object? sender, EventArgs e)
    {
        await _viewModel.EnterAccountManagementModeAsync();
        await WaitForSurfaceAsync(LoginSurface.AccountManagement);
        FocusPrimaryField();
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
        PasswordRecoveryPanel.SetNotice(_viewModel.NoticeMessage, _viewModel.HasNoticeMessage);
        SignInPanel.SetAccountHint(_viewModel.SignInAccountErrorMessage, _viewModel.HasSignInAccountErrorMessage);

        OwnerCreatePanel.ApplyRecoveryState();
        SignInPanel.UpdateCredentialSectionState(_currentSurface == LoginSurface.SignIn, _hasLoaded);
    }

    private void FocusPrimaryField()
    {
        SignInPanel.CloseRecentAccountsPopupExternally();

        switch (_currentSurface)
        {
            case LoginSurface.OwnerCreate:
                OwnerCreatePanel.FocusPrimary();
                return;
            case LoginSurface.PasswordRecovery:
                PasswordRecoveryPanel.FocusPrimary();
                return;
            case LoginSurface.AccountManagement:
                AccountManagementPanel.FocusPrimary();
                return;
            case LoginSurface.CreateMember:
                CreateManagedAccountPanel.FocusPrimary();
                return;
            default:
                SignInPanel.FocusPrimary();
                return;
        }
    }
}
