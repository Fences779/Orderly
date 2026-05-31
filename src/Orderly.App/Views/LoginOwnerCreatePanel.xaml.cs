using System.Windows;
using Orderly.App.ViewModels;

namespace Orderly.App.Views;

public partial class LoginOwnerCreatePanel : System.Windows.Controls.UserControl
{
    private LoginViewModel? _viewModel;

    public LoginOwnerCreatePanel()
    {
        InitializeComponent();
    }

    // Minimal coordination contract: the parent supplies the shared LoginViewModel once.
    // The first-owner-creation and recovery-key flows remain entirely inside this control
    // and call the same view-model methods as before (no auth semantics change).
    public void Initialize(LoginViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    public void FocusPrimary()
    {
        TxtOwnerUsername.Focus();
    }

    // Mirrors the recovery-state application the parent previously performed inline in
    // ApplyViewModelState; behavior and values are identical.
    public void ApplyRecoveryState()
    {
        if (_viewModel is null)
        {
            return;
        }

        RecoveryPanel.Visibility = _viewModel.IsRecoveryStepVisible ? Visibility.Visible : Visibility.Collapsed;
        TxtRecoveryKeyValue.Text = _viewModel.GeneratedRecoveryKey;
        ChkRecoverySaved.IsChecked = _viewModel.IsRecoveryKeyConfirmed;
        BtnContinueAfterRecovery.IsEnabled = _viewModel.IsRecoveryConfirmationReady;
    }

    private async void BtnCreateOwner_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        await _viewModel.CreateFirstOwnerAsync(
            TxtOwnerUsername.Text,
            TxtOwnerDisplayName.Text,
            TxtOwnerPassword.Password,
            TxtOwnerPin.Password);
    }

    private void BtnContinueAfterRecovery_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.ConfirmRecoveryKeyAndContinue();
    }

    private void ChkRecoverySaved_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.IsRecoveryKeyConfirmed = ChkRecoverySaved.IsChecked == true;
    }
}
