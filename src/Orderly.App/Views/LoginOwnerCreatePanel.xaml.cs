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
        TxtCopyRecoveryKeyStatus.Visibility = Visibility.Collapsed;
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

    private void BtnCopyRecoveryKey_Click(object sender, RoutedEventArgs e)
    {
        var recoveryKey = TxtRecoveryKeyValue.Text;
        if (string.IsNullOrWhiteSpace(recoveryKey))
        {
            ShowCopyRecoveryKeyStatus("当前没有可复制的密钥。", isError: true);
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(recoveryKey);
            ShowCopyRecoveryKeyStatus("已复制到剪贴板。");
        }
        catch (Exception ex)
        {
            ShowCopyRecoveryKeyStatus($"复制失败：{ex.Message}", isError: true);
        }
    }

    private void ShowCopyRecoveryKeyStatus(string message, bool isError = false)
    {
        TxtCopyRecoveryKeyStatus.Text = message;
        TxtCopyRecoveryKeyStatus.Foreground = isError
            ? System.Windows.Media.Brushes.Firebrick
            : System.Windows.Media.Brushes.ForestGreen;
        TxtCopyRecoveryKeyStatus.Visibility = Visibility.Visible;
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
