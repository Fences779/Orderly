using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;

namespace Orderly.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly ICustomerService _customerService;
    private readonly IOrderService _orderService;
    private readonly IDealService _dealService;
    private readonly IFollowUpService _followUpService;
    private readonly INoteService _noteService;
    private readonly IActivityLogService _activityLogService;
    private readonly IPriceAdjustmentService _priceAdjustmentService;
    private readonly IReplyTemplateRepository _replyTemplateRepository;
    private readonly IAppSettingRepository _settingRepository;
    private readonly IClipboardService _clipboardService;

    public MainViewModel(
        ICustomerRepository customerRepository,
        IOrderRepository orderRepository,
        ICustomerService customerService,
        IOrderService orderService,
        IDealService dealService,
        IFollowUpService followUpService,
        INoteService noteService,
        IActivityLogService activityLogService,
        IPriceAdjustmentService priceAdjustmentService,
        IReplyTemplateRepository replyTemplateRepository,
        IAppSettingRepository settingRepository,
        IClipboardService clipboardService,
        string databasePath)
    {
        _customerRepository = customerRepository;
        _orderRepository = orderRepository;
        _customerService = customerService;
        _orderService = orderService;
        _dealService = dealService;
        _followUpService = followUpService;
        _noteService = noteService;
        _activityLogService = activityLogService;
        _priceAdjustmentService = priceAdjustmentService;
        _replyTemplateRepository = replyTemplateRepository;
        _settingRepository = settingRepository;
        _clipboardService = clipboardService;
        DatabasePath = databasePath;
        InitializeFilterOptions();
    }

    public ObservableCollection<Customer> Customers { get; } = new();
    public ObservableCollection<OrderListItem> Orders { get; } = new();
    public ObservableCollection<Deal> Deals { get; } = new();
    public ObservableCollection<FollowUp> FollowUps { get; } = new();
    public ObservableCollection<CustomerNote> CustomerNotes { get; } = new();
    public ObservableCollection<PriceAdjustment> PriceAdjustments { get; } = new();
    public ObservableCollection<ActivityLog> ActivityLogs { get; } = new();
    public ObservableCollection<ReplyTemplate> ReplyTemplates { get; } = new();
    public ObservableCollection<string> Sections { get; } = new(new[] { "工作台", "客户/订单", "话术库", "设置" });
    public ObservableCollection<SearchFilterOption> SearchFilterOptions { get; } = new();
    public ObservableCollection<QuickFilterOption> QuickFilterOptions { get; } = new();
    public ObservableCollection<CustomerStatus> CustomerStatusOptions { get; } = new(Enum.GetValues<CustomerStatus>());
    public ObservableCollection<OrderStatus> OrderStatusOptions { get; } = new(Enum.GetValues<OrderStatus>());

    public string DatabasePath { get; }

    private List<Customer> _allCustomers = new();
    private List<OrderListItem> _allOrders = new();
    private List<Deal> _allDeals = new();
    private List<FollowUp> _allFollowUps = new();
    private List<CustomerNote> _allCustomerNotes = new();

    [ObservableProperty]
    private string selectedSection = "工作台";

    [ObservableProperty]
    private string searchKeyword = string.Empty;

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
    [NotifyCanExecuteChangedFor(nameof(AddOrderCommand))]
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
    [NotifyCanExecuteChangedFor(nameof(AddNoteCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddFollowUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddOrderCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddPriceAdjustmentCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeDealStageCommand))]
    [NotifyCanExecuteChangedFor(nameof(AdvanceDealStageCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeCustomerStatusCommand))]
    private Customer? selectedCustomer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentDealStage))]
    private Deal? selectedDeal;

    [ObservableProperty]
    private AppPreferences preferences = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusError))]
    [NotifyPropertyChangedFor(nameof(StatusMessageTitle))]
    [NotifyPropertyChangedFor(nameof(CustomersEmptyStateText))]
    [NotifyPropertyChangedFor(nameof(OrdersEmptyStateText))]
    private string statusMessage = "准备就绪";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddNoteCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddFollowUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddCustomerCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddOrderCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddPriceAdjustmentCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeDealStageCommand))]
    [NotifyCanExecuteChangedFor(nameof(AdvanceDealStageCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeCustomerStatusCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeOrderStatusCommand))]
    [NotifyCanExecuteChangedFor(nameof(CompleteFollowUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(SnoozeFollowUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelFollowUpCommand))]
    private bool isLoading;

    [ObservableProperty]
    private string? loadError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddNoteCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddFollowUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddCustomerCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddOrderCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddPriceAdjustmentCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeDealStageCommand))]
    [NotifyCanExecuteChangedFor(nameof(AdvanceDealStageCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeCustomerStatusCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeOrderStatusCommand))]
    [NotifyCanExecuteChangedFor(nameof(CompleteFollowUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(SnoozeFollowUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelFollowUpCommand))]
    private bool isSaving;

    private bool _isSynchronizingSelection;
    private int _detailLoadVersion;
}
