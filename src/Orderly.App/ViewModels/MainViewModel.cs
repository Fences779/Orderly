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
        ISessionContextService? sessionContextService = null)
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
            sessionContextService)
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
        ISessionContextService? sessionContextService = null)
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
