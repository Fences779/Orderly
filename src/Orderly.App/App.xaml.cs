using System.Windows;
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
    private string[] _startupArgs = [];
    private SqliteConnectionFactory? _connectionFactory;
    private string? _databasePath;
    private bool _startupDataPrepared;
    private QaDataMaintenanceService.QaDataMaintenanceCommand _qaMaintenanceCommand;

    public bool IsExiting { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _startupArgs = e.Args;
        QaDataMaintenanceService.TryGetRequestedCommand(_startupArgs, out _qaMaintenanceCommand);
        Console.WriteLine("App starting");

        try
        {
            if (_qaMaintenanceCommand != QaDataMaintenanceService.QaDataMaintenanceCommand.None)
            {
                await RunQaMaintenanceCommandAsync();
                ExitApplication();
                return;
            }

            if (QaDataSeeder.IsRequested(_startupArgs))
            {
                await EnsureDatabasePreparedAsync();
            }

            if (QaDataSeeder.IsQaMode(_startupArgs))
            {
                await InitializeWorkspaceAsync();
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

    private async Task CompleteLoginAsync(LoginView loginView)
    {
        if (_isLoginCompleted)
        {
            return;
        }

        _isLoginCompleted = true;
        loginView.Close();

        try
        {
            await InitializeWorkspaceAsync();
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

    private async Task InitializeWorkspaceAsync()
    {
        var databasePath = await EnsureDatabasePreparedAsync();
        var connectionFactory = _connectionFactory ?? throw new InvalidOperationException("Database connection factory is not initialized.");

        ICustomerRepository customerRepository = new CustomerRepository(connectionFactory);
        IOrderRepository orderRepository = new OrderRepository(connectionFactory);
        IDealRepository dealRepository = new DealRepository(connectionFactory);
        IFollowUpRepository followUpRepository = new FollowUpRepository(connectionFactory);
        ICustomerNoteRepository noteRepository = new CustomerNoteRepository(connectionFactory);
        IConversationMessageRepository conversationMessageRepository = new ConversationMessageRepository(connectionFactory);
        IOcrResultRepository ocrResultRepository = new OcrResultRepository(connectionFactory);
        IAiSuggestionRepository aiSuggestionRepository = new AiSuggestionRepository(connectionFactory);
        IActivityLogRepository activityLogRepository = new ActivityLogRepository(connectionFactory);
        ISyncRecordRepository syncRecordRepository = new SyncRecordRepository(connectionFactory);
        IPriceAdjustmentRepository priceAdjustmentRepository = new PriceAdjustmentRepository(connectionFactory);
        IReplyTemplateRepository replyTemplateRepository = new ReplyTemplateRepository(connectionFactory);
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
        IBackupService backupService = new LocalBackupService(connectionFactory, syncService, syncRecordRepository, activityLogRepository);
        IPriceAdjustmentService priceAdjustmentService = new PriceAdjustmentService(priceAdjustmentRepository, activityLogRepository);

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
            replyTemplateRepository,
            settingRepository,
            clipboardService,
            databasePath);
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
            if (string.Equals(hotkey, preferences.MainHotkey, StringComparison.OrdinalIgnoreCase))
            {
                ShowMainWindow();
            }
            else if (string.Equals(hotkey, preferences.FloatingHotkey, StringComparison.OrdinalIgnoreCase))
            {
                ToggleFloatingWindow();
            }
        };

        _mainWindow.SourceInitialized += (_, _) =>
        {
            _hotkeyService.Attach(_mainWindow);
            _hotkeyService.Register(MainHotkeyId, preferences.MainHotkey);
            _hotkeyService.Register(FloatingHotkeyId, preferences.FloatingHotkey);
        };

        Console.WriteLine("MainWindow showing");
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.ShowInTaskbar = true;
        _mainWindow.Opacity = 1;
        _mainWindow.Show();
        _mainWindow.Activate();

        if (preferences.ShowFloatingWindowOnStartup)
        {
            _floatingWindow.Show();
        }
    }

    private void ShowLoginView()
    {
        var loginViewModel = new LoginViewModel();
        var loginView = new LoginView(loginViewModel);

        loginViewModel.LoginSucceeded += async (_, _) => await CompleteLoginAsync(loginView);
        loginView.Closed += (_, _) =>
        {
            if (!_isLoginCompleted && !IsExiting)
            {
                ExitApplication();
            }
        };

        Console.WriteLine("LoginView showing");
        loginView.WindowState = WindowState.Normal;
        loginView.ShowInTaskbar = true;
        loginView.Opacity = 1;
        loginView.Show();
        loginView.Activate();
    }

    private async Task RunQaMaintenanceCommandAsync()
    {
        await EnsureDatabasePreparedAsync();

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

    private async Task<string> EnsureDatabasePreparedAsync()
    {
        if (_startupDataPrepared && !string.IsNullOrWhiteSpace(_databasePath) && _connectionFactory is not null)
        {
            return _databasePath;
        }

        _databasePath ??= DatabasePaths.GetDefaultDatabasePath();
        _connectionFactory ??= new SqliteConnectionFactory(_databasePath);

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

        _startupDataPrepared = true;
        return _databasePath;
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

    private void ExitApplication()
    {
        IsExiting = true;
        _hotkeyService?.Dispose();
        _trayIconService?.Dispose();
        _floatingWindow?.Close();
        _mainWindow?.Close();
        Shutdown();
    }
}
