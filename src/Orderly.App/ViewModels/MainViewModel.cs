using CommunityToolkit.Mvvm.ComponentModel;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;

namespace Orderly.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    // Nine top-level navigation entries (Req 6.1 / 17.3). Rendered labels are Chinese-only;
    // the English names in comments are developer-only annotations and MUST NOT be rendered.
    public const string SectionWorkbench = "工作台";        // Workbench
    public const string SectionOrders = "订单";             // Orders
    public const string SectionProducts = "商品";           // Products
    public const string SectionInventory = "库存管理";       // Inventory (rendered label: 库存)
    public const string SectionCustomers = "客户";          // Customers
    public const string SectionCashflow = "现金流";          // Cash Flow
    public const string SectionBusinessAdvice = "经营建议";   // Business Advice
    public const string SectionSettings = "设置";           // Settings
    public const string SectionMe = "我的";                 // Me / Account

    // Legacy/relocated destinations whose dedicated views were removed. They are NOT valid pages
    // anymore; saved or stale references to them are remapped to the closest current page so an
    // upgraded user never lands on a blank main area (订单履约 → 订单, 异常处理 → 经营建议).
    public const string SectionFulfillment = "订单履约";     // Order Fulfillment (relocated → Orders)
    public const string SectionException = "异常处理";       // Exception Handling (relocated → Business Advice)

    private static readonly HashSet<string> SupportedSections = new(StringComparer.Ordinal)
    {
        SectionWorkbench,
        SectionOrders,
        SectionProducts,
        SectionInventory,
        SectionCustomers,
        SectionCashflow,
        SectionBusinessAdvice,
        SectionSettings,
        SectionMe
    };

    // Removed pages whose views no longer exist: map old saved values to the closest current page.
    private static readonly Dictionary<string, string> RelocatedSections = new(StringComparer.Ordinal)
    {
        [SectionFulfillment] = SectionOrders,
        [SectionException] = SectionBusinessAdvice
    };

    private readonly ICustomerRepository _customerRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly ICustomerService _customerService;
    private readonly IOrderService _orderService;
    private readonly IDealService _dealService;
    private readonly IFollowUpService _followUpService;
    private readonly INoteService _noteService;
    private readonly IConversationService _conversationService;
    private readonly IOcrService _ocrService;
    private readonly IAiAssistantService _aiAssistantService;
    private readonly IAutoReplyService _autoReplyService;
    private readonly IActivityLogService _activityLogService;
    private readonly IWorkbenchTaskService _workbenchTaskService;
    private readonly IGlobalSearchService _globalSearchService;
    private readonly INavigationRouteService _navigationRouteService;
    private readonly IBackupService _backupService;
    private readonly IPriceAdjustmentService _priceAdjustmentService;
    private readonly IReplyTemplateRepository _replyTemplateRepository;
    private readonly IAppSettingRepository _settingRepository;
    private readonly ISyncService _syncService;
    private readonly ISyncRecordRepository _syncRecordRepository;
    private readonly IClipboardService _clipboardService;
    private readonly ILocalAccountManagementService? _localAccountManagementService;
    private readonly ISessionContextService? _sessionContextService;

    /// <summary>「我的页」专属 ViewModel（设计 §8.1 / BC-8），经组合暴露给视图绑定。</summary>
    public MeProfileViewModel MeProfile { get; }

    /// <summary>「设置页」专属 ViewModel（设计 §8.4 / BC-8 / BC-9），经组合暴露给视图绑定。</summary>
    public SettingsViewModel Settings { get; }

    /// <summary>
    /// 「和钱相关机密页面」PIN 门禁协调器（任务 19.1 / BC-12，设计 §9.8 / Req 18.1·18.2·18.3·13.3）。
    /// 经组合暴露给 <c>MainWindow</c> 在现金流 / 经营建议页面宿主上叠加 PIN 验证遮罩与内容渲染门控。
    /// </summary>
    public SensitivePageGuardViewModel SensitiveGuard { get; }

    public MainViewModel(
        ICustomerRepository customerRepository,
        IOrderRepository orderRepository,
        ICustomerService customerService,
        IOrderService orderService,
        IDealService dealService,
        IFollowUpService followUpService,
        INoteService noteService,
        IConversationService conversationService,
        IOcrService ocrService,
        IAiAssistantService aiAssistantService,
        IAutoReplyService autoReplyService,
        IActivityLogService activityLogService,
        IBackupService backupService,
        IPriceAdjustmentService priceAdjustmentService,
        IReplyTemplateRepository replyTemplateRepository,
        IAppSettingRepository settingRepository,
        ISyncService syncService,
        ISyncRecordRepository syncRecordRepository,
        IClipboardService clipboardService,
        string databasePath,
        ILocalAccountManagementService? localAccountManagementService = null,
        ISessionContextService? sessionContextService = null,
        ISensitivePageGuard? sensitivePageGuard = null)
        : this(
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
            EmptyWorkbenchTaskService.Instance,
            EmptyGlobalSearchService.Instance,
            EmptyNavigationRouteService.Instance,
            backupService,
            priceAdjustmentService,
            replyTemplateRepository,
            settingRepository,
            syncService,
            syncRecordRepository,
            clipboardService,
            databasePath,
            localAccountManagementService,
            sessionContextService,
            sensitivePageGuard)
    {
    }

    public MainViewModel(
        ICustomerRepository customerRepository,
        IOrderRepository orderRepository,
        ICustomerService customerService,
        IOrderService orderService,
        IDealService dealService,
        IFollowUpService followUpService,
        INoteService noteService,
        IConversationService conversationService,
        IOcrService ocrService,
        IAiAssistantService aiAssistantService,
        IAutoReplyService autoReplyService,
        IActivityLogService activityLogService,
        IWorkbenchTaskService workbenchTaskService,
        IGlobalSearchService globalSearchService,
        INavigationRouteService navigationRouteService,
        IBackupService backupService,
        IPriceAdjustmentService priceAdjustmentService,
        IReplyTemplateRepository replyTemplateRepository,
        IAppSettingRepository settingRepository,
        ISyncService syncService,
        ISyncRecordRepository syncRecordRepository,
        IClipboardService clipboardService,
        string databasePath,
        ILocalAccountManagementService? localAccountManagementService = null,
        ISessionContextService? sessionContextService = null,
        ISensitivePageGuard? sensitivePageGuard = null)
    {
        _customerRepository = customerRepository;
        _orderRepository = orderRepository;
        _customerService = customerService;
        _orderService = orderService;
        _dealService = dealService;
        _followUpService = followUpService;
        _noteService = noteService;
        _conversationService = conversationService;
        _ocrService = ocrService;
        _aiAssistantService = aiAssistantService;
        _autoReplyService = autoReplyService;
        _activityLogService = activityLogService;
        _workbenchTaskService = workbenchTaskService;
        _globalSearchService = globalSearchService;
        _navigationRouteService = navigationRouteService;
        _backupService = backupService;
        _priceAdjustmentService = priceAdjustmentService;
        _replyTemplateRepository = replyTemplateRepository;
        _settingRepository = settingRepository;
        _syncService = syncService;
        _syncRecordRepository = syncRecordRepository;
        _clipboardService = clipboardService;
        _localAccountManagementService = localAccountManagementService;
        _sessionContextService = sessionContextService;
        DatabasePath = databasePath;
        InitializeFilterOptions();

        // 我的页 ViewModel（设计 §8.1 / BC-8）：经组合暴露，命令接线由后续任务完成。
        // 当前阶段以 MainViewModel 已持有的服务注入；ILocalAuthService / IAvatarStorageService /
        // IToastService / ICredentialChangeSessionCoordinator / ISecurityAuditService / IEmergencyEnableService 等留待集成任务（21.1）
        // 完整接线（设计允许部分服务先注入备用：协调器为空时凭证修改命令跳过会话转移；安全审计服务为空时
        // IsSecurityAuditAvailable=false，账户安全卡显示占位文案，仍正常展示最近登录时间；紧急启用服务为空时
        // 紧急启用命令不可执行，受限模式只读状态仍随会话权限模式刷新）。
        MeProfile = new MeProfileViewModel(
            accountService: localAccountManagementService,
            sessionContext: sessionContextService,
            settingRepository: settingRepository,
            securityAudit: null,
            emergencyEnable: null);

        // 设置页 ViewModel（设计 §8.4 / BC-8 / BC-9）：经组合暴露，P0 状态与映射（13.1）、自动保存引擎（13.2）、
        // 命令与状态文案（13.3）、P1/AI 诊断/快捷键与通知（13.4，含运行态热键应用）已迁入「新家」。当前阶段
        // SettingsView 仍绑定 MainViewModel 既有 Settings* 分部；运行态热键/通知委托接缝经
        // ConfigureSettingsRuntimeHooks 在集成接线（21.1）时转发注入。搜索（13.5）、离开页闸门（13.6）及
        // DataContext 改绑由后续任务完成。ISettingsSearchIndex / IToastService 留待集成任务（21.1）注入。
        Settings = new SettingsViewModel(
            settingRepository: settingRepository,
            activityLogService: activityLogService,
            clipboardService: clipboardService,
            sessionContextService: sessionContextService,
            databasePath: databasePath);

        // 机密页面 PIN 门禁协调器（任务 19.1 / BC-12 / 设计 §9.8）：以可空 ISensitivePageGuard 注入并接线。
        // guard 为空时（DI 完整注册在 21.1 之前）安全降级——门禁不激活、现金流/经营建议照常可进入，
        // 避免在接线前把机密页面彻底挡死；注入真实 guard（21.1）后门禁自动启用。
        SensitiveGuard = new SensitivePageGuardViewModel(sensitivePageGuard, sessionContextService);
        SensitiveGuard.OnSectionChanged(SelectedSection);
    }

    private static string NormalizeSection(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return SectionWorkbench;
        }

        var normalized = value.Trim();
        if (SupportedSections.Contains(normalized))
        {
            return normalized;
        }

        // Relocated pages (订单履约/异常处理) map to their replacement; any other legacy or
        // unrecognized value falls back to the Workbench so the content area is never blank.
        return RelocatedSections.TryGetValue(normalized, out var mapped) ? mapped : SectionWorkbench;
    }
}
