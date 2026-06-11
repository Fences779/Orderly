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
    private async Task InitializeWorkspaceAsync(string databasePath)
    {
        databasePath = await EnsureDatabasePreparedAsync(databasePath);
        var connectionFactory = _connectionFactory ?? throw new InvalidOperationException("Database connection factory is not initialized.");
        var fieldEncryptionService = _fieldEncryptionService ?? throw new InvalidOperationException("Field encryption service is not initialized.");

        await BackfillSensitiveFieldsAsync(connectionFactory);

        ICustomerRepository customerRepository = new CustomerRepository(connectionFactory, fieldEncryptionService);
        IOrderRepository orderRepository = new OrderRepository(connectionFactory, fieldEncryptionService);
        IDealRepository dealRepository = new DealRepository(connectionFactory, fieldEncryptionService);
        IFollowUpRepository followUpRepository = new FollowUpRepository(connectionFactory, fieldEncryptionService);
        ICustomerNoteRepository noteRepository = new CustomerNoteRepository(connectionFactory, fieldEncryptionService);
        IConversationMessageRepository conversationMessageRepository = new ConversationMessageRepository(connectionFactory, fieldEncryptionService);
        IOcrResultRepository ocrResultRepository = new OcrResultRepository(connectionFactory, fieldEncryptionService);
        IAiSuggestionRepository aiSuggestionRepository = new AiSuggestionRepository(connectionFactory, fieldEncryptionService);
        IActivityLogRepository activityLogRepository = new ActivityLogRepository(connectionFactory, fieldEncryptionService);
        ISyncRecordRepository syncRecordRepository = new SyncRecordRepository(connectionFactory, fieldEncryptionService);
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
            aiProviderOptions,
            settingRepository);
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
        var stringNarrationHttpClient = OutboundHttpClientFactory.Create(
            TimeSpan.FromSeconds(stringNarrationGatewayOptions.TimeoutSeconds));
        var stringNarrationGatewayClient = new StringNarrationGatewayClient(stringNarrationHttpClient, stringNarrationGatewayOptions);
        IStringNarrationOrderService stringNarrationOrderService = new StringNarrationGatewayOrderService(stringNarrationGatewayClient);
        IStringNarrationBusinessService stringNarrationBusinessService = new StringNarrationGatewayBusinessService(stringNarrationGatewayClient);
        var inventoryGatewayOptions = InventoryGatewayOptions.FromEnvironment();
        var inventoryHttpClient = OutboundHttpClientFactory.Create(
            TimeSpan.FromSeconds(inventoryGatewayOptions.TimeoutSeconds));
        IInventoryWorkspaceService inventoryWorkspaceService = inventoryGatewayOptions.IsConfigured
            ? new CloudInventoryWorkspaceService(new InventoryGatewayClient(inventoryHttpClient, inventoryGatewayOptions))
            : new StringNarrationInventoryWorkspaceServiceAdapter(stringNarrationBusinessService);

        var preferences = await settingRepository.GetPreferencesAsync();
        Orderly.App.Helpers.ThemeHelper.ApplyTheme(preferences.ThemeMode);

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
            stringNarrationGatewayOptions.TimeoutSeconds,
            stringNarrationBusinessService,
            inventoryWorkspaceService);
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

    private async Task BackfillSensitiveFieldsAsync(SqliteConnectionFactory connectionFactory)
    {
        if (_sessionContextService?.IsSignedIn != true)
        {
            return;
        }

        var fieldEncryptionService = _fieldEncryptionService ?? throw new InvalidOperationException("Field encryption service is not initialized.");
        var sensitiveMigrationService = new SensitiveFieldMigrationService(connectionFactory, fieldEncryptionService);
        await sensitiveMigrationService.BackfillAsync();
    }
}
