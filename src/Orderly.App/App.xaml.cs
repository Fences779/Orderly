using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using Orderly.Core.Models;
using Orderly.App.ViewModels;
using Orderly.App.Views;
using Orderly.Core.Repositories;
using Orderly.Core.Services;
using Orderly.Data.Repositories;
using Orderly.Data.Services;
using Orderly.Data.Sqlite;
using Orderly.Infrastructure.Hotkeys;
using Orderly.Infrastructure.Services;
using Orderly.Infrastructure.Tray;

namespace Orderly.App;

public partial class App : System.Windows.Application
{
    private const int MainHotkeyId = 1001;
    private const int FloatingHotkeyId = 1002;
    private const string QaSessionAccountId = "qa-local-account";
    private const string QaSessionUsername = "qa-local-user";
    private const string QaSessionDisplayName = "QA Local User";
    private const string QaSessionDataKeySeed = "Orderly-QA-Encryption-v1";

    private TrayIconService? _trayIconService;
    private GlobalHotkeyService? _hotkeyService;
    private MainWindow? _mainWindow;
    private FloatingWindow? _floatingWindow;
    private MainViewModel? _mainViewModel;
    private FloatingWindowViewModel? _floatingViewModel;
    private bool _isLoginCompleted;
    private bool _isHotkeyAttached;
    private string _registeredMainHotkey = "Ctrl+Alt+O";
    private string _registeredFloatingHotkey = "Ctrl+Alt+R";
    private string[] _startupArgs = [];
    private SqliteConnectionFactory? _connectionFactory;
    private LauncherConnectionFactory? _launcherConnectionFactory;
    private ILocalAuthService? _localAuthService;
    private ILocalAccountManagementService? _localAccountManagementService;
    private ISessionContextService? _sessionContextService;
    private ISessionLockService? _sessionLockService;
    private IFieldEncryptionService? _fieldEncryptionService;
    private LoginView? _loginView;
    private string? _databasePath;
    private string? _preparedDatabasePath;
    private bool _isPinUnlockDialogOpen;
    private QaDataMaintenanceService.QaDataMaintenanceCommand _qaMaintenanceCommand;

    public bool IsExiting { get; private set; }
    public bool IsSwitchingSession { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _startupArgs = e.Args;
        QaDataMaintenanceService.TryGetRequestedCommand(_startupArgs, out _qaMaintenanceCommand);
        var isQaMode = QaDataSeeder.IsQaMode(_startupArgs);
        Console.WriteLine("App starting");

        try
        {
            await EnsureIdentityPreparedAsync();
            EnsureAuthServicesPrepared();

            if (_qaMaintenanceCommand != QaDataMaintenanceService.QaDataMaintenanceCommand.None)
            {
                await RunQaMaintenanceCommandAsync();
                ExitApplication();
                return;
            }

            if (QaDataSeeder.IsRequested(_startupArgs))
            {
                await EnsureDatabasePreparedAsync(DatabasePaths.GetDefaultDatabasePath());
            }

            if (QaDataSeeder.IsQaMode(_startupArgs))
            {
                var qaDatabasePath = DatabasePaths.GetDefaultDatabasePath();
                InitializeQaSessionContext(qaDatabasePath);
                await InitializeWorkspaceAsync(qaDatabasePath);
                return;
            }
        }
        catch (Exception ex)
        {
            if (_qaMaintenanceCommand != QaDataMaintenanceService.QaDataMaintenanceCommand.None)
            {
                Environment.ExitCode = 1;
                Console.Error.WriteLine($"QA data maintenance failed: {ex.Message}");
            }
            else
            {
                System.Windows.MessageBox.Show(
                    $"启动失败：{ex.Message}",
                    "Orderly",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            ExitApplication();
            return;
        }

        ShowLoginView();
    }

    private async Task CompleteLoginAsync(LocalSessionContext session)
    {
        if (_isLoginCompleted)
        {
            return;
        }

        _isLoginCompleted = true;
        _sessionContextService?.SetCurrent(session);
        _sessionLockService?.MarkSignedIn();
        _loginView?.Close();

        try
        {
            await InitializeWorkspaceAsync(session.DatabasePath);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"启动工作台失败：{ex.Message}",
                "Orderly",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            ExitApplication();
        }
    }

    private void ShowLoginView()
    {
        var localAuthService = _localAuthService ?? throw new InvalidOperationException("Local auth service is not initialized.");
        var localAccountManagementService = _localAccountManagementService ?? throw new InvalidOperationException("Local account management service is not initialized.");
        var loginViewModel = new LoginViewModel(localAuthService, localAccountManagementService);
        _loginView = new LoginView(loginViewModel);

        loginViewModel.LoginSucceeded += session => _ = CompleteLoginAsync(session);
        _loginView.Closed += (_, _) =>
        {
            if (!_isLoginCompleted && !IsExiting)
            {
                ExitApplication();
            }
        };

        Console.WriteLine("LoginView showing");
        _loginView.WindowState = WindowState.Normal;
        _loginView.ShowInTaskbar = true;
        _loginView.Opacity = 1;
        _loginView.Show();
        _loginView.Activate();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void ToggleFloatingWindow()
    {
        if (_floatingWindow is null)
        {
            return;
        }

        if (_floatingWindow.IsVisible)
        {
            _floatingWindow.Hide();
        }
        else
        {
            _floatingWindow.Show();
            _floatingWindow.Activate();
        }
    }

    private bool TryApplyRuntimeHotkeys(string mainHotkey, string floatingHotkey)
    {
        if (_hotkeyService is null || _mainWindow is null || !_isHotkeyAttached)
        {
            return false;
        }

        var mainOk = _hotkeyService.Register(MainHotkeyId, mainHotkey);
        var floatingOk = _hotkeyService.Register(FloatingHotkeyId, floatingHotkey);
        if (mainOk && floatingOk)
        {
            _registeredMainHotkey = mainHotkey;
            _registeredFloatingHotkey = floatingHotkey;
            return true;
        }

        _hotkeyService.Register(MainHotkeyId, _registeredMainHotkey);
        _hotkeyService.Register(FloatingHotkeyId, _registeredFloatingHotkey);
        return false;
    }

    private bool TrySendDesktopNotification(string title, string message)
    {
        if (_trayIconService is null || !_trayIconService.CanShowNotifications)
        {
            return false;
        }

        _trayIconService.ShowInfo(title, message);
        return true;
    }

    private void ExitApplication()
    {
        IsExiting = true;
        if (_sessionLockService is not null)
        {
            _sessionLockService.LockStateChanged -= OnSessionLockStateChanged;
        }

        SystemEvents.PowerModeChanged -= OnPowerModeChanged;

        _loginView?.Close();
        _hotkeyService?.Dispose();
        _trayIconService?.Dispose();
        _floatingWindow?.Close();
        _mainWindow?.Close();
        Shutdown();
    }
}
