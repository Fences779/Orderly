using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using Orderly.App.Session;
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
            PrepareDeferredPinUnlock();
            _sessionLockService?.LockBySystemResume();
        }
    }

    private void OnSessionLockStateChanged(object? sender, SessionLockState state)
    {
        var action = PinUnlockPromptPolicy.EvaluateOnLock(state, _deferPinUnlockUntilMainWindowOpen);
        if (action == PinUnlockPromptAction.None)
        {
            if (state == SessionLockState.Unlocked && _mainWindow is not null)
            {
                _mainWindow.IsEnabled = true;
            }

            _deferPinUnlockUntilMainWindowOpen = false;
            return;
        }

        if (action == PinUnlockPromptAction.DeferUntilMainWindowOpen)
        {
            HideMainWindowUntilPinRequested();
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
        var windowsHelloService = _windowsHelloService;
        var session = _sessionContextService?.Current;
        if (localAuthService is null || session is null)
        {
            return;
        }

        _isPinUnlockDialogOpen = true;
        _deferPinUnlockUntilMainWindowOpen = false;
        try
        {
            if (_mainWindow is { IsVisible: true } visibleMainWindow)
            {
                visibleMainWindow.IsEnabled = false;
            }

            while (_sessionLockService?.IsPinRequired == true)
            {
                session = _sessionContextService?.Current;
                if (session is null)
                {
                    break;
                }

                var isWindowsHelloAvailable = windowsHelloService is not null
                    && await windowsHelloService.IsAvailableAsync();
                var dialog = new PinUnlockView(session.DisplayName, session.Username, isWindowsHelloAvailable);
                if (_mainWindow is { IsVisible: true } mainWindow)
                {
                    dialog.Owner = mainWindow;
                }

                var result = dialog.ShowDialog();
                if (result != true)
                {
                    await LogoutToLoginAsync();
                    return;
                }

                if (dialog.UnlockMethod == PinUnlockMethod.WindowsHello)
                {
                    if (windowsHelloService is null
                        || !await windowsHelloService.VerifyAsync($"验证后解锁 Orderly 账号 {session.Username}"))
                    {
                        ShowPinUnlockMessage("Windows Hello 验证未通过或已取消。");
                        continue;
                    }

                    if (_sessionContextService?.TryRestoreDataKey(session.AccountId) == true)
                    {
                        _sessionLockService?.UnlockWithPin(verified: true);
                        break;
                    }

                    ShowPinUnlockMessage("会话密钥恢复失败，请切换账号后重新登录。");
                    continue;
                }

                var verified = await localAuthService.VerifyPinAsync(session.AccountId, dialog.EnteredPin);
                if (verified)
                {
                    _sessionLockService?.UnlockWithPin(verified);
                    break;
                }

                ShowPinUnlockMessage("PIN 错误，请重试。");
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

    private void ShowPinUnlockMessage(string message)
    {
        if (_mainWindow is { IsVisible: true } owner)
        {
            System.Windows.MessageBox.Show(
                owner,
                message,
                "Orderly",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        System.Windows.MessageBox.Show(
            message,
            "Orderly",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private async Task LogoutToLoginAsync()
    {
        _sessionLockService?.Logout();
        _sessionContextService?.Clear();

        await TeardownWorkspaceAsync();
        _isLoginCompleted = false;

        await Dispatcher.InvokeAsync(ShowLoginView);
    }

    private async Task TeardownWorkspaceAsync()
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

            CancelMinimizeToTrayIdleLock();
            if (_mainWindow is not null)
            {
                if (_mainViewModel is not null)
                {
                    await _mainViewModel.PersistWindowBoundsIfNeededAsync(_mainWindow);
                }

                _mainWindow.HiddenToTray -= OnMainWindowHiddenToTray;
            }

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

    /// <summary>
    /// 任务 9.8：主窗口最小化到托盘时的应用级会话锁定触发点（需求 18.1/18.2/13.3）。
    /// 依 <see cref="TrayLockTriggerPolicy"/> 决策：默认立即锁定，配置了空闲时限则延时锁定。
    /// 锁定统一复用既有 <see cref="ISessionLockService.LockManually"/> 进入 PendingPinUnlock，
    /// 不改动既有解锁交互。
    /// </summary>
    private void OnMainWindowHiddenToTray(object? sender, EventArgs e)
    {
        var action = TrayLockTriggerPolicy.Evaluate(
            _sessionContextService?.IsSignedIn == true,
            _sessionLockService?.State ?? SessionLockState.LoggedOut,
            _minimizeToTrayIdleLockDelay);

        switch (action)
        {
            case TrayLockAction.LockImmediately:
                CancelMinimizeToTrayIdleLock();
                PrepareDeferredPinUnlock();
                _sessionLockService?.LockManually();
                break;

            case TrayLockAction.LockAfterIdleDelay:
                StartMinimizeToTrayIdleLock();
                break;

            case TrayLockAction.None:
            default:
                break;
        }
    }

    private void StartMinimizeToTrayIdleLock()
    {
        CancelMinimizeToTrayIdleLock();

        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = _minimizeToTrayIdleLockDelay,
        };
        timer.Tick += OnMinimizeToTrayIdleLockElapsed;
        _minimizeToTrayIdleLockTimer = timer;
        timer.Start();
    }

    private void OnMinimizeToTrayIdleLockElapsed(object? sender, EventArgs e)
    {
        CancelMinimizeToTrayIdleLock();

        // 到时仅在仍处于已登录且未被解除锁定时锁定（用户可能已在期间重新打开窗口）。
        if (_sessionContextService?.IsSignedIn == true
            && _sessionLockService?.State == SessionLockState.Unlocked)
        {
            PrepareDeferredPinUnlock();
            _sessionLockService.LockManually();
        }
    }

    /// <summary>
    /// 取消尚未到时的「空闲时限」锁定计时器。在主窗口被重新打开（<see cref="ShowMainWindow"/>）
    /// 或立即锁定时调用，确保「未到时限不锁定」。
    /// </summary>
    private void CancelMinimizeToTrayIdleLock()
    {
        if (_minimizeToTrayIdleLockTimer is null)
        {
            return;
        }

        _minimizeToTrayIdleLockTimer.Stop();
        _minimizeToTrayIdleLockTimer.Tick -= OnMinimizeToTrayIdleLockElapsed;
        _minimizeToTrayIdleLockTimer = null;
    }

    private void HandleLogoutRequested()
    {
        _ = LogoutToLoginAsync();
    }

    private void PrepareDeferredPinUnlock()
    {
        _deferPinUnlockUntilMainWindowOpen = true;
    }

    private void HideMainWindowUntilPinRequested()
    {
        if (_mainWindow is null || !_mainWindow.IsVisible)
        {
            return;
        }

        _mainWindow.IsEnabled = true;
        _mainWindow.Hide();
    }
}
