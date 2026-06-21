using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.IO;
using Microsoft.Win32;
using Orderly.App.Session;
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
    private const int GlobalSearchHotkeyId = 1003;
    private const int TodayWorkbenchHotkeyId = 1004;
    private const int CopyOrderSummaryHotkeyId = 1005;
    private const int OpenProductionSheetHotkeyId = 1006;
    private const int MarkOrderExceptionHotkeyId = 1007;
    private const int AdvanceFulfillmentHotkeyId = 1008;
    private const int OpenCustomerProfileHotkeyId = 1009;
    private const int NewCustomerNoteHotkeyId = 1010;
    private const int CopyCustomerPreferenceSummaryHotkeyId = 1011;
    private const string QaSessionAccountId = "qa-local-account";
    private const string QaSessionUsername = "qa-local-user";
    private const string QaSessionDisplayName = "QA Local User";
    private const int QaSessionDataKeyLength = 32;
    private const string QaSessionKeyFilePrefix = "qa-session-data-key-";
    private const string QaSessionProtectedKeyFileExtension = ".dpapi";
    private const string QaSessionLegacyRawKeyFileExtension = ".key";
    private const string QaSessionProtectedEntropyPurpose = "Orderly.QaSessionDataKey.v1";
    private const int MaxProtectedQaSessionDataKeyBytes = 4096;
    private const string PrivilegedQaStartupEnvName = "ORDERLY_ENABLE_PRIVILEGED_QA_STARTUP";
    private const string QaDataRootEnvName = "ORDERLY_QA_DATA_ROOT";

    private TrayIconService? _trayIconService;
    private GlobalHotkeyService? _hotkeyService;
    private MainWindow? _mainWindow;
    private FloatingWindow? _floatingWindow;
    private MainViewModel? _mainViewModel;
    private FloatingWindowViewModel? _floatingViewModel;
    private bool _isLoginCompleted;
    private bool _isHotkeyAttached;
    private readonly Dictionary<int, string> _registeredHotkeys = new();
    private string[] _startupArgs = [];
    private SqliteConnectionFactory? _connectionFactory;
    private LauncherConnectionFactory? _launcherConnectionFactory;
    private ILocalAuthService? _localAuthService;
    private ILocalAccountManagementService? _localAccountManagementService;
    private ISessionContextService? _sessionContextService;
    private ISessionLockService? _sessionLockService;
    private IFieldEncryptionService? _fieldEncryptionService;
    // 任务 21.1：共享安全审计服务（带会话加密库 provider）与账号仓储引用，供认证/账户服务、
    // MeProfileViewModel、凭证修改会话转移协调器、Owner 紧急启用服务复用同一实例 / 同一会话上下文。
    private ISecurityAuditService? _securityAuditService;
    private ILocalAccountRepository? _localAccountRepository;
    private LoginView? _loginView;
    private string? _databasePath;
    private string? _preparedDatabasePath;
    private bool _isPinUnlockDialogOpen;
    private bool _deferPinUnlockUntilMainWindowOpen;
    private QaDataMaintenanceService.QaDataMaintenanceCommand _qaMaintenanceCommand;

    // 任务 9.8：最小化到托盘时锁定的可选「空闲时限」策略。默认 TimeSpan.Zero = 立即锁定。
    // 大于零时启用「最小化后经过空闲时限再锁定」，到时由 _minimizeToTrayIdleLockTimer 触发锁定。
    private TimeSpan _minimizeToTrayIdleLockDelay = TimeSpan.Zero;
    private System.Windows.Threading.DispatcherTimer? _minimizeToTrayIdleLockTimer;

    public bool IsExiting { get; private set; }
    public bool IsSwitchingSession { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Orderly.App.Helpers.ThemeHelper.Initialize();
        _startupArgs = e.Args;
        QaDataMaintenanceService.TryGetRequestedCommand(_startupArgs, out _qaMaintenanceCommand);
        var isQaMode = QaDataSeeder.IsQaMode(_startupArgs);
        var isQaSeedRequested = QaDataSeeder.IsRequested(_startupArgs);
        var isDemoSeedRequested = DemoDataSeeder.IsRequested(_startupArgs);
        Console.WriteLine("App starting");

        try
        {
            await EnsureIdentityPreparedAsync();
            EnsureAuthServicesPrepared();
            EnsurePrivilegedStartupModesAllowed(
                isQaMode,
                isQaSeedRequested,
                isDemoSeedRequested);

            if (_qaMaintenanceCommand != QaDataMaintenanceService.QaDataMaintenanceCommand.None)
            {
                await RunQaMaintenanceCommandAsync();
                ExitApplication();
                return;
            }

            if (isQaMode)
            {
                var qaDatabasePath = DatabasePaths.GetDefaultDatabasePath(allowQaOverride: true);
                InitializeQaSessionContext(qaDatabasePath);
                await InitializeWorkspaceAsync(qaDatabasePath);
                return;
            }

            if (isQaSeedRequested)
            {
                await PrepareQaSeedDatabaseAsync(DatabasePaths.GetDefaultDatabasePath(allowQaOverride: true));
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

    private void EnsurePrivilegedStartupModesAllowed(
        bool isQaMode,
        bool isQaSeedRequested,
        bool isDemoSeedRequested)
    {
        if (_qaMaintenanceCommand == QaDataMaintenanceService.QaDataMaintenanceCommand.None
            && !isQaMode
            && !isQaSeedRequested
            && !isDemoSeedRequested)
        {
            return;
        }

        if (IsPrivilegedStartupModeAllowed())
        {
            if (!IsPrivilegedQaStartupExplicitlyEnabled())
            {
                throw new InvalidOperationException($"{PrivilegedQaStartupEnvName}=1 未设置，已拒绝 QA / Demo 启动旁路。");
            }

            if (_qaMaintenanceCommand != QaDataMaintenanceService.QaDataMaintenanceCommand.None
                || isQaMode
                || isQaSeedRequested)
            {
                EnsureQaDatabasePathIsIsolated();
            }

            return;
        }

        throw new InvalidOperationException(
            "QA / Demo 启动入口仅允许在 Development、QA、Test 或 Local 环境使用。请先设置 ORDERLY_RUNTIME_ENV 或 DOTNET_ENVIRONMENT。");
    }

    private void EnsurePrivilegedStartupModesAllowed()
    {
        EnsurePrivilegedStartupModesAllowed(
            QaDataSeeder.IsQaMode(_startupArgs),
            QaDataSeeder.IsRequested(_startupArgs),
            DemoDataSeeder.IsRequested(_startupArgs));
    }

    private static bool IsPrivilegedStartupModeAllowed()
    {
        var runtime = (Environment.GetEnvironmentVariable("ORDERLY_RUNTIME_ENV")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? string.Empty).Trim().ToLowerInvariant();

        return runtime is "development" or "dev" or "qa" or "test" or "local";
    }

    private static bool IsPrivilegedQaStartupExplicitlyEnabled()
    {
        var value = Environment.GetEnvironmentVariable(PrivilegedQaStartupEnvName);
        return string.Equals(value?.Trim(), "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureQaDatabasePathIsIsolated()
    {
        var rawQaDatabasePath = Environment.GetEnvironmentVariable("ORDERLY_QA_DB_PATH");
        if (string.IsNullOrWhiteSpace(rawQaDatabasePath))
        {
            throw new InvalidOperationException("QA 启动旁路必须显式设置 ORDERLY_QA_DB_PATH，不能使用默认真实数据库。");
        }

        var qaDatabasePath = Path.GetFullPath(rawQaDatabasePath);
        var qaRootPath = ResolveQaDataRootPath();
        if (!IsPathUnderDirectory(qaDatabasePath, qaRootPath))
        {
            throw new InvalidOperationException($"ORDERLY_QA_DB_PATH 必须位于 {QaDataRootEnvName} 指定的测试数据目录下。");
        }

        LocalDataFileSecurity.EnsureDirectoryExistsAndIsNotLinked(qaRootPath, "QA 测试数据目录");
        var qaDirectory = Path.GetDirectoryName(qaDatabasePath);
        if (!string.IsNullOrWhiteSpace(qaDirectory))
        {
            LocalDataFileSecurity.EnsureDirectoryExistsAndIsNotLinked(qaDirectory, "QA 数据库目录");
        }

        LocalDataFileSecurity.EnsureFileIsNotLinked(qaDatabasePath, "QA 数据库文件");
    }

    private static string ResolveQaDataRootPath()
    {
        var configuredRoot = Environment.GetEnvironmentVariable(QaDataRootEnvName);
        var root = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(DatabasePaths.GetAppRootPath(), "qa")
            : configuredRoot;
        return Path.GetFullPath(root);
    }

    private static bool IsPathUnderDirectory(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path);
        var fullDirectory = Path.GetFullPath(directory);
        var prefix = fullDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? fullDirectory
            : fullDirectory + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private async Task CompleteLoginAsync(LocalSessionContext session)
    {
        if (_isLoginCompleted)
        {
            return;
        }

        _isLoginCompleted = true;
        var currentSession = _sessionContextService?.Current;
        if (currentSession is null
            || !string.Equals(currentSession.AccountId, session.AccountId, StringComparison.Ordinal)
            || _sessionContextService?.IsDataKeyAvailable != true)
        {
            if (session.DataKey.Length != 32)
            {
                throw new InvalidOperationException("Authenticated session data key is unavailable.");
            }

            _sessionContextService?.SetCurrent(session);
            currentSession = _sessionContextService?.Current;
        }

        _sessionLockService?.MarkSignedIn();
        _loginView?.Close();

        try
        {
            await InitializeWorkspaceAsync(currentSession?.DatabasePath ?? session.DatabasePath);
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

    private async void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        // 重新打开窗口即取消尚未到时的空闲锁定（任务 9.8：未到时限不锁定）。
        CancelMinimizeToTrayIdleLock();

        if (PinUnlockPromptPolicy.ShouldRequirePinBeforeShowingMainWindow(
                _sessionLockService?.State ?? SessionLockState.LoggedOut,
                _deferPinUnlockUntilMainWindowOpen))
        {
            await RequirePinUnlockAsync();
            if (_mainWindow is null || _sessionLockService?.State != SessionLockState.Unlocked)
            {
                return;
            }
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

        if (_mainViewModel?.Preferences.FloatingBallEnabled != true)
        {
            _floatingWindow.Hide();
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

    private void ApplyFloatingWindowRuntime(AppPreferences previous, AppPreferences current)
    {
        if (_floatingWindow is null)
        {
            return;
        }

        if (!current.FloatingBallEnabled)
        {
            _floatingWindow.Hide();
            return;
        }

        if (!previous.FloatingBallEnabled && current.FloatingBallEnabled)
        {
            _floatingWindow.Show();
            _floatingWindow.Activate();
        }
    }

    private async void NavigateFromFloatingWindow(string section)
    {
        if (_mainViewModel is null)
        {
            return;
        }

        ShowMainWindow();
        await _mainViewModel.SelectSectionCommand.ExecuteAsync(section);
    }

    private bool TryApplyRuntimeHotkeys(AppPreferences preferences)
    {
        if (_hotkeyService is null || _mainWindow is null || !_isHotkeyAttached)
        {
            return false;
        }

        var previous = new Dictionary<int, string>(_registeredHotkeys);
        var candidates = BuildRuntimeHotkeyMap(preferences);
        foreach (var candidate in candidates)
        {
            if (!_hotkeyService.Register(candidate.Key, candidate.Value))
            {
                foreach (var item in previous)
                {
                    _hotkeyService.Register(item.Key, item.Value);
                }

                _registeredHotkeys.Clear();
                foreach (var item in previous)
                {
                    _registeredHotkeys[item.Key] = item.Value;
                }

                return false;
            }
        }

        _registeredHotkeys.Clear();
        foreach (var candidate in candidates)
        {
            _registeredHotkeys[candidate.Key] = candidate.Value;
        }

        return true;
    }

    private static IReadOnlyDictionary<int, string> BuildRuntimeHotkeyMap(AppPreferences preferences)
    {
        return new Dictionary<int, string>
        {
            [MainHotkeyId] = preferences.MainHotkey,
            [FloatingHotkeyId] = preferences.FloatingHotkey,
            [GlobalSearchHotkeyId] = preferences.GlobalSearchHotkey,
            [TodayWorkbenchHotkeyId] = preferences.TodayWorkbenchHotkey,
            [CopyOrderSummaryHotkeyId] = preferences.CopyOrderSummaryHotkey,
            [OpenProductionSheetHotkeyId] = preferences.OpenProductionSheetHotkey,
            [MarkOrderExceptionHotkeyId] = preferences.MarkOrderExceptionHotkey,
            [AdvanceFulfillmentHotkeyId] = preferences.AdvanceFulfillmentHotkey,
            [OpenCustomerProfileHotkeyId] = preferences.OpenCustomerProfileHotkey,
            [NewCustomerNoteHotkeyId] = preferences.NewCustomerNoteHotkey,
            [CopyCustomerPreferenceSummaryHotkeyId] = preferences.CopyCustomerPreferenceSummaryHotkey
        };
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

    private void ExitApplicationFromTray()
    {
        // 托盘菜单来自 WinForms 通知图标，统一切回 WPF Dispatcher 做真实退出，
        // 并在清理完成后补最终进程终止兜底，避免仅隐藏窗口后进程残留。
        _ = Dispatcher.InvokeAsync(() => ExitApplication(forceProcessTermination: true));
    }

    private void OnMainWindowExitRequested(object? sender, EventArgs e)
    {
        // OnClosing 尚未返回，延后到下一次 Dispatcher 调度再进入统一退出流程，
        // 避免在同一次关闭回调内重入 Window.Close()。
        _ = Dispatcher.InvokeAsync(() => ExitApplication());
    }

    private void ExitApplication(bool forceProcessTermination = false)
    {
        if (IsExiting)
        {
            if (forceProcessTermination)
            {
                Environment.Exit(0);
            }

            return;
        }

        IsExiting = true;
        Orderly.App.Helpers.ThemeHelper.Shutdown();
        if (_sessionLockService is not null)
        {
            _sessionLockService.LockStateChanged -= OnSessionLockStateChanged;
        }

        SystemEvents.PowerModeChanged -= OnPowerModeChanged;

        CancelMinimizeToTrayIdleLock();
        if (_mainWindow is not null)
        {
            _mainViewModel?.PersistWindowBoundsIfNeededAsync(_mainWindow).GetAwaiter().GetResult();
            _mainWindow.HiddenToTray -= OnMainWindowHiddenToTray;
            _mainWindow.ExitRequested -= OnMainWindowExitRequested;
        }

        _loginView?.Close();
        _hotkeyService?.Dispose();
        _trayIconService?.Dispose();
        _floatingWindow?.Close();
        _mainWindow?.Close();
        Shutdown();

        if (forceProcessTermination)
        {
            Environment.Exit(0);
        }
    }
}
