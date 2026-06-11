using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using Orderly.App.ViewModels;
using Orderly.App.Views;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;
using Orderly.Data.Repositories;
using Orderly.Data.Services;
using Orderly.Data.Sqlite;
using Orderly.Infrastructure.Hotkeys;
using Orderly.Infrastructure.Services;
using Orderly.Infrastructure.Tray;

namespace Orderly.App;

public partial class App
{
    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume)
        {
            return;
        }

        if (_sessionContextService?.IsSignedIn == true)
        {
            _sessionLockService?.LockBySystemResume();
        }
    }

    private void OnSessionLockStateChanged(object? sender, SessionLockState state)
    {
        if (state != SessionLockState.PendingPinUnlock)
        {
            if (state == SessionLockState.Unlocked && _mainWindow is not null)
            {
                _mainWindow.IsEnabled = true;
            }

            return;
        }

        _ = Dispatcher.InvokeAsync(RequirePinUnlockAsync);
    }

    private async Task RequirePinUnlockAsync()
    {
        if (_isPinUnlockDialogOpen)
        {
            return;
        }

        var localAuthService = _localAuthService;
        var session = _sessionContextService?.Current;
        if (localAuthService is null || session is null)
        {
            return;
        }

        _isPinUnlockDialogOpen = true;
        try
        {
            if (_mainWindow is not null)
            {
                _mainWindow.IsEnabled = false;
            }

            while (_sessionLockService?.IsPinRequired == true)
            {
                session = _sessionContextService?.Current;
                if (session is null)
                {
                    break;
                }

                var dialog = new PinUnlockView(session.DisplayName, session.Username)
                {
                    Owner = _mainWindow
                };

                var result = dialog.ShowDialog();
                if (result != true)
                {
                    await LogoutToLoginAsync();
                    return;
                }

                var verified = await localAuthService.VerifyPinAsync(session.AccountId, dialog.EnteredPin);
                if (verified)
                {
                    _sessionLockService?.UnlockWithPin(verified);
                    break;
                }

                System.Windows.MessageBox.Show(
                    _mainWindow,
                    "PIN 错误，请重试。",
                    "Orderly",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            _isPinUnlockDialogOpen = false;
            if (_sessionLockService?.State == SessionLockState.Unlocked && _mainWindow is not null)
            {
                _mainWindow.IsEnabled = true;
            }
        }
    }

    private async Task LogoutToLoginAsync()
    {
        _sessionLockService?.Logout();
        _sessionContextService?.Clear();

        TeardownWorkspace();
        _isLoginCompleted = false;

        await Dispatcher.InvokeAsync(ShowLoginView);
    }

    private void TeardownWorkspace()
    {
        IsSwitchingSession = true;
        try
        {
            if (_mainViewModel is not null)
            {
                _mainViewModel.LockSessionRequested -= HandleLockSessionRequested;
                _mainViewModel.LogoutRequested -= HandleLogoutRequested;
            }

            _hotkeyService?.Dispose();
            _hotkeyService = null;
            _isHotkeyAttached = false;

            _trayIconService?.Dispose();
            _trayIconService = null;

            _floatingWindow?.Close();
            _floatingWindow = null;
            _floatingViewModel = null;

            _mainWindow?.Close();
            _mainWindow = null;
            _mainViewModel = null;
            MainWindow = null;

            _connectionFactory = null;
            _databasePath = null;
            _preparedDatabasePath = null;
        }
        finally
        {
            IsSwitchingSession = false;
        }
    }

    private void HandleLockSessionRequested()
    {
        _sessionLockService?.LockManually();
    }

    private void HandleLogoutRequested()
    {
        _ = LogoutToLoginAsync();
    }
}
