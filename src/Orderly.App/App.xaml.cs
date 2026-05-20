using System.Net.Http;
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
                await InitializeWorkspaceAsync(DatabasePaths.GetDefaultDatabasePath());
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

    private async Task EnsureIdentityPreparedAsync()
    {
        if (_launcherConnectionFactory is not null)
        {
            return;
        }

        var launcherPath = DatabasePaths.GetLauncherDatabasePath();
        _launcherConnectionFactory = new LauncherConnectionFactory(launcherPath);
        var launcherInitializer = new LauncherDatabaseInitializer(_launcherConnectionFactory);
        await launcherInitializer.InitializeAsync();
    }

    private void EnsureAuthServicesPrepared()
    {
        if (_localAuthService is not null
            && _localAccountManagementService is not null
            && _sessionContextService is not null
            && _sessionLockService is not null
            && _fieldEncryptionService is not null)
        {
            return;
        }

        var launcherConnectionFactory = _launcherConnectionFactory ?? throw new InvalidOperationException("Launcher connection factory is not initialized.");
        var accountRepository = new LocalAccountRepository(launcherConnectionFactory);
        var legacyMigrationService = new LegacyDatabaseMigrationService();

        _sessionContextService = new SessionContextService();
        _sessionLockService = new SessionLockService();
        _fieldEncryptionService = new FieldEncryptionService(_sessionContextService);
        _localAuthService = new LocalAuthService(accountRepository, legacyMigrationService, _sessionContextService);
        _localAccountManagementService = new LocalAccountManagementService(accountRepository, _sessionContextService);

        _sessionLockService.LockStateChanged += OnSessionLockStateChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
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

    private async Task InitializeWorkspaceAsync(string databasePath)
    {
        databasePath = await EnsureDatabasePreparedAsync(databasePath);
        var connectionFactory = _connectionFactory ?? throw new InvalidOperationException("Database connection factory is not initialized.");
        var fieldEncryptionService = _fieldEncryptionService ?? throw new InvalidOperationException("Field encryption service is not initialized.");

        if (_sessionContextService?.IsSignedIn == true)
        {
            var sensitiveMigrationService = new SensitiveFieldMigrationService(connectionFactory, fieldEncryptionService);
            await sensitiveMigrationService.BackfillAsync();
        }

        ICustomerRepository customerRepository = new CustomerRepository(connectionFactory, fieldEncryptionService);
        IOrderRepository orderRepository = new OrderRepository(connectionFactory, fieldEncryptionService);
        IDealRepository dealRepository = new DealRepository(connectionFactory, fieldEncryptionService);
        IFollowUpRepository followUpRepository = new FollowUpRepository(connectionFactory, fieldEncryptionService);
        ICustomerNoteRepository noteRepository = new CustomerNoteRepository(connectionFactory, fieldEncryptionService);
        IConversationMessageRepository conversationMessageRepository = new ConversationMessageRepository(connectionFactory, fieldEncryptionService);
        IOcrResultRepository ocrResultRepository = new OcrResultRepository(connectionFactory, fieldEncryptionService);
        IAiSuggestionRepository aiSuggestionRepository = new AiSuggestionRepository(connectionFactory, fieldEncryptionService);
        IActivityLogRepository activityLogRepository = new ActivityLogRepository(connectionFactory, fieldEncryptionService);
        ISyncRecordRepository syncRecordRepository = new SyncRecordRepository(connectionFactory);
        IPriceAdjustmentRepository priceAdjustmentRepository = new PriceAdjustmentRepository(connectionFactory, fieldEncryptionService);
        IReplyTemplateRepository replyTemplateRepository = new ReplyTemplateRepository(connectionFactory, fieldEncryptionService);
        IAppSettingRepository settingRepository = new AppSettingRepository(connectionFactory);
        IClipboardService clipboardService = new DesktopClipboardService();
        ISyncService syncService = new LocalSyncService(syncRecordRepository, activityLogRepository);
        var aiProviderOptions = AiProviderOptions.FromEnvironment();
        IAiSuggestionProvider localAiSuggestionProvider = new LocalAiSuggestionProvider();
        var primaryAiSuggestionProvider = AiSuggestionProviderFactory.CreatePrimaryProvider(aiProviderOptions, localAiSuggestionProvider);

        ICustomerService customerService = new CustomerService(customerRepository, activityLogRepository);
        IOrderService orderService = new OrderService(orderRepository, activityLogRepository);
        IDealService dealService = new DealService(dealRepository, activityLogRepository);
        IFollowUpService followUpService = new FollowUpService(followUpRepository, activityLogRepository);
        INoteService noteService = new NoteService(noteRepository, activityLogRepository);
        IConversationService conversationService = new ConversationService(conversationMessageRepository, activityLogRepository);
        IOcrService ocrService = new LocalOcrService(ocrResultRepository, activityLogRepository, conversationService, conversationMessageRepository);
        IAiAssistantService aiAssistantService = new LocalAiAssistantService(
            customerRepository,
            orderRepository,
            conversationMessageRepository,
            aiSuggestionRepository,
            activityLogRepository,
            primaryAiSuggestionProvider,
            localAiSuggestionProvider,
            aiProviderOptions);
        IAutoReplyService autoReplyService = new LocalAutoReplyService(aiSuggestionRepository, orderRepository, activityLogRepository, clipboardService);
        IActivityLogService activityLogService = new ActivityLogService(activityLogRepository);
        IPipelineStageResolver pipelineStageResolver = new PipelineStageResolver(
            customerRepository,
            orderRepository,
            dealRepository,
            conversationMessageRepository,
            aiSuggestionRepository,
            followUpRepository,
            activityLogRepository,
            priceAdjustmentRepository);
        IWorkbenchTaskService workbenchTaskService = new LocalWorkbenchTaskService(
            customerRepository,
            orderRepository,
            dealRepository,
            followUpRepository,
            conversationMessageRepository,
            aiSuggestionRepository,
            ocrResultRepository,
            activityLogRepository,
            priceAdjustmentRepository);
        IGlobalSearchService globalSearchService = new LocalGlobalSearchService(
            customerRepository,
            orderRepository,
            dealRepository,
            followUpRepository,
            conversationMessageRepository,
            aiSuggestionRepository,
            ocrResultRepository,
            activityLogRepository,
            priceAdjustmentRepository);
        INavigationRouteService navigationRouteService = new LocalNavigationRouteService(
            customerRepository,
            orderRepository,
            conversationMessageRepository,
            aiSuggestionRepository,
            ocrResultRepository,
            followUpRepository,
            activityLogRepository);
        IBackupService backupService = new LocalBackupService(
            connectionFactory,
            syncService,
            syncRecordRepository,
            activityLogRepository,
            _launcherConnectionFactory,
            _sessionContextService);
        IPriceAdjustmentService priceAdjustmentService = new PriceAdjustmentService(priceAdjustmentRepository, activityLogRepository);
        var stringNarrationGatewayOptions = StringNarrationGatewayOptions.FromEnvironment();
        var stringNarrationHttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(stringNarrationGatewayOptions.TimeoutSeconds)
        };
        IStringNarrationOrderService stringNarrationOrderService = new StringNarrationGatewayOrderService(
            new StringNarrationGatewayClient(stringNarrationHttpClient, stringNarrationGatewayOptions));

        var preferences = await settingRepository.GetPreferencesAsync();

        _mainViewModel = new MainViewModel(
            customerRepository,
            orderRepository,
            customerService,
            orderService,
            dealService,
            followUpService,
            noteService,
            conversationService,
            ocrService,
            aiAssistantService,
            autoReplyService,
            activityLogService,
            workbenchTaskService,
            globalSearchService,
            navigationRouteService,
            backupService,
            priceAdjustmentService,
            stringNarrationOrderService,
            replyTemplateRepository,
            settingRepository,
            syncService,
            syncRecordRepository,
            clipboardService,
            databasePath,
            _localAccountManagementService,
            _sessionContextService,
            stringNarrationGatewayOptions.Endpoint,
            stringNarrationGatewayOptions.HasToken,
            stringNarrationGatewayOptions.TimeoutSeconds);
        _mainViewModel.LockSessionRequested += HandleLockSessionRequested;
        _mainViewModel.LogoutRequested += HandleLogoutRequested;
        await _mainViewModel.LoadAsync();

        _floatingViewModel = new FloatingWindowViewModel(orderRepository, replyTemplateRepository, clipboardService);
        await _floatingViewModel.LoadAsync();

        _mainWindow = new MainWindow(_mainViewModel);
        MainWindow = _mainWindow;
        _floatingWindow = new FloatingWindow(_floatingViewModel);

        _trayIconService = new TrayIconService();
        _trayIconService.OpenMainRequested += (_, _) => ShowMainWindow();
        _trayIconService.ToggleFloatingRequested += (_, _) => ToggleFloatingWindow();
        _trayIconService.ExitRequested += (_, _) => ExitApplication();

        _hotkeyService = new GlobalHotkeyService();
        _hotkeyService.HotkeyPressed += (_, hotkey) =>
        {
            if (string.Equals(hotkey, _registeredMainHotkey, StringComparison.OrdinalIgnoreCase))
            {
                ShowMainWindow();
            }
            else if (string.Equals(hotkey, _registeredFloatingHotkey, StringComparison.OrdinalIgnoreCase))
            {
                ToggleFloatingWindow();
            }
        };
        _mainViewModel.ConfigureSettingsRuntimeHooks(
            TryApplyRuntimeHotkeys,
            TrySendDesktopNotification);

        _mainWindow.SourceInitialized += (_, _) =>
        {
            _hotkeyService.Attach(_mainWindow);
            _isHotkeyAttached = true;
            _ = TryApplyRuntimeHotkeys(preferences.MainHotkey, preferences.FloatingHotkey);
        };

        Console.WriteLine("MainWindow showing");
        var startMinimizedToTray = preferences.StartMinimizedToTray;
        _mainWindow.WindowState = startMinimizedToTray ? WindowState.Minimized : WindowState.Normal;
        _mainWindow.ShowInTaskbar = !startMinimizedToTray;
        _mainWindow.Opacity = 1;
        _mainWindow.Show();
        if (startMinimizedToTray)
        {
            _mainWindow.Hide();
        }
        else
        {
            _mainWindow.Activate();
        }

        if (preferences.ShowFloatingWindowOnStartup)
        {
            _floatingWindow.Show();
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

    private async Task RunQaMaintenanceCommandAsync()
    {
        await EnsureDatabasePreparedAsync(DatabasePaths.GetDefaultDatabasePath());

        var connectionFactory = _connectionFactory ?? throw new InvalidOperationException("Database connection factory is not initialized.");
        var maintenanceService = new QaDataMaintenanceService(connectionFactory);

        object result = _qaMaintenanceCommand switch
        {
            QaDataMaintenanceService.QaDataMaintenanceCommand.Status => await maintenanceService.GetStatusAsync(),
            QaDataMaintenanceService.QaDataMaintenanceCommand.Clear => await maintenanceService.ClearAsync(),
            QaDataMaintenanceService.QaDataMaintenanceCommand.Reset => await maintenanceService.ResetAsync(),
            _ => throw new InvalidOperationException("Unsupported QA data maintenance command.")
        };

        Console.WriteLine(result);
    }

    private async Task<string> EnsureDatabasePreparedAsync(string databasePath)
    {
        if (_connectionFactory is not null
            && !string.IsNullOrWhiteSpace(_preparedDatabasePath)
            && string.Equals(_preparedDatabasePath, databasePath, StringComparison.OrdinalIgnoreCase))
        {
            return _preparedDatabasePath;
        }

        _databasePath = databasePath;
        _connectionFactory = new SqliteConnectionFactory(_databasePath);

        var initializer = new DatabaseInitializer(_connectionFactory);
        await initializer.InitializeAsync();
        Console.WriteLine("Database initialized");

        if (DemoDataSeeder.IsRequested(_startupArgs))
        {
            var demoDataSeeder = new DemoDataSeeder(_connectionFactory);
            await demoDataSeeder.SeedIfNeededAsync();
            Console.WriteLine("Demo data seeding checked");
        }

        if (QaDataSeeder.IsRequested(_startupArgs))
        {
            var qaDataSeeder = new QaDataSeeder(_connectionFactory);
            var qaSeedResult = await qaDataSeeder.SeedIfNeededAsync();
            Console.WriteLine($"QA data seeding checked: {qaSeedResult}");
        }

        _preparedDatabasePath = _databasePath;
        return _databasePath;
    }

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
                    _sessionLockService?.UnlockWithPin();
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
