using System.Collections.ObjectModel;
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

    private static readonly HashSet<string> SupportedSections = new(StringComparer.Ordinal)
    {
        SectionWorkbench,
        SectionFulfillment,
        SectionInventory,
        SectionCashflow,
        SectionException,
        SectionSettings
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
        IStringNarrationBusinessService? stringNarrationBusinessService = null)
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
            stringNarrationBusinessService)
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
        IStringNarrationBusinessService? stringNarrationBusinessService = null)
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

    public ObservableCollection<Customer> Customers { get; } = new();
    public ObservableCollection<OrderListItem> Orders { get; } = new();
    public ObservableCollection<Deal> Deals { get; } = new();
    public ObservableCollection<FollowUp> FollowUps { get; } = new();
    public ObservableCollection<CustomerNote> CustomerNotes { get; } = new();
    public ObservableCollection<ConversationMessageListItem> ConversationMessages { get; } = new();
    public ObservableCollection<AiSuggestionListItem> AiSuggestions { get; } = new();
    public ObservableCollection<PriceAdjustment> PriceAdjustments { get; } = new();
    public ObservableCollection<ActivityLog> ActivityLogs { get; } = new();
    public ObservableCollection<ReplyTemplate> ReplyTemplates { get; } = new();
    public ObservableCollection<WorkbenchTaskListItem> WorkbenchTasks { get; } = new();
    public ObservableCollection<SearchResultListItem> SearchResults { get; } = new();
    public ObservableCollection<string> Sections { get; } = new(new[] { SectionWorkbench, SectionFulfillment, SectionInventory, SectionCashflow, SectionException, SectionSettings });
    public ObservableCollection<SearchFilterOption> SearchFilterOptions { get; } = new();
    public ObservableCollection<QuickFilterOption> QuickFilterOptions { get; } = new();
    public ObservableCollection<CustomerStatus> CustomerStatusOptions { get; } = new(Enum.GetValues<CustomerStatus>());
    public ObservableCollection<OrderStatus> OrderStatusOptions { get; } = new(Enum.GetValues<OrderStatus>());
    public ObservableCollection<LocalAccountSummary> ManagedAccounts { get; } = new();

    public event Action? LockSessionRequested;
    public event Action? LogoutRequested;

    public string DatabasePath { get; }

    private List<Customer> _allCustomers = new();
    private List<OrderListItem> _allOrders = new();
    private List<Deal> _allDeals = new();
    private List<FollowUp> _allFollowUps = new();
    private List<CustomerNote> _allCustomerNotes = new();

    [ObservableProperty]
    private string selectedSection = SectionWorkbench;

    [ObservableProperty]
    private bool isCurrentUserOwner;

    [ObservableProperty]
    private string currentAccountDisplayName = string.Empty;

    [ObservableProperty]
    private string searchKeyword = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunSearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshSearchCommand))]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    private SearchFilterOption selectedStatusFilter = SearchFilterOption.All;

    [ObservableProperty]
    private QuickFilterOption selectedQuickFilter = QuickFilterOption.All;

    [ObservableProperty]
    private CustomerStatus selectedCustomerStatusInput = CustomerStatus.Active;

    [ObservableProperty]
    private OrderStatus selectedOrderStatusInput = OrderStatus.PendingCommunication;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedOrder))]
    [NotifyPropertyChangedFor(nameof(SelectedStatusLabel))]
    [NotifyPropertyChangedFor(nameof(HasSelectedOrder))]
    [NotifyPropertyChangedFor(nameof(OrderDetailsEmptyMessage))]
    [NotifyPropertyChangedFor(nameof(SelectedOrderHeadline))]
    [NotifyPropertyChangedFor(nameof(SelectedOrderRequirementSummary))]
    [NotifyPropertyChangedFor(nameof(SelectedOrderAmountText))]
    [NotifyPropertyChangedFor(nameof(SelectedNextFollowUpText))]
    [NotifyPropertyChangedFor(nameof(SelectedOrderRequirementText))]
    [NotifyPropertyChangedFor(nameof(SelectedOrderStatusText))]
    [NotifyPropertyChangedFor(nameof(SelectedSourcePlatformText))]
    [NotifyPropertyChangedFor(nameof(SelectedChannelText))]
    [NotifyPropertyChangedFor(nameof(SelectedExternalIdText))]
    [NotifyPropertyChangedFor(nameof(SelectedConversationContextText))]
    [NotifyCanExecuteChangedFor(nameof(SelectOcrImageCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConvertOcrToConversationMessageCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddOrderCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddConversationMessageCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerateAiSuggestionCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddPriceAdjustmentCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeOrderStatusCommand))]
    private OrderListItem? selectedOrderItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CustomerStatusLabel))]
    [NotifyPropertyChangedFor(nameof(CustomerPriorityLabel))]
    [NotifyPropertyChangedFor(nameof(HasSelectedCustomer))]
    [NotifyPropertyChangedFor(nameof(OrderDetailsEmptyMessage))]
    [NotifyPropertyChangedFor(nameof(SelectedCustomerNameText))]
    [NotifyPropertyChangedFor(nameof(CustomerRemarkText))]
    [NotifyPropertyChangedFor(nameof(SelectedConversationContextText))]
    [NotifyCanExecuteChangedFor(nameof(SelectOcrImageCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConvertOcrToConversationMessageCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddNoteCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddFollowUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddOrderCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddConversationMessageCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerateAiSuggestionCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddPriceAdjustmentCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeDealStageCommand))]
    [NotifyCanExecuteChangedFor(nameof(AdvanceDealStageCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeCustomerStatusCommand))]
    private Customer? selectedCustomer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentDealStage))]
    private Deal? selectedDeal;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcceptAiSuggestionCommand))]
    [NotifyCanExecuteChangedFor(nameof(RejectAiSuggestionCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrepareAutoReplyDraftCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyAutoReplyDraftCommand))]
    [NotifyCanExecuteChangedFor(nameof(MarkAutoReplySentCommand))]
    [NotifyCanExecuteChangedFor(nameof(RejectAutoReplyDraftCommand))]
    private AiSuggestionListItem? selectedAiSuggestion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenWorkbenchTaskCommand))]
    private WorkbenchTaskListItem? selectedWorkbenchTask;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenSearchResultCommand))]
    private SearchResultListItem? selectedSearchResult;

    [ObservableProperty]
    private WorkbenchTaskFilter workbenchTaskFilter = new();

    [ObservableProperty]
    private NavigationTarget? currentNavigationTarget;

    [ObservableProperty]
    private string lastNavigationStatus = string.Empty;

    [ObservableProperty]
    private string lastNavigationError = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentOcrResult))]
    [NotifyPropertyChangedFor(nameof(CurrentOcrFileNameText))]
    [NotifyPropertyChangedFor(nameof(CurrentOcrStatusText))]
    [NotifyPropertyChangedFor(nameof(CurrentOcrPreviewText))]
    [NotifyPropertyChangedFor(nameof(CurrentOcrHintText))]
    [NotifyPropertyChangedFor(nameof(IsCurrentOcrConverted))]
    [NotifyCanExecuteChangedFor(nameof(ConvertOcrToConversationMessageCommand))]
    private OcrResult? currentOcrResult;

    [ObservableProperty]
    private AppPreferences preferences = new();

    [ObservableProperty]
    private string recentBackupStatusText = "暂无本地备份";

    [ObservableProperty]
    private string recentBackupDetailText = "导出后会在这里显示最近一次本地备份状态。";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ValidateBackupCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreBackupCommand))]
    private string selectedBackupPath = string.Empty;

    [ObservableProperty]
    private string restoreStatusText = "未选择恢复备份";

    [ObservableProperty]
    private string restoreDetailText = "先选择备份文件，再执行校验或恢复。";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreBackupCommand))]
    private BackupRestorePreviewResult? restorePreview;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreBackupCommand))]
    private bool isRestoreRiskConfirmed;

    public bool HasRestorePreview => RestorePreview is not null;

    public bool CanConfirmRestoreRisk => RestorePreview?.CanRestore == true;

    public string RestorePreviewFileName => string.IsNullOrWhiteSpace(RestorePreview?.FileName)
        ? "未生成"
        : RestorePreview.FileName;

    public string RestorePreviewExportedAtText => RestorePreview?.ExportedAt is DateTimeOffset exportedAt && exportedAt != default
        ? exportedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss")
        : "未知";

    public string RestorePreviewSchemaVersionText => RestorePreview?.SchemaVersion?.ToString() ?? "未知";

    public string RestorePreviewChecksumText => string.IsNullOrWhiteSpace(RestorePreview?.Checksum)
        ? "无"
        : RestorePreview.Checksum;

    public string RestorePreviewChecksumStatusText => RestorePreview is null
        ? "未校验"
        : RestorePreview.IsChecksumValid
            ? "Valid"
            : "Invalid";

    public string RestorePreviewCountsText => FormatBackupCounts(RestorePreview?.Counts ?? new Dictionary<string, int>(StringComparer.Ordinal));

    public string RestorePreviewTargetCountsText => FormatBackupCounts(RestorePreview?.TargetCounts ?? new Dictionary<string, int>(StringComparer.Ordinal));

    public string RestorePreviewTargetStateCodeText => GetRestoreTargetCode(RestorePreview?.TargetState ?? BackupRestoreTargetState.Unknown);

    public string RestorePreviewTargetStateText => GetRestoreTargetLabel(RestorePreview?.TargetState ?? BackupRestoreTargetState.Unknown);

    public string RestorePreviewWillClearQaDataText => RestorePreview is { WillClearQaData: true } ? "是" : "否";

    public string RestorePreviewCanRestoreText => RestorePreview is { CanRestore: true } ? "是" : "否";

    public string RestorePreviewRefuseReasonText => string.IsNullOrWhiteSpace(RestorePreview?.RefuseReason)
        ? "无"
        : RestorePreview.RefuseReason;

    public string RestoreRiskPromptText => RestorePreview switch
    {
        null => "先选择备份并生成恢复预览，再确认风险。",
        { CanRestore: false } => "当前预览已拒绝恢复，禁止继续执行。",
        { WillClearQaData: true } => "恢复会先清理当前 QA/测试数据，再按备份完整覆盖恢复。",
        _ => "恢复会按预览结果覆盖当前空库；不会合并数据，也不会覆盖已有生产库。"
    };

    public string RestoreRiskConfirmationText => RestorePreview is { WillClearQaData: true }
        ? "我已确认：将先清理当前 QA/测试数据，再执行恢复。"
        : "我已确认：已阅读预览和风险提示，并继续恢复。";

    public bool CanRestoreWithConfirmation => RestorePreview?.CanRestore == true && IsRestoreRiskConfirmed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusError))]
    [NotifyPropertyChangedFor(nameof(StatusMessageTitle))]
    [NotifyPropertyChangedFor(nameof(CustomersEmptyStateText))]
    [NotifyPropertyChangedFor(nameof(OrdersEmptyStateText))]
    private string statusMessage = "准备就绪";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddConversationMessageCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectOcrImageCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConvertOcrToConversationMessageCommand))]
    private string conversationMessageInput = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectBackupFileCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportBackupCommand))]
    [NotifyCanExecuteChangedFor(nameof(ValidateBackupCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreBackupCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddNoteCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddFollowUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddCustomerCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddOrderCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddConversationMessageCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectOcrImageCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConvertOcrToConversationMessageCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddPriceAdjustmentCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeDealStageCommand))]
    [NotifyCanExecuteChangedFor(nameof(AdvanceDealStageCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeCustomerStatusCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeOrderStatusCommand))]
    [NotifyCanExecuteChangedFor(nameof(CompleteFollowUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(SnoozeFollowUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelFollowUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerateAiSuggestionCommand))]
    [NotifyCanExecuteChangedFor(nameof(AcceptAiSuggestionCommand))]
    [NotifyCanExecuteChangedFor(nameof(RejectAiSuggestionCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrepareAutoReplyDraftCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyAutoReplyDraftCommand))]
    [NotifyCanExecuteChangedFor(nameof(MarkAutoReplySentCommand))]
    [NotifyCanExecuteChangedFor(nameof(RejectAutoReplyDraftCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshWorkbenchTasksCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenWorkbenchTaskCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunSearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshSearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenSearchResultCommand))]
    private bool isLoading;

    [ObservableProperty]
    private string? loadError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectBackupFileCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportBackupCommand))]
    [NotifyCanExecuteChangedFor(nameof(ValidateBackupCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreBackupCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddNoteCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddFollowUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddCustomerCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddOrderCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddConversationMessageCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectOcrImageCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConvertOcrToConversationMessageCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddPriceAdjustmentCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeDealStageCommand))]
    [NotifyCanExecuteChangedFor(nameof(AdvanceDealStageCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeCustomerStatusCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeOrderStatusCommand))]
    [NotifyCanExecuteChangedFor(nameof(CompleteFollowUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(SnoozeFollowUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelFollowUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerateAiSuggestionCommand))]
    [NotifyCanExecuteChangedFor(nameof(AcceptAiSuggestionCommand))]
    [NotifyCanExecuteChangedFor(nameof(RejectAiSuggestionCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrepareAutoReplyDraftCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyAutoReplyDraftCommand))]
    [NotifyCanExecuteChangedFor(nameof(MarkAutoReplySentCommand))]
    [NotifyCanExecuteChangedFor(nameof(RejectAutoReplyDraftCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshWorkbenchTasksCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenWorkbenchTaskCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunSearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshSearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenSearchResultCommand))]
    private bool isSaving;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectBackupFileCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportBackupCommand))]
    [NotifyCanExecuteChangedFor(nameof(ValidateBackupCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreBackupCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddNoteCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddFollowUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddCustomerCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddOrderCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddConversationMessageCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectOcrImageCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConvertOcrToConversationMessageCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddPriceAdjustmentCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeDealStageCommand))]
    [NotifyCanExecuteChangedFor(nameof(AdvanceDealStageCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeCustomerStatusCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeOrderStatusCommand))]
    [NotifyCanExecuteChangedFor(nameof(CompleteFollowUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(SnoozeFollowUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelFollowUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerateAiSuggestionCommand))]
    [NotifyCanExecuteChangedFor(nameof(AcceptAiSuggestionCommand))]
    [NotifyCanExecuteChangedFor(nameof(RejectAiSuggestionCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrepareAutoReplyDraftCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyAutoReplyDraftCommand))]
    [NotifyCanExecuteChangedFor(nameof(MarkAutoReplySentCommand))]
    [NotifyCanExecuteChangedFor(nameof(RejectAutoReplyDraftCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshWorkbenchTasksCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenWorkbenchTaskCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunSearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshSearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenSearchResultCommand))]
    private bool isGeneratingAiSuggestion;

    private bool _isSynchronizingSelection;
    private int _detailLoadVersion;

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

    private sealed class EmptyWorkbenchTaskService : IWorkbenchTaskService
    {
        public static EmptyWorkbenchTaskService Instance { get; } = new();

        public Task<IReadOnlyList<WorkbenchTask>> GetTasksAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<WorkbenchTask>>([]);
        }

        public Task<IReadOnlyList<WorkbenchTask>> GetTasksAsync(WorkbenchTaskFilter filter, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<WorkbenchTask>>([]);
        }

        public Task<IReadOnlyList<WorkbenchTask>> GetTasksAsync(WorkbenchTaskQuery query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<WorkbenchTask>>([]);
        }
    }

    private sealed class EmptyGlobalSearchService : IGlobalSearchService
    {
        public static EmptyGlobalSearchService Instance { get; } = new();

        public Task<SearchResultSet> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SearchResultSet
            {
                Query = request?.Query?.Trim() ?? string.Empty,
                Limit = request?.Limit ?? 50,
                TotalCount = 0,
                Items = []
            });
        }
    }

    private sealed class EmptyNavigationRouteService : INavigationRouteService
    {
        public static EmptyNavigationRouteService Instance { get; } = new();

        public Task<NavigationRouteResult> ResolveAsync(SearchResultItem item, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new NavigationRouteResult
            {
                CanNavigate = false,
                DisabledReason = "Navigation route service is not configured.",
                StatusMessage = "Navigation route service is not configured."
            });
        }

        public Task<NavigationRouteResult> ResolveAsync(WorkbenchTask task, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new NavigationRouteResult
            {
                CanNavigate = false,
                DisabledReason = "Navigation route service is not configured.",
                StatusMessage = "Navigation route service is not configured."
            });
        }

        public Task<NavigationRouteResult> ResolveAsync(QuickAction action, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new NavigationRouteResult
            {
                CanNavigate = false,
                DisabledReason = "Navigation route service is not configured.",
                StatusMessage = "Navigation route service is not configured."
            });
        }
    }

    private sealed class EmptyStringNarrationOrderService : IStringNarrationOrderService
    {
        public static EmptyStringNarrationOrderService Instance { get; } = new();

        public Task<StringNarrationWhoamiResult> WhoamiAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("串述订单服务未配置。");
        }

        public Task<StringNarrationOrderListResult> GetOrdersAsync(StringNarrationOrderQuery query, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("串述订单服务未配置。");
        }

        public Task<StringNarrationFulfillmentStats> GetFulfillmentStatsAsync(StringNarrationOrderQuery query, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("串述订单服务未配置。");
        }

        public Task<StringNarrationOrderDetail> GetOrderDetailAsync(string orderNo, string tradeNo = "", string id = "", CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("串述订单服务未配置。");
        }

        public Task<StringNarrationOrderDetail> UpdateFulfillmentAsync(StringNarrationFulfillmentUpdateRequest request, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("串述订单服务未配置。");
        }

        public Task<StringNarrationExceptionActionResult> ApplyExceptionActionAsync(StringNarrationExceptionActionRequest request, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("串述订单服务未配置。");
        }

        public Task<StringNarrationExceptionSampleReplayResult> ReplayExceptionSamplesAsync(StringNarrationExceptionSampleReplayRequest request, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("串述订单服务未配置。");
        }

        public Task<StringNarrationOrderDetail> GenerateProductionOrderAsync(StringNarrationGenerateProductionOrderRequest request, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("串述订单服务未配置。");
        }
    }

    private sealed class EmptyStringNarrationBusinessService : IStringNarrationBusinessService
    {
        public static EmptyStringNarrationBusinessService Instance { get; } = new();

        public Task<StringNarrationInventoryListResult> GetInventoryAsync(StringNarrationInventoryQuery query, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("串述业务数据服务未配置。");
        }

        public Task<StringNarrationCashflowListResult> GetCashflowAsync(StringNarrationCashflowQuery query, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("串述业务数据服务未配置。");
        }

        public Task<StringNarrationCashflowHealthDashboardResult> GetCashflowHealthDashboardAsync(
            StringNarrationCashflowHealthDashboardRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("串述业务数据服务未配置。");
        }

        public Task<StringNarrationInventoryManagementDashboardResult> GetInventoryManagementDashboardAsync(
            StringNarrationInventoryManagementDashboardRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("串述业务数据服务未配置。");
        }
    }
}
