using CommunityToolkit.Mvvm.ComponentModel;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;

namespace Orderly.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public const string SectionWorkbench = "工作台";
    public const string SectionFulfillment = "订单履约";
    public const string SectionInventory = "库存管理";
    public const string SectionCashflow = "现金流";
    public const string SectionException = "异常处理";
    public const string SectionSettings = "设置";
    public const string SectionMe = "我的";

    private static readonly HashSet<string> SupportedSections = new(StringComparer.Ordinal)
    {
        SectionWorkbench,
        SectionFulfillment,
        SectionInventory,
        SectionCashflow,
        SectionException,
        SectionSettings,
        SectionMe
    };

    private static readonly HashSet<string> LegacySections = new(StringComparer.Ordinal)
    {
        "客户/订单",
        "话术库"
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
    private readonly IStringNarrationOrderService _stringNarrationOrderService;
    private readonly IStringNarrationBusinessService _stringNarrationBusinessService;
    private readonly IInventoryWorkspaceService _inventoryWorkspaceService;
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
        IStringNarrationOrderService? stringNarrationOrderService,
        IReplyTemplateRepository replyTemplateRepository,
        IAppSettingRepository settingRepository,
        ISyncService syncService,
        ISyncRecordRepository syncRecordRepository,
        IClipboardService clipboardService,
        string databasePath,
        ILocalAccountManagementService? localAccountManagementService = null,
        ISessionContextService? sessionContextService = null,
        string stringNarrationGatewayEndpoint = "",
        bool isStringNarrationGatewayTokenConfigured = false,
        int stringNarrationGatewayTimeoutSeconds = 15,
        IStringNarrationBusinessService? stringNarrationBusinessService = null,
        IInventoryWorkspaceService? inventoryWorkspaceService = null)
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
            stringNarrationOrderService ?? EmptyStringNarrationOrderService.Instance,
            replyTemplateRepository,
            settingRepository,
            syncService,
            syncRecordRepository,
            clipboardService,
            databasePath,
            localAccountManagementService,
            sessionContextService,
            stringNarrationGatewayEndpoint,
            isStringNarrationGatewayTokenConfigured,
            stringNarrationGatewayTimeoutSeconds,
            stringNarrationBusinessService,
            inventoryWorkspaceService)
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
        IStringNarrationOrderService? stringNarrationOrderService,
        IReplyTemplateRepository replyTemplateRepository,
        IAppSettingRepository settingRepository,
        ISyncService syncService,
        ISyncRecordRepository syncRecordRepository,
        IClipboardService clipboardService,
        string databasePath,
        ILocalAccountManagementService? localAccountManagementService = null,
        ISessionContextService? sessionContextService = null,
        string stringNarrationGatewayEndpoint = "",
        bool isStringNarrationGatewayTokenConfigured = false,
        int stringNarrationGatewayTimeoutSeconds = 15,
        IStringNarrationBusinessService? stringNarrationBusinessService = null,
        IInventoryWorkspaceService? inventoryWorkspaceService = null)
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
        _stringNarrationOrderService = stringNarrationOrderService ?? EmptyStringNarrationOrderService.Instance;
        _stringNarrationBusinessService = stringNarrationBusinessService ?? EmptyStringNarrationBusinessService.Instance;
        _inventoryWorkspaceService = inventoryWorkspaceService ?? EmptyInventoryWorkspaceService.Instance;
        _replyTemplateRepository = replyTemplateRepository;
        _settingRepository = settingRepository;
        _syncService = syncService;
        _syncRecordRepository = syncRecordRepository;
        _clipboardService = clipboardService;
        _localAccountManagementService = localAccountManagementService;
        _sessionContextService = sessionContextService;
        DatabasePath = databasePath;
        IsStringNarrationGatewayEndpointConfigured = !string.IsNullOrWhiteSpace(stringNarrationGatewayEndpoint);
        StringNarrationGatewayEndpoint = IsStringNarrationGatewayEndpointConfigured ? stringNarrationGatewayEndpoint : "未配置";
        IsStringNarrationGatewayTokenConfigured = isStringNarrationGatewayTokenConfigured;
        StringNarrationGatewayTimeoutSeconds = stringNarrationGatewayTimeoutSeconds;
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

        return LegacySections.Contains(normalized) ? SectionWorkbench : SectionWorkbench;
    }
}
