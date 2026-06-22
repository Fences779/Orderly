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
    private static void LogNonCriticalWorkspaceStartupFailure(string step, Exception ex)
    {
        Console.Error.WriteLine($"Non-critical workspace startup step failed ({step}): {ex}");
    }

    private void TryRunNonCriticalWorkspaceStartupStep(string step, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            LogNonCriticalWorkspaceStartupFailure(step, ex);
        }
    }

    private async Task TryRunNonCriticalWorkspaceStartupStepAsync(string step, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            LogNonCriticalWorkspaceStartupFailure(step, ex);
        }
    }

    private async Task InitializeWorkspaceAsync(string databasePath)
    {
        databasePath = await EnsureDatabasePreparedAsync(databasePath);
        var connectionFactory = _connectionFactory ?? throw new InvalidOperationException("Database connection factory is not initialized.");
        var fieldEncryptionService = _fieldEncryptionService ?? throw new InvalidOperationException("Field encryption service is not initialized.");

        // 在构造任何仓储（写入路径）前做启动期 fail-closed 断言：
        // 生产路径必须为真实 AES-GCM 实现，检测到空操作加密器即拒绝进入写入路径，避免敏感字段明文落盘。
        FieldEncryptionGuard.EnsureProductionGrade(fieldEncryptionService, nameof(InitializeWorkspaceAsync));

        // 启动期接入旧 CRM 数据迁移（Req 3.4–3.10）。必须在敏感字段回填之前执行，因为回填会把旧表的明文列
        // 清零，而迁移从明文列读取旧 CRM 数据。迁移是 backup-first / 非破坏性 / 幂等的：只读旧表、只写 Commerce
        // 表，失败不改动旧数据，并已完成则跳过（见 CommerceStartupMigrationService）。
        await RunCommerceLegacyMigrationAsync(connectionFactory);

        await BackfillSensitiveFieldsAsync(connectionFactory);

        ICustomerRepository customerRepository = new CustomerRepository(connectionFactory, fieldEncryptionService);
        IOrderRepository orderRepository = new OrderRepository(connectionFactory, fieldEncryptionService);
        IDealRepository dealRepository = new DealRepository(connectionFactory, fieldEncryptionService);
        IFollowUpRepository followUpRepository = new FollowUpRepository(connectionFactory, fieldEncryptionService);
        ICustomerNoteRepository noteRepository = new CustomerNoteRepository(connectionFactory, fieldEncryptionService);
        IConversationMessageRepository conversationMessageRepository = new ConversationMessageRepository(connectionFactory, fieldEncryptionService);
        IOcrResultRepository ocrResultRepository = new OcrResultRepository(connectionFactory, fieldEncryptionService);
        IAiSuggestionRepository aiSuggestionRepository = new AiSuggestionRepository(connectionFactory, fieldEncryptionService);
        IActivityLogRepository rawActivityLogRepository = new ActivityLogRepository(connectionFactory, fieldEncryptionService);
        ISyncRecordRepository syncRecordRepository = new SyncRecordRepository(connectionFactory, fieldEncryptionService);
        IPriceAdjustmentRepository priceAdjustmentRepository = new PriceAdjustmentRepository(connectionFactory, fieldEncryptionService);
        IReplyTemplateRepository replyTemplateRepository = new ReplyTemplateRepository(connectionFactory, fieldEncryptionService);
        IAppSettingRepository settingRepository = new AppSettingRepository(connectionFactory);
        IActivityLogRepository activityLogRepository = new SettingsAwareActivityLogRepository(rawActivityLogRepository, settingRepository);
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

        // Legacy outbound gateway/remote integration (customer-specific) has been removed.
        // The remote-backed order/business/inventory services are no longer wired; the
        // commerce pages are re-sourced through the universal Commerce Service Layer below.

        // --- Commerce Service Layer wiring (Req 7.3, 7.4) ---
        // The nine-entry shell's business pages obtain data only through these universal services
        // over the Commerce repositories (the same encrypted SqliteConnectionFactory used by the
        // P0 path, C-2). No legacy remote service participates.
        var commerceOrderRepository = new Orderly.Data.Commerce.Repositories.CommerceOrderRepository(connectionFactory);
        var commerceOrderItemRepository = new Orderly.Data.Commerce.Repositories.OrderItemRepository(connectionFactory);
        var commerceCustomerRepository = new Orderly.Data.Commerce.Repositories.CommerceCustomerRepository(connectionFactory);
        var commerceInventoryItemRepository = new Orderly.Data.Commerce.Repositories.InventoryItemRepository(connectionFactory);
        var commerceInventoryMovementRepository = new Orderly.Data.Commerce.Repositories.InventoryMovementRepository(connectionFactory);
        var commerceCashFlowRepository = new Orderly.Data.Commerce.Repositories.CashFlowEntryRepository(connectionFactory);
        var commerceProductRepository = new Orderly.Data.Commerce.Repositories.ProductRepository(connectionFactory);
        var commerceInsightRepository = new Orderly.Data.Commerce.Repositories.BusinessInsightRepository(connectionFactory);
        var commerceMetricSnapshotRepository = new Orderly.Data.Commerce.Repositories.BusinessMetricSnapshotRepository(connectionFactory);

        Orderly.Core.Commerce.Services.IDashboardService commerceDashboardService =
            new Orderly.Data.Commerce.Services.CommerceDashboardService(
                commerceOrderRepository,
                commerceCashFlowRepository,
                commerceInventoryItemRepository,
                commerceCustomerRepository,
                commerceMetricSnapshotRepository);
        Orderly.Core.Commerce.Services.IOrderService commerceOrderService =
            new Orderly.Data.Commerce.Services.CommerceOrderService(
                connectionFactory,
                commerceOrderRepository,
                commerceOrderItemRepository,
                commerceInventoryItemRepository,
                commerceInventoryMovementRepository,
                commerceCustomerRepository);
        Orderly.Core.Commerce.Services.IInventoryService commerceInventoryService =
            new Orderly.Data.Commerce.Services.CommerceInventoryService(
                commerceInventoryItemRepository,
                commerceInventoryMovementRepository);
        Orderly.Core.Commerce.Services.ICustomerService commerceCustomerService =
            new Orderly.Data.Commerce.Services.CommerceCustomerService(
                commerceOrderRepository,
                commerceCustomerRepository);
        Orderly.Core.Commerce.Services.ICashFlowService commerceCashFlowService =
            new Orderly.Data.Commerce.Services.CommerceCashFlowService(commerceCashFlowRepository);
        Orderly.Core.Commerce.Services.IBusinessInsightService commerceBusinessInsightService =
            new Orderly.Data.Commerce.Services.CommerceBusinessInsightService(
                commerceInventoryService,
                commerceCashFlowRepository,
                reservedProviders: null,
                insightRepository: commerceInsightRepository);
        Orderly.Core.Commerce.Services.IProductService commerceProductService =
            new Orderly.Data.Commerce.Services.CommerceProductService(commerceProductRepository);

        var preferences = await settingRepository.GetPreferencesAsync();
        Orderly.App.Helpers.ThemeHelper.ApplyTheme(preferences.ThemeMode);
        Orderly.App.Helpers.ThemeHelper.ApplyAccentColor(preferences.AccentColor);
        Orderly.App.Helpers.FontSizeHelper.ApplyFontScale(preferences.FontSizePreset);

        // 任务 21.1：装配设置页 / 我的页 / 门禁层所需的服务实例（UI 改动严格限定在两页及共享样式 /
        // 壳层 Toast / QA 锚点 + 敏感页面门禁访问控制层；登录页与其它页面不受影响）。
        var sessionContextService = _sessionContextService ?? throw new InvalidOperationException("Session context service is not initialized.");
        var sessionLockService = _sessionLockService ?? throw new InvalidOperationException("Session lock service is not initialized.");
        var localAuthService = _localAuthService ?? throw new InvalidOperationException("Local auth service is not initialized.");
        var securityAuditService = _securityAuditService ?? throw new InvalidOperationException("Security audit service is not initialized.");
        var localAccountRepository = _localAccountRepository ?? throw new InvalidOperationException("Local account repository is not initialized.");

        // 壳层 Toast 转发器：MainWindow（IToastService 实现）在 MainViewModel 之后创建，故先以转发器作为
        // 稳定接缝注入子 VM，待 MainWindow 创建后回填其 Target（见下方）。
        var toastRelay = new Orderly.App.Services.ToastServiceRelay();

        // 头像存储（BC-4）：落 app 数据目录受保护 avatars/ 子目录，仅存相对引用键。
        IAvatarStorageService avatarStorageService = new Orderly.App.Services.AvatarStorageService();

        // 设置搜索静态索引（BC-9 / §9.4）。
        ISettingsSearchIndex settingsSearchIndex = new SettingsSearchIndex();

        // 和钱相关机密页面 PIN 门禁（BC-12 / §9.8）：依赖会话上下文 + 本地认证（复用既有 PIN 校验通道）。
        ISensitivePageGuard sensitivePageGuard = new SensitivePageGuard(sessionContextService, localAuthService);

        // 凭证修改后会话转移协调器（BC-11 / §9.6）：主密码改→强制登出、PIN 改→PendingPinUnlock；成功先记审计。
        ICredentialChangeSessionCoordinator credentialChangeSessionCoordinator =
            new CredentialChangeSessionCoordinator(sessionLockService, sessionContextService, securityAuditService);

        // Owner 紧急启用（BC-13 / §9.7）：校验 6 位 PIN → 进入受限权限模式；成功 / 失败均记审计（不含明文 PIN）。
        IEmergencyEnableService emergencyEnableService =
            new EmergencyEnableService(localAccountRepository, localAuthService, sessionContextService, securityAuditService);

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
            syncService,
            syncRecordRepository,
            clipboardService,
            databasePath,
            _localAccountManagementService,
            _sessionContextService,
            sensitivePageGuard,
            toastRelay,
            avatarStorageService,
            localAuthService,
            securityAuditService,
            settingsSearchIndex,
            credentialChangeSessionCoordinator,
            emergencyEnableService,
            commerceOrderRepository);
        _mainViewModel.LockSessionRequested += HandleLockSessionRequested;
        _mainViewModel.LogoutRequested += HandleLogoutRequested;

        // Attach the dedicated per-page ViewModels backed solely by the Commerce Service Layer
        // (Req 7.1, 7.3). Settings (设置) and Me (我的) remain served by their existing delimited
        // MainViewModel partials. Page binding and load-on-navigation are completed by task 19.3.
        _mainViewModel.AttachCommercePages(
            new Orderly.App.ViewModels.Pages.WorkbenchPageViewModel(commerceDashboardService),
            new Orderly.App.ViewModels.Pages.OrdersPageViewModel(commerceOrderService, commerceOrderRepository),
            new Orderly.App.ViewModels.Pages.ProductsPageViewModel(commerceProductService),
            new Orderly.App.ViewModels.Pages.InventoryPageViewModel(commerceInventoryService, commerceInventoryItemRepository),
            new Orderly.App.ViewModels.Pages.CustomersPageViewModel(commerceCustomerService, commerceCustomerRepository),
            new Orderly.App.ViewModels.Pages.CashflowPageViewModel(commerceCashFlowService, commerceCashFlowRepository),
            new Orderly.App.ViewModels.Pages.BusinessAdvicePageViewModel(commerceBusinessInsightService));

        await _mainViewModel.LoadAsync();

        // Load the initially selected commerce page (load-on-navigation also covers later switches).
        await _mainViewModel.EnsureCommercePageLoadedAsync(_mainViewModel.SelectedSection);

        _floatingViewModel = new FloatingWindowViewModel(orderRepository, replyTemplateRepository, clipboardService);
        await TryRunNonCriticalWorkspaceStartupStepAsync(
            "floating window view-model load",
            () => _floatingViewModel.LoadAsync());

        _mainWindow = new MainWindow(_mainViewModel);
        MainWindow = _mainWindow;
        // 任务 21.1：MainWindow 实现 IToastService，创建后回填壳层 Toast 转发器目标，使设置页 / 我的页
        // 子 VM 的 Toast 提示（保存结果 / 头像 / 紧急启用等）经统一壳层 Popup_Toast 呈现。
        toastRelay.Target = _mainWindow;
        _mainWindow.HiddenToTray += OnMainWindowHiddenToTray;
        _mainWindow.ExitRequested += OnMainWindowExitRequested;
        TryRunNonCriticalWorkspaceStartupStep(
            "floating window construction",
            () =>
            {
                _floatingWindow = new FloatingWindow(
                    _floatingViewModel,
                    preferences,
                    settingRepository,
                    ShowMainWindow,
                    NavigateFromFloatingWindow,
                    ExitApplicationFromTray);
            });

        TryRunNonCriticalWorkspaceStartupStep(
            "tray icon service setup",
            () =>
            {
                _trayIconService = new TrayIconService();
                _trayIconService.OpenMainRequested += (_, _) => ShowMainWindow();
                _trayIconService.ToggleFloatingRequested += (_, _) => ToggleFloatingWindow();
                _trayIconService.ExitRequested += (_, _) => ExitApplicationFromTray();
            });

        TryRunNonCriticalWorkspaceStartupStep(
            "global hotkey service setup",
            () =>
            {
                _hotkeyService = new GlobalHotkeyService();
                _hotkeyService.HotkeyPressed += (_, hotkey) =>
                {
                    var matched = _registeredHotkeys.FirstOrDefault(item => string.Equals(item.Value, hotkey, StringComparison.OrdinalIgnoreCase));
                    switch (matched.Key)
                    {
                        case MainHotkeyId:
                            ShowMainWindow();
                            break;
                        case FloatingHotkeyId:
                            ToggleFloatingWindow();
                            break;
                        case GlobalSearchHotkeyId:
                            ShowMainWindow();
                            _mainViewModel?.HandleRuntimeHotkeyAction(RuntimeHotkeyAction.GlobalSearch);
                            break;
                        case TodayWorkbenchHotkeyId:
                            ShowMainWindow();
                            _mainViewModel?.HandleRuntimeHotkeyAction(RuntimeHotkeyAction.TodayWorkbench);
                            break;
                        case CopyOrderSummaryHotkeyId:
                            _mainViewModel?.HandleRuntimeHotkeyAction(RuntimeHotkeyAction.CopyOrderSummary);
                            break;
                        case OpenProductionSheetHotkeyId:
                            ShowMainWindow();
                            _mainViewModel?.HandleRuntimeHotkeyAction(RuntimeHotkeyAction.OpenProductionSheet);
                            break;
                        case MarkOrderExceptionHotkeyId:
                            ShowMainWindow();
                            _mainViewModel?.HandleRuntimeHotkeyAction(RuntimeHotkeyAction.MarkOrderException);
                            break;
                        case AdvanceFulfillmentHotkeyId:
                            ShowMainWindow();
                            _mainViewModel?.HandleRuntimeHotkeyAction(RuntimeHotkeyAction.AdvanceFulfillment);
                            break;
                        case OpenCustomerProfileHotkeyId:
                            ShowMainWindow();
                            _mainViewModel?.HandleRuntimeHotkeyAction(RuntimeHotkeyAction.OpenCustomerProfile);
                            break;
                        case NewCustomerNoteHotkeyId:
                            ShowMainWindow();
                            _mainViewModel?.HandleRuntimeHotkeyAction(RuntimeHotkeyAction.NewCustomerNote);
                            break;
                        case CopyCustomerPreferenceSummaryHotkeyId:
                            _mainViewModel?.HandleRuntimeHotkeyAction(RuntimeHotkeyAction.CopyCustomerPreferenceSummary);
                            break;
                    }
                };
            });
        TryRunNonCriticalWorkspaceStartupStep(
            "settings runtime hook setup",
            () => _mainViewModel.ConfigureSettingsRuntimeHooks(
                TryApplyRuntimeHotkeys,
                TrySendDesktopNotification,
                ApplyFloatingWindowRuntime));

        _mainWindow.SourceInitialized += (_, _) =>
        {
            TryRunNonCriticalWorkspaceStartupStep(
                "main window source initialization",
                () =>
                {
                    _hotkeyService?.Attach(_mainWindow);
                    _isHotkeyAttached = _hotkeyService is not null;
                    if (_isHotkeyAttached)
                    {
                        _ = TryApplyRuntimeHotkeys(preferences);
                    }
                });
        };

        Console.WriteLine("MainWindow showing");
        var defaultWindowState = string.Equals(preferences.DefaultWindowMode, "最大化", StringComparison.Ordinal)
            ? WindowState.Maximized
            : WindowState.Normal;
        TryRunNonCriticalWorkspaceStartupStep(
            "window bounds restore",
            () => ApplyWindowBounds(_mainWindow, preferences));
        _mainWindow.WindowState = defaultWindowState;
        _mainWindow.ShowInTaskbar = true;
        _mainWindow.Opacity = 1;
        _mainWindow.Show();
        _mainWindow.Activate();

        if (preferences.FloatingBallEnabled && preferences.ShowFloatingWindowOnStartup && _floatingWindow is not null)
        {
            TryRunNonCriticalWorkspaceStartupStep(
                "floating window startup show",
                () => _floatingWindow.Show());
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

    /// <summary>
    /// Runs the non-destructive, backup-first, idempotent legacy CRM → Commerce migration at startup
    /// (Req 3.4–3.10). The migration only reads the legacy tables and writes the Commerce tables, so a
    /// failure never harms existing data; the failure reason is logged and startup continues so the app
    /// still opens (the legacy data remains intact and the migration retries on the next launch).
    /// </summary>
    private async Task RunCommerceLegacyMigrationAsync(SqliteConnectionFactory connectionFactory)
    {
        if (_sessionContextService?.IsSignedIn != true)
        {
            return;
        }

        try
        {
            var fieldEncryptionService = _fieldEncryptionService ?? throw new InvalidOperationException("Field encryption service is not initialized.");
            var migrationService = new CommerceStartupMigrationService(connectionFactory, backup: null, fieldEncryption: fieldEncryptionService);
            var result = await migrationService.RunAsync();
            Console.WriteLine($"Commerce legacy migration: {result.OutcomeToken} (records={result.MigratedRecordCount}).");
        }
        catch (Exception ex)
        {
            // A migration failure must not block sign-in. The legacy data is untouched (the migration
            // only reads it), so the next launch retries. Surface the reason for diagnosis.
            Console.Error.WriteLine($"Commerce legacy migration failed (legacy data preserved, will retry): {ex.Message}");
        }
    }
}
