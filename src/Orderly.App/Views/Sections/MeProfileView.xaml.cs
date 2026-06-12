using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Orderly.App.ViewModels;

namespace Orderly.App.Views.Sections;

public partial class MeProfileView : System.Windows.Controls.UserControl
{
    private MainViewModel? _viewModel;
    private bool _syncingSecretInputs;

    public MeProfileView()
    {
        InitializeComponent();
        DataContextChanged += MeProfileView_DataContextChanged;
        Unloaded += MeProfileView_Unloaded;
    }

    private void MeProfileView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        _viewModel = e.NewValue as MainViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void MeProfileView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _viewModel = null;
        }
    }

    private void SecretInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingSecretInputs || _viewModel is null || sender is not PasswordBox passwordBox)
        {
            return;
        }

        switch (passwordBox.Name)
        {
            case nameof(NewMemberPasswordBox):
                _viewModel.NewMemberPassword = passwordBox.Password;
                break;
            case nameof(NewMemberPinBox):
                _viewModel.NewMemberPin = passwordBox.Password;
                break;
            case nameof(ResetMemberPasswordBox):
                _viewModel.ResetMemberPasswordInput = passwordBox.Password;
                break;
            case nameof(ResetMemberPinBox):
                _viewModel.ResetMemberPinInput = passwordBox.Password;
                break;
            case nameof(CurrentMasterPasswordBox):
                _viewModel.CurrentMasterPasswordInput = passwordBox.Password;
                break;
            case nameof(NewCurrentMasterPasswordBox):
                _viewModel.NewCurrentMasterPasswordInput = passwordBox.Password;
                break;
            case nameof(CurrentPinBox):
                _viewModel.CurrentPinInput = passwordBox.Password;
                break;
            case nameof(NewCurrentPinBox):
                _viewModel.NewCurrentPinInput = passwordBox.Password;
                break;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is null || string.IsNullOrWhiteSpace(e.PropertyName))
        {
            return;
        }

        _syncingSecretInputs = true;
        try
        {
            ClearPasswordBoxWhenViewModelValueIsEmpty(NewMemberPasswordBox, e.PropertyName, nameof(_viewModel.NewMemberPassword), _viewModel.NewMemberPassword);
            ClearPasswordBoxWhenViewModelValueIsEmpty(NewMemberPinBox, e.PropertyName, nameof(_viewModel.NewMemberPin), _viewModel.NewMemberPin);
            ClearPasswordBoxWhenViewModelValueIsEmpty(ResetMemberPasswordBox, e.PropertyName, nameof(_viewModel.ResetMemberPasswordInput), _viewModel.ResetMemberPasswordInput);
            ClearPasswordBoxWhenViewModelValueIsEmpty(ResetMemberPinBox, e.PropertyName, nameof(_viewModel.ResetMemberPinInput), _viewModel.ResetMemberPinInput);
            ClearPasswordBoxWhenViewModelValueIsEmpty(CurrentMasterPasswordBox, e.PropertyName, nameof(_viewModel.CurrentMasterPasswordInput), _viewModel.CurrentMasterPasswordInput);
            ClearPasswordBoxWhenViewModelValueIsEmpty(NewCurrentMasterPasswordBox, e.PropertyName, nameof(_viewModel.NewCurrentMasterPasswordInput), _viewModel.NewCurrentMasterPasswordInput);
            ClearPasswordBoxWhenViewModelValueIsEmpty(CurrentPinBox, e.PropertyName, nameof(_viewModel.CurrentPinInput), _viewModel.CurrentPinInput);
            ClearPasswordBoxWhenViewModelValueIsEmpty(NewCurrentPinBox, e.PropertyName, nameof(_viewModel.NewCurrentPinInput), _viewModel.NewCurrentPinInput);
        }
        finally
        {
            _syncingSecretInputs = false;
        }
    }

    private static void ClearPasswordBoxWhenViewModelValueIsEmpty(
        PasswordBox passwordBox,
        string changedPropertyName,
        string targetPropertyName,
        string viewModelValue)
    {
        if (string.Equals(changedPropertyName, targetPropertyName, StringComparison.Ordinal)
            && string.IsNullOrEmpty(viewModelValue)
            && passwordBox.Password.Length > 0)
        {
            passwordBox.Password = string.Empty;
        }
    }
}
