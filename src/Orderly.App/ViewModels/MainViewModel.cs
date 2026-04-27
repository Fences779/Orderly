using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orderly.App.Views;
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

    public MerchantOrder? SelectedOrder => SelectedOrderItem?.Order;
    public string SelectedStatusLabel => SelectedOrder is null ? string.Empty : OrderStatusCatalog.GetLabel(SelectedOrder.Status);
    public string CustomerStatusLabel => SelectedCustomer is null ? string.Empty : GetCustomerStatusLabel(SelectedCustomer.Status);
    public string CustomerPriorityLabel => SelectedCustomer is null ? string.Empty : GetCustomerPriorityLabel(SelectedCustomer.Priority);
    public string CurrentDealStage => SelectedDeal is null ? "无" : GetDealStageLabel(SelectedDeal.Stage);
    public string LatestNote => CustomerNotes.FirstOrDefault()?.Content ?? "暂无备注";
    public string SelectedCustomerNameText => string.IsNullOrWhiteSpace(SelectedCustomer?.Name) ? "未选择客户" : SelectedCustomer.Name;
    public string SelectedOrderHeadline => string.IsNullOrWhiteSpace(SelectedOrder?.Title) ? "未选择订单" : SelectedOrder.Title;
    public string SelectedOrderRequirementSummary => string.IsNullOrWhiteSpace(SelectedOrder?.Requirement)
        ? "当前订单暂无需求摘要"
        : SelectedOrder.Requirement;
    public string SelectedOrderStatusText => SelectedOrder is null ? "未选择订单" : SelectedStatusLabel;
    public string SelectedOrderAmountText => SelectedOrder is null
        ? "金额待确认"
        : SelectedOrder.Amount > 0 ? $"¥{SelectedOrder.Amount:N0}" : "待报价";
    public string SelectedNextFollowUpText => SelectedOrder?.NextFollowUpAt?.ToString("yyyy-MM-dd HH:mm") ?? "暂无安排";
    public string SelectedOrderRequirementText => string.IsNullOrWhiteSpace(SelectedOrder?.Requirement)
        ? "暂无需求记录"
        : SelectedOrder.Requirement;
    public string SelectedSourcePlatformText => string.IsNullOrWhiteSpace(SelectedOrder?.SourcePlatform)
        ? "未标记来源"
        : SelectedOrder.SourcePlatform;
    public string SelectedChannelText => string.IsNullOrWhiteSpace(SelectedOrder?.Channel)
        ? "未标记渠道"
        : SelectedOrder.Channel;
    public string SelectedExternalIdText => string.IsNullOrWhiteSpace(SelectedOrder?.ExternalId)
        ? "未关联外部ID"
        : SelectedOrder.ExternalId;
    public string CustomerRemarkText => string.IsNullOrWhiteSpace(SelectedCustomer?.Remark)
        ? "暂无客户备注"
        : SelectedCustomer.Remark;
    public bool IsStatusError =>
        StatusMessage.Contains("失败", StringComparison.Ordinal) ||
        StatusMessage.Contains("错误", StringComparison.Ordinal);
    public string StatusMessageTitle => IsStatusError ? "需要处理" : "当前状态";
    public bool HasSelectedCustomer => SelectedCustomer is not null;
    public bool HasSelectedOrder => SelectedOrder is not null;
    public bool HasDeals => Deals.Count > 0;
    public bool HasFollowUps => FollowUps.Count > 0;
    public bool HasCustomerNotes => CustomerNotes.Count > 0;
    public bool HasPriceAdjustments => PriceAdjustments.Count > 0;
    public bool HasActivityLogs => ActivityLogs.Count > 0;
    public bool HasCustomers => Customers.Count > 0;
    public bool HasOrders => Orders.Count > 0;
    public int DealsCount => Deals.Count;
    public int FollowUpsCount => FollowUps.Count;
    public int CustomerNotesCount => CustomerNotes.Count;
    public int PriceAdjustmentsCount => PriceAdjustments.Count;
    public int ActivityLogsCount => ActivityLogs.Count;
    public bool IsBusy => IsLoading || IsSaving;
    public string OrderDetailsEmptyMessage => SelectedCustomer is null ? "请选择订单或客户" : "当前客户暂无关联订单";
    public int PendingCount => _allOrders.Count(item => item.Order.Status is OrderStatus.PendingCommunication or OrderStatus.PendingQuote or OrderStatus.PendingFollowUp);
    public int WonCount => _allOrders.Count(item => item.Order.Status == OrderStatus.Won);
    public decimal TotalAmount => _allOrders.Sum(item => item.Order.Amount);
    public string CustomersCountText => $"{Customers.Count} 个客户";
    public string OrdersCountText => $"{Orders.Count} 个订单";
    public string CustomersEmptyStateText => IsStatusError
        ? $"客户列表暂时不可用\n{StatusMessage}"
        : "还没有客户记录\n本地数据加载完成后会显示在这里";
    public string OrdersEmptyStateText => IsStatusError
        ? $"订单列表暂时不可用\n{StatusMessage}"
        : "还没有订单记录\n本地数据加载完成后会显示在这里";

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        LoadError = null;
        StatusMessage = "正在加载本地数据...";

        try
        {
            Preferences = await _settingRepository.GetPreferencesAsync(cancellationToken);
            var customers = await _customerRepository.GetAllAsync(cancellationToken);
            var orders = await _orderRepository.GetRecentAsync(cancellationToken);
            var deals = await _dealService.GetDealsAsync(cancellationToken);
            var followUps = await _followUpService.GetFollowUpsAsync(cancellationToken);
            var notes = await _noteService.GetNotesAsync(cancellationToken);
            var templates = await _replyTemplateRepository.GetAllAsync(cancellationToken);

            _allCustomers = customers.ToList();
            _allOrders = orders.Select(order => new OrderListItem(order)).ToList();
            _allDeals = deals.ToList();
            _allFollowUps = followUps.ToList();
            _allCustomerNotes = notes.ToList();
            ReplaceCollection(ReplyTemplates, templates);
            ApplyFilters();

            SelectedOrderItem = Orders.FirstOrDefault();
            if (SelectedOrderItem is null)
            {
                SelectedCustomer = Customers.FirstOrDefault();
            }

            OnSummaryChanged();
            StatusMessage = $"已加载 {Customers.Count} 个客户、{Orders.Count} 个订单";
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
            StatusMessage = $"加载失败：{ex.Message}";
            ClearDetails();
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedOrderItemChanged(OrderListItem? value)
    {
        OnPropertyChanged(nameof(SelectedOrder));
        OnPropertyChanged(nameof(SelectedStatusLabel));
        OnPropertyChanged(nameof(HasSelectedOrder));
        OnPropertyChanged(nameof(OrderDetailsEmptyMessage));
        SelectedOrderStatusInput = value?.Order.Status ?? OrderStatus.PendingCommunication;

        if (_isSynchronizingSelection)
        {
            return;
        }

        _ = SelectCustomerForOrderAsync(value?.Order);
    }

    partial void OnSelectedCustomerChanged(Customer? value)
    {
        StatusMessage = value is null ? "未选择客户" : $"已选择客户：{value.Name}";
        SelectedCustomerStatusInput = value?.Status ?? CustomerStatus.Active;

        if (!_isSynchronizingSelection)
        {
            SyncSelectedOrderForCustomer(value);
        }

        _ = LoadSelectedCustomerDetailsAsync(value);
    }

    partial void OnSelectedDealChanged(Deal? value)
    {
        OnPropertyChanged(nameof(CurrentDealStage));
    }

    partial void OnSearchKeywordChanged(string value)
    {
        ApplyFilters();
    }

    partial void OnSelectedStatusFilterChanged(SearchFilterOption value)
    {
        ApplyFilters();
    }

    partial void OnSelectedQuickFilterChanged(QuickFilterOption value)
    {
        ApplyFilters();
    }

    [RelayCommand]
    private void SelectSection(string section)
    {
        if (!string.IsNullOrWhiteSpace(section))
        {
            SelectedSection = section;
        }
    }

    [RelayCommand]
    private void SelectOrder(OrderListItem? order)
    {
        if (order is not null)
        {
            SelectedOrderItem = order;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        await LoadAsync();
    }

    [RelayCommand(CanExecute = nameof(CanAddCustomer))]
    private async Task AddCustomerAsync()
    {
        try
        {
            StatusMessage = "正在新增客户...";
            var (dialog, result) = await ShowDialogAsync(() => new AddCustomerDialog());

            if (result != true)
            {
                StatusMessage = "已取消新增客户";
                return;
            }

            await ExecuteSaveActionAsync(
                busyMessage: "正在保存客户...",
                successMessage: "客户已保存",
                errorTitle: "新增客户失败",
                errorStatusPrefix: "保存客户失败",
                action: async () =>
                {
                    var created = await _customerService.SaveCustomerAsync(dialog.Customer);
                    await ReloadListDataAsync(selectedCustomerId: created.Id);
                    SelectCustomerById(created.Id);
                });
        }
        catch (Exception ex)
        {
            StatusMessage = $"新增客户失败：{ex.Message}";
            ShowErrorMessage("新增客户失败", ex);
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddOrder))]
    private async Task AddOrderAsync()
    {
        if (_allCustomers.Count == 0)
        {
            System.Windows.MessageBox.Show(GetDialogOwner(), "请先新增客户。", "创建订单", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            StatusMessage = "正在创建订单...";
            var (dialog, result) = await ShowDialogAsync(() => new AddOrderDialog(_allCustomers, SelectedCustomer));

            if (result != true || dialog.SelectedCustomer is null)
            {
                StatusMessage = "已取消创建订单";
                return;
            }

            await ExecuteSaveActionAsync(
                busyMessage: "正在保存订单...",
                successMessage: "订单已创建",
                errorTitle: "创建订单失败",
                errorStatusPrefix: "保存订单失败",
                action: async () =>
                {
                    var customer = dialog.SelectedCustomer;
                    var created = await _orderService.SaveOrderAsync(new MerchantOrder
                    {
                        CustomerId = customer.Id,
                        Title = dialog.OrderTitle,
                        Requirement = dialog.Requirement,
                        Amount = dialog.Amount,
                        Status = dialog.Status,
                        NextFollowUpAt = dialog.NextFollowUpAt,
                        SourcePlatform = customer.SourcePlatform,
                        Channel = customer.Channel
                    });

                    if (!string.IsNullOrWhiteSpace(dialog.Remark))
                    {
                        await _noteService.SaveNoteAsync(new CustomerNote
                        {
                            CustomerId = customer.Id,
                            OrderId = created.Id,
                            Type = NoteType.General,
                            Content = dialog.Remark
                        });
                    }

                    await ReloadListDataAsync(selectedCustomerId: customer.Id, selectedOrderId: created.Id);
                    SelectOrderById(created.Id);
                });
        }
        catch (Exception ex)
        {
            StatusMessage = $"创建订单失败：{ex.Message}";
            ShowErrorMessage("创建订单失败", ex);
        }
    }

    [RelayCommand]
    private void ClearSearchAndFilters()
    {
        SearchKeyword = string.Empty;
        SelectedStatusFilter = SearchFilterOption.All;
        SelectedQuickFilter = QuickFilterOption.All;
        ApplyFilters();
        StatusMessage = "已清空搜索和筛选";
    }

    [RelayCommand(CanExecute = nameof(CanAddNote))]
    private async Task AddNoteAsync()
    {
        var customer = SelectedCustomer;
        if (customer is null)
        {
            ShowNoSelectionMessage();
            return;
        }

        try
        {
            StatusMessage = "正在新增备注...";
            var (dialog, result) = await ShowDialogAsync(() => new AddNoteDialog(ReplyTemplates));

            if (result != true)
            {
                StatusMessage = "已取消新增备注";
                return;
            }

            await ExecuteSaveActionAsync(
                busyMessage: "正在保存备注...",
                successMessage: "备注已保存",
                errorTitle: "新增备注失败",
                errorStatusPrefix: "保存备注失败",
                action: async () =>
                {
                    var metadataJson = CreateNoteActivityMetadataJson(dialog.InsertedTemplate);
                    await _noteService.SaveNoteAsync(new CustomerNote
                    {
                        CustomerId = customer.Id,
                        DealId = SelectedDeal?.Id,
                        OrderId = SelectedOrder?.Id,
                        Type = dialog.SelectedNoteType,
                        Content = dialog.NoteContent
                    }, metadataJson);

                    await ReloadSelectedCustomerDetailsAsync(customer);
                });
        }
        catch (Exception ex)
        {
            StatusMessage = $"新增备注失败：{ex.Message}";
            ShowErrorMessage("新增备注失败", ex);
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddFollowUp))]
    private async Task AddFollowUpAsync()
    {
        var customer = SelectedCustomer;
        if (customer is null)
        {
            ShowNoSelectionMessage();
            return;
        }

        try
        {
            StatusMessage = "正在新增跟进...";
            var (dialog, result) = await ShowDialogAsync(() => new AddFollowUpDialog());

            if (result != true)
            {
                StatusMessage = "已取消新增跟进";
                return;
            }

            await ExecuteSaveActionAsync(
                busyMessage: "正在保存跟进...",
                successMessage: "跟进已保存",
                errorTitle: "新增跟进失败",
                errorStatusPrefix: "保存跟进失败",
                action: async () =>
                {
                    await _followUpService.SaveFollowUpAsync(new FollowUp
                    {
                        CustomerId = customer.Id,
                        DealId = SelectedDeal?.Id,
                        OrderId = SelectedOrder?.Id,
                        Title = dialog.FollowUpTitle,
                        Content = dialog.FollowUpContent,
                        Status = FollowUpStatus.Pending,
                        ScheduledAt = dialog.ScheduledAt
                    });

                    await ReloadSelectedCustomerDetailsAsync(customer);
                });
        }
        catch (Exception ex)
        {
            StatusMessage = $"新增跟进失败：{ex.Message}";
            ShowErrorMessage("新增跟进失败", ex);
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddPriceAdjustment))]
    private async Task AddPriceAdjustmentAsync()
    {
        var customer = SelectedCustomer;
        var order = SelectedOrder;
        if (customer is null || order is null)
        {
            ShowNoSelectionMessage();
            return;
        }

        try
        {
            StatusMessage = "正在新增改价...";
            var (dialog, result) = await ShowDialogAsync(() => new AddPriceAdjustmentDialog(order.Amount));

            if (result != true)
            {
                StatusMessage = "已取消新增改价";
                return;
            }

            await ExecuteSaveActionAsync(
                busyMessage: "正在保存改价...",
                successMessage: "改价已保存",
                errorTitle: "新增改价失败",
                errorStatusPrefix: "保存改价失败",
                action: async () =>
                {
                    await _priceAdjustmentService.SaveAdjustmentAsync(new PriceAdjustment
                    {
                        CustomerId = customer.Id,
                        DealId = SelectedDeal?.Id ?? order.DealId,
                        OrderId = order.Id,
                        OriginalAmount = dialog.OriginalAmount,
                        AdjustedAmount = dialog.AdjustedAmount,
                        Reason = dialog.Reason,
                        Status = dialog.Status,
                        RequestedBy = "local"
                    });

                    await ReloadSelectedCustomerDetailsAsync(customer);
                });
        }
        catch (Exception ex)
        {
            StatusMessage = $"新增改价失败：{ex.Message}";
            ShowErrorMessage("新增改价失败", ex);
        }
    }

    [RelayCommand(CanExecute = nameof(CanChangeDealStage))]
    private async Task ChangeDealStageAsync()
    {
        var customer = SelectedCustomer;
        if (customer is null)
        {
            ShowNoSelectionMessage();
            return;
        }

        try
        {
            await ExecuteSaveActionAsync(
                busyMessage: "正在推进成交阶段...",
                successMessage: "成交阶段已更新",
                errorTitle: "更新成交阶段失败",
                errorStatusPrefix: "更新成交阶段失败",
                action: async () =>
                {
                    var deal = await EnsureSelectedDealAsync(customer);
                    var nextStage = GetNextStage(deal.Stage);
                    await _dealService.UpdateStageAsync(deal.Id, nextStage);
                    await ReloadListDataAsync(selectedCustomerId: customer.Id, selectedOrderId: SelectedOrder?.Id);
                    await ReloadSelectedCustomerDetailsAsync(customer);
                    StatusMessage = $"成交阶段已更新为 {GetDealStageLabel(nextStage)}";
                });
        }
        catch (Exception ex)
        {
            StatusMessage = $"更新成交阶段失败：{ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanChangeDealStage))]
    private Task AdvanceDealStageAsync()
    {
        return ChangeDealStageAsync();
    }

    [RelayCommand(CanExecute = nameof(CanChangeCustomerStatus))]
    private async Task ChangeCustomerStatusAsync()
    {
        var customer = SelectedCustomer;
        if (customer is null)
        {
            ShowNoSelectionMessage();
            return;
        }

        var status = SelectedCustomerStatusInput;
        if (customer.Status == status)
        {
            StatusMessage = "客户状态未变化";
            return;
        }

        await ExecuteSaveActionAsync(
            busyMessage: "正在更新客户状态...",
            successMessage: "客户状态已更新",
            errorTitle: "更新客户状态失败",
            errorStatusPrefix: "更新客户状态失败",
            action: async () =>
            {
                await _customerService.UpdateStatusAsync(customer.Id, status);
                await ReloadListDataAsync(selectedCustomerId: customer.Id, selectedOrderId: SelectedOrder?.Id);
                SelectCustomerById(customer.Id);
                await ReloadSelectedCustomerDetailsAsync(SelectedCustomer);
            });
    }

    [RelayCommand(CanExecute = nameof(CanChangeOrderStatus))]
    private async Task ChangeOrderStatusAsync()
    {
        var order = SelectedOrder;
        if (order is null)
        {
            ShowNoSelectionMessage();
            return;
        }

        var status = SelectedOrderStatusInput;
        if (order.Status == status)
        {
            StatusMessage = "订单状态未变化";
            return;
        }

        await ExecuteSaveActionAsync(
            busyMessage: "正在更新订单状态...",
            successMessage: "订单状态已更新",
            errorTitle: "更新订单状态失败",
            errorStatusPrefix: "更新订单状态失败",
            action: async () =>
            {
                await _orderService.UpdateStatusAsync(order.Id, status);
                await ReloadListDataAsync(selectedCustomerId: order.CustomerId, selectedOrderId: order.Id);
                SelectOrderById(order.Id);
                await ReloadSelectedCustomerDetailsAsync(SelectedCustomer);
            });
    }

    [RelayCommand(CanExecute = nameof(CanCompleteFollowUp))]
    private async Task CompleteFollowUpAsync(FollowUp? followUp)
    {
        if (followUp is null || SelectedCustomer is null)
        {
            return;
        }

        await ExecuteSaveActionAsync(
            busyMessage: "正在完成跟进...",
            successMessage: "跟进已完成",
            errorTitle: "完成跟进失败",
            errorStatusPrefix: "完成跟进失败",
            action: async () =>
            {
                await _followUpService.CompleteFollowUpAsync(followUp.Id, DateTime.Now);
                await ReloadListDataAsync(selectedCustomerId: followUp.CustomerId, selectedOrderId: SelectedOrder?.Id);
                await ReloadSelectedCustomerDetailsAsync(SelectedCustomer);
            });
    }

    [RelayCommand(CanExecute = nameof(CanSnoozeFollowUp))]
    private async Task SnoozeFollowUpAsync(FollowUp? followUp)
    {
        if (followUp is null || SelectedCustomer is null)
        {
            return;
        }

        var (dialog, result) = await ShowDialogAsync(() => new SnoozeFollowUpDialog(followUp.ScheduledAt));
        if (result != true)
        {
            StatusMessage = "已取消延期跟进";
            return;
        }

        await ExecuteSaveActionAsync(
            busyMessage: "正在延期跟进...",
            successMessage: "跟进已延期",
            errorTitle: "延期跟进失败",
            errorStatusPrefix: "延期跟进失败",
            action: async () =>
            {
                await _followUpService.SnoozeFollowUpAsync(followUp.Id, dialog.ScheduledAt);
                await ReloadListDataAsync(selectedCustomerId: followUp.CustomerId, selectedOrderId: SelectedOrder?.Id);
                await ReloadSelectedCustomerDetailsAsync(SelectedCustomer);
            });
    }

    [RelayCommand(CanExecute = nameof(CanCancelFollowUp))]
    private async Task CancelFollowUpAsync(FollowUp? followUp)
    {
        if (followUp is null || SelectedCustomer is null)
        {
            return;
        }

        await ExecuteSaveActionAsync(
            busyMessage: "正在取消跟进...",
            successMessage: "跟进已取消",
            errorTitle: "取消跟进失败",
            errorStatusPrefix: "取消跟进失败",
            action: async () =>
            {
                await _followUpService.CancelFollowUpAsync(followUp.Id);
                await ReloadListDataAsync(selectedCustomerId: followUp.CustomerId, selectedOrderId: SelectedOrder?.Id);
                await ReloadSelectedCustomerDetailsAsync(SelectedCustomer);
            });
    }

    [RelayCommand]
    private void CopyTemplate(ReplyTemplate? template)
    {
        if (template is null)
        {
            return;
        }

        _clipboardService.SetText(template.Content);
        StatusMessage = $"已复制话术：{template.Title}";
    }

    private async Task LoadSelectedCustomerDetailsAsync(Customer? customer)
    {
        var loadVersion = Interlocked.Increment(ref _detailLoadVersion);
        ClearDetails();
        if (customer is null)
        {
            return;
        }

        try
        {
            var deals = await _dealService.GetCustomerDealsAsync(customer.Id);
            var followUps = await _followUpService.GetCustomerFollowUpsAsync(customer.Id);
            var notes = await _noteService.GetCustomerNotesAsync(customer.Id);
            var adjustments = await _priceAdjustmentService.GetCustomerAdjustmentsAsync(customer.Id);
            var activities = await _activityLogService.GetCustomerActivitiesAsync(customer.Id);

            if (loadVersion != _detailLoadVersion || SelectedCustomer?.Id != customer.Id)
            {
                return;
            }

            ReplaceCollection(Deals, deals);
            ReplaceCollection(FollowUps, followUps.OrderByDescending(followUp => followUp.ScheduledAt));
            ReplaceCollection(CustomerNotes, notes.OrderByDescending(note => note.CreatedAt));
            ReplaceCollection(PriceAdjustments, adjustments.OrderByDescending(adjustment => adjustment.CreatedAt));
            ReplaceCollection(ActivityLogs, activities.OrderByDescending(activity => activity.CreatedAt));

            SelectedDeal = Deals.FirstOrDefault(deal => deal.Id == SelectedOrder?.DealId) ?? Deals.FirstOrDefault();
            OnDetailStateChanged();
        }
        catch (Exception ex)
        {
            if (loadVersion != _detailLoadVersion)
            {
                return;
            }

            LoadError = ex.Message;
            StatusMessage = $"加载客户详情失败：{ex.Message}";
        }
    }

    private async Task SelectCustomerForOrderAsync(MerchantOrder? order)
    {
        try
        {
            if (order is null)
            {
                SelectedCustomer = null;
                return;
            }

            Customer? customer = null;
            if (order.CustomerId > 0)
            {
                customer = Customers.FirstOrDefault(item => item.Id == order.CustomerId)
                    ?? order.Customer
                    ?? await _customerRepository.GetByIdAsync(order.CustomerId);
            }
            else if (order.Customer is not null)
            {
                customer = order.Customer;
            }

            if (SelectedOrder?.Id != order.Id)
            {
                return;
            }

            if (customer is null)
            {
                SelectedCustomer = null;
                StatusMessage = "未选择客户";
                return;
            }

            if (SelectedCustomer?.Id == customer.Id)
            {
                SelectedCustomer = customer;
                await ReloadSelectedCustomerDetailsAsync(customer);
                return;
            }

            SelectedCustomer = customer;
        }
        catch (Exception ex)
        {
            SelectedCustomer = null;
            StatusMessage = $"选择客户失败：{ex.Message}";
        }
    }

    private void SyncSelectedOrderForCustomer(Customer? customer)
    {
        if (customer is null)
        {
            return;
        }

        var matchingOrder = Orders.FirstOrDefault(item => item.Order.CustomerId == customer.Id);
        if (SelectedOrderItem?.Order.CustomerId == customer.Id && matchingOrder is not null)
        {
            return;
        }

        _isSynchronizingSelection = true;
        try
        {
            SelectedOrderItem = matchingOrder;
        }
        finally
        {
            _isSynchronizingSelection = false;
        }
    }

    private async Task<Deal> EnsureSelectedDealAsync(Customer customer)
    {
        if (SelectedDeal is not null)
        {
            return SelectedDeal;
        }

        var order = SelectedOrder;
        var deal = new Deal
        {
            CustomerId = customer.Id,
            Title = order?.Title ?? $"{customer.Name} 成交机会",
            Stage = DealStage.New,
            EstimatedAmount = order?.Amount ?? 0,
            Requirement = order?.Requirement ?? string.Empty,
            SourcePlatform = order?.SourcePlatform ?? customer.SourcePlatform,
            Channel = order?.Channel ?? customer.Channel
        };

        return await _dealService.SaveDealAsync(deal);
    }

    private static DealStage GetNextStage(DealStage stage)
    {
        return stage switch
        {
            DealStage.New => DealStage.Qualified,
            DealStage.Qualified => DealStage.Quoting,
            DealStage.Quoting => DealStage.Negotiating,
            DealStage.Negotiating => DealStage.Won,
            DealStage.Won => DealStage.Archived,
            DealStage.Lost => DealStage.Archived,
            _ => DealStage.New
        };
    }

    private void InitializeFilterOptions()
    {
        SearchFilterOptions.Add(SearchFilterOption.All);
        foreach (var status in Enum.GetValues<CustomerStatus>())
        {
            SearchFilterOptions.Add(new SearchFilterOption($"客户：{GetCustomerStatusLabel(status)}", SearchFilterKind.CustomerStatus, status));
        }

        foreach (var priority in Enum.GetValues<CustomerPriority>())
        {
            SearchFilterOptions.Add(new SearchFilterOption($"优先级：{GetCustomerPriorityLabel(priority)}", SearchFilterKind.CustomerPriority, priority));
        }

        foreach (var status in Enum.GetValues<OrderStatus>())
        {
            SearchFilterOptions.Add(new SearchFilterOption($"订单：{OrderStatusCatalog.GetLabel(status)}", SearchFilterKind.OrderStatus, status));
        }

        foreach (var stage in Enum.GetValues<DealStage>())
        {
            SearchFilterOptions.Add(new SearchFilterOption($"Deal：{GetDealStageLabel(stage)}", SearchFilterKind.DealStage, stage));
        }

        foreach (var status in Enum.GetValues<FollowUpStatus>())
        {
            SearchFilterOptions.Add(new SearchFilterOption($"跟进：{GetFollowUpStatusLabel(status)}", SearchFilterKind.FollowUpStatus, status));
        }

        QuickFilterOptions.Add(QuickFilterOption.All);
        QuickFilterOptions.Add(new QuickFilterOption("今日跟进", QuickFilterKind.TodayFollowUp));
        QuickFilterOptions.Add(new QuickFilterOption("逾期跟进", QuickFilterKind.OverdueFollowUp));
        QuickFilterOptions.Add(new QuickFilterOption("明日跟进", QuickFilterKind.TomorrowFollowUp));
        QuickFilterOptions.Add(new QuickFilterOption("待处理订单", QuickFilterKind.PendingOrders));
        QuickFilterOptions.Add(new QuickFilterOption("已成交订单", QuickFilterKind.WonOrders));
    }

    private async Task ReloadListDataAsync(int? selectedCustomerId = null, int? selectedOrderId = null)
    {
        var customers = await _customerRepository.GetAllAsync();
        var orders = await _orderRepository.GetRecentAsync();
        var deals = await _dealService.GetDealsAsync();
        var followUps = await _followUpService.GetFollowUpsAsync();
        var notes = await _noteService.GetNotesAsync();

        _allCustomers = customers.ToList();
        _allOrders = orders.Select(order => new OrderListItem(order)).ToList();
        _allDeals = deals.ToList();
        _allFollowUps = followUps.ToList();
        _allCustomerNotes = notes.ToList();
        ApplyFilters(selectedCustomerId ?? SelectedCustomer?.Id, selectedOrderId ?? SelectedOrder?.Id);
        AddOrderCommand.NotifyCanExecuteChanged();
    }

    private void ApplyFilters()
    {
        ApplyFilters(SelectedCustomer?.Id, SelectedOrder?.Id);
    }

    private void ApplyFilters(int? selectedCustomerId, int? selectedOrderId)
    {
        var visibleCustomers = _allCustomers.Where(CustomerMatchesFilters).ToList();
        var visibleOrders = _allOrders.Where(OrderMatchesFilters).ToList();

        if (selectedCustomerId is int customerId && visibleCustomers.All(customer => customer.Id != customerId))
        {
            var selectedCustomer = _allCustomers.FirstOrDefault(customer => customer.Id == customerId);
            if (selectedCustomer is not null)
            {
                visibleCustomers.Insert(0, selectedCustomer);
            }
        }

        if (selectedOrderId is int orderId && visibleOrders.All(order => order.Id != orderId))
        {
            var selectedOrder = _allOrders.FirstOrDefault(order => order.Id == orderId);
            if (selectedOrder is not null)
            {
                visibleOrders.Insert(0, selectedOrder);
            }
        }

        ReplaceCollection(Customers, visibleCustomers);
        ReplaceCollection(Orders, visibleOrders);

        if (selectedCustomerId is int restoreCustomerId)
        {
            SelectedCustomer = Customers.FirstOrDefault(customer => customer.Id == restoreCustomerId);
        }

        if (selectedOrderId is int restoreOrderId)
        {
            SelectedOrderItem = Orders.FirstOrDefault(order => order.Id == restoreOrderId);
        }

        OnSummaryChanged();
    }

    private bool CustomerMatchesFilters(Customer customer)
    {
        return CustomerMatchesSearch(customer) &&
               CustomerMatchesStatusFilter(customer) &&
               CustomerMatchesQuickFilter(customer);
    }

    private bool OrderMatchesFilters(OrderListItem item)
    {
        return OrderMatchesSearch(item) &&
               OrderMatchesStatusFilter(item) &&
               OrderMatchesQuickFilter(item);
    }

    private bool CustomerMatchesSearch(Customer customer)
    {
        var keyword = SearchKeyword.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return true;
        }

        var relatedOrders = _allOrders.Where(order => order.Order.CustomerId == customer.Id).Select(order => order.Order);
        var relatedDeals = _allDeals.Where(deal => deal.CustomerId == customer.Id);
        var relatedFollowUps = _allFollowUps.Where(followUp => followUp.CustomerId == customer.Id);
        var relatedNotes = _allCustomerNotes.Where(note => note.CustomerId == customer.Id);

        return ContainsAny(keyword,
            customer.Name,
            customer.ContactHandle,
            customer.Phone,
            customer.SourcePlatform,
            customer.Channel,
            customer.Remark,
            GetCustomerStatusLabel(customer.Status),
            GetCustomerPriorityLabel(customer.Priority)) ||
            relatedOrders.Any(order => ContainsAny(keyword, order.Title, order.Requirement, order.SourcePlatform, order.Channel, OrderStatusCatalog.GetLabel(order.Status))) ||
            relatedDeals.Any(deal => ContainsAny(keyword, deal.Title, deal.Requirement, GetDealStageLabel(deal.Stage))) ||
            relatedFollowUps.Any(followUp => ContainsAny(keyword, followUp.Title, followUp.Content, GetFollowUpStatusLabel(followUp.Status))) ||
            relatedNotes.Any(note => ContainsAny(keyword, note.Content, note.Type.ToString()));
    }

    private bool OrderMatchesSearch(OrderListItem item)
    {
        var keyword = SearchKeyword.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return true;
        }

        var order = item.Order;
        var customer = order.Customer ?? _allCustomers.FirstOrDefault(candidate => candidate.Id == order.CustomerId);
        var relatedDeals = _allDeals.Where(deal => deal.Id == order.DealId || deal.CustomerId == order.CustomerId);
        var relatedFollowUps = _allFollowUps.Where(followUp => followUp.OrderId == order.Id || followUp.CustomerId == order.CustomerId);
        var relatedNotes = _allCustomerNotes.Where(note => note.OrderId == order.Id || note.CustomerId == order.CustomerId);

        return ContainsAny(keyword,
            order.Title,
            order.Requirement,
            order.SourcePlatform,
            order.Channel,
            order.ExternalId,
            item.CustomerNameDisplay,
            customer?.Name,
            customer?.ContactHandle,
            customer?.Phone,
            customer?.SourcePlatform,
            customer?.Remark,
            OrderStatusCatalog.GetLabel(order.Status)) ||
            relatedDeals.Any(deal => ContainsAny(keyword, deal.Title, deal.Requirement, GetDealStageLabel(deal.Stage))) ||
            relatedFollowUps.Any(followUp => ContainsAny(keyword, followUp.Title, followUp.Content, GetFollowUpStatusLabel(followUp.Status))) ||
            relatedNotes.Any(note => ContainsAny(keyword, note.Content, note.Type.ToString()));
    }

    private bool CustomerMatchesStatusFilter(Customer customer)
    {
        return SelectedStatusFilter.Kind switch
        {
            SearchFilterKind.All => true,
            SearchFilterKind.CustomerStatus => SelectedStatusFilter.Value is CustomerStatus status && customer.Status == status,
            SearchFilterKind.CustomerPriority => SelectedStatusFilter.Value is CustomerPriority priority && customer.Priority == priority,
            SearchFilterKind.OrderStatus => SelectedStatusFilter.Value is OrderStatus status && _allOrders.Any(order => order.Order.CustomerId == customer.Id && order.Order.Status == status),
            SearchFilterKind.DealStage => SelectedStatusFilter.Value is DealStage stage && _allDeals.Any(deal => deal.CustomerId == customer.Id && deal.Stage == stage),
            SearchFilterKind.FollowUpStatus => SelectedStatusFilter.Value is FollowUpStatus status && _allFollowUps.Any(followUp => followUp.CustomerId == customer.Id && followUp.Status == status),
            _ => true
        };
    }

    private bool OrderMatchesStatusFilter(OrderListItem item)
    {
        var order = item.Order;
        var customer = order.Customer ?? _allCustomers.FirstOrDefault(candidate => candidate.Id == order.CustomerId);
        return SelectedStatusFilter.Kind switch
        {
            SearchFilterKind.All => true,
            SearchFilterKind.CustomerStatus => SelectedStatusFilter.Value is CustomerStatus status && customer?.Status == status,
            SearchFilterKind.CustomerPriority => SelectedStatusFilter.Value is CustomerPriority priority && customer?.Priority == priority,
            SearchFilterKind.OrderStatus => SelectedStatusFilter.Value is OrderStatus status && order.Status == status,
            SearchFilterKind.DealStage => SelectedStatusFilter.Value is DealStage stage && _allDeals.Any(deal => (deal.Id == order.DealId || deal.CustomerId == order.CustomerId) && deal.Stage == stage),
            SearchFilterKind.FollowUpStatus => SelectedStatusFilter.Value is FollowUpStatus status && _allFollowUps.Any(followUp => (followUp.OrderId == order.Id || followUp.CustomerId == order.CustomerId) && followUp.Status == status),
            _ => true
        };
    }

    private bool CustomerMatchesQuickFilter(Customer customer)
    {
        return SelectedQuickFilter.Kind switch
        {
            QuickFilterKind.All => true,
            QuickFilterKind.TodayFollowUp => HasFollowUpOn(customer.Id, DateTime.Today),
            QuickFilterKind.OverdueFollowUp => HasOverdueFollowUp(customer.Id),
            QuickFilterKind.TomorrowFollowUp => HasFollowUpOn(customer.Id, DateTime.Today.AddDays(1)),
            QuickFilterKind.PendingOrders => _allOrders.Any(order => order.Order.CustomerId == customer.Id && IsPendingOrder(order.Order.Status)),
            QuickFilterKind.WonOrders => _allOrders.Any(order => order.Order.CustomerId == customer.Id && order.Order.Status == OrderStatus.Won),
            _ => true
        };
    }

    private bool OrderMatchesQuickFilter(OrderListItem item)
    {
        var order = item.Order;
        return SelectedQuickFilter.Kind switch
        {
            QuickFilterKind.All => true,
            QuickFilterKind.TodayFollowUp => HasFollowUpOn(order.CustomerId, DateTime.Today, order.Id) || IsOrderFollowUpOn(order, DateTime.Today),
            QuickFilterKind.OverdueFollowUp => HasOverdueFollowUp(order.CustomerId, order.Id) || IsOrderFollowUpOverdue(order),
            QuickFilterKind.TomorrowFollowUp => HasFollowUpOn(order.CustomerId, DateTime.Today.AddDays(1), order.Id) || IsOrderFollowUpOn(order, DateTime.Today.AddDays(1)),
            QuickFilterKind.PendingOrders => IsPendingOrder(order.Status),
            QuickFilterKind.WonOrders => order.Status == OrderStatus.Won,
            _ => true
        };
    }

    private static bool ContainsAny(string keyword, params string?[] values)
    {
        return values.Any(value => !string.IsNullOrWhiteSpace(value) && value.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private bool HasFollowUpOn(int customerId, DateTime date, int? orderId = null)
    {
        return _allFollowUps.Any(followUp =>
            followUp.CustomerId == customerId &&
            (orderId is null || followUp.OrderId == orderId) &&
            CanTransitionFollowUp(followUp.Status) &&
            followUp.ScheduledAt.Date == date.Date);
    }

    private bool HasOverdueFollowUp(int customerId, int? orderId = null)
    {
        return _allFollowUps.Any(followUp =>
            followUp.CustomerId == customerId &&
            (orderId is null || followUp.OrderId == orderId) &&
            CanTransitionFollowUp(followUp.Status) &&
            followUp.ScheduledAt.Date < DateTime.Today);
    }

    private static bool IsOrderFollowUpOn(Order order, DateTime date)
    {
        return order.NextFollowUpAt?.Date == date.Date;
    }

    private static bool IsOrderFollowUpOverdue(Order order)
    {
        return order.NextFollowUpAt?.Date < DateTime.Today && IsPendingOrder(order.Status);
    }

    private static bool IsPendingOrder(OrderStatus status)
    {
        return status is OrderStatus.PendingCommunication or OrderStatus.PendingQuote or OrderStatus.PendingFollowUp;
    }

    private void SelectCustomerById(int customerId)
    {
        SelectedCustomer = Customers.FirstOrDefault(customer => customer.Id == customerId)
            ?? _allCustomers.FirstOrDefault(customer => customer.Id == customerId);
    }

    private void SelectOrderById(int orderId)
    {
        SelectedOrderItem = Orders.FirstOrDefault(order => order.Id == orderId)
            ?? _allOrders.FirstOrDefault(order => order.Id == orderId);
    }

    private static string GetCustomerStatusLabel(CustomerStatus status)
    {
        return CustomerStatusCatalog.GetLabel(status);
    }

    private static string GetCustomerPriorityLabel(CustomerPriority priority)
    {
        return priority switch
        {
            CustomerPriority.Low => "低",
            CustomerPriority.Normal => "普通",
            CustomerPriority.High => "高",
            CustomerPriority.Critical => "紧急",
            _ => priority.ToString()
        };
    }

    private static string GetDealStageLabel(DealStage stage)
    {
        return stage switch
        {
            DealStage.New => "新建",
            DealStage.Qualified => "已确认",
            DealStage.Quoting => "报价中",
            DealStage.Negotiating => "谈判中",
            DealStage.Won => "已成交",
            DealStage.Lost => "已丢单",
            DealStage.Archived => "已归档",
            _ => stage.ToString()
        };
    }

    private static string GetFollowUpStatusLabel(FollowUpStatus status)
    {
        return status switch
        {
            FollowUpStatus.Pending => "待跟进",
            FollowUpStatus.InProgress => "进行中",
            FollowUpStatus.Completed => "已完成",
            FollowUpStatus.Skipped => "已跳过",
            FollowUpStatus.Cancelled => "已取消",
            FollowUpStatus.Overdue => "已逾期",
            _ => status.ToString()
        };
    }

    private void ClearDetails()
    {
        Deals.Clear();
        FollowUps.Clear();
        CustomerNotes.Clear();
        PriceAdjustments.Clear();
        ActivityLogs.Clear();
        SelectedDeal = null;
        OnDetailStateChanged();
    }

    private void OnSummaryChanged()
    {
        OnPropertyChanged(nameof(HasCustomers));
        OnPropertyChanged(nameof(HasOrders));
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(WonCount));
        OnPropertyChanged(nameof(TotalAmount));
        OnPropertyChanged(nameof(CustomersCountText));
        OnPropertyChanged(nameof(OrdersCountText));
        OnPropertyChanged(nameof(CustomersEmptyStateText));
        OnPropertyChanged(nameof(OrdersEmptyStateText));
    }

    private static Window? GetDialogOwner()
    {
        var mainWindow = System.Windows.Application.Current.MainWindow;
        if (mainWindow is MainWindow && mainWindow.IsVisible)
        {
            return mainWindow;
        }

        return System.Windows.Application.Current.Windows
            .OfType<MainWindow>()
            .FirstOrDefault(window => window.IsVisible);
    }

    private static async Task<(TDialog Dialog, bool? Result)> ShowDialogAsync<TDialog>(Func<TDialog> dialogFactory)
        where TDialog : Window
    {
        var dispatcher = System.Windows.Application.Current.MainWindow?.Dispatcher
            ?? System.Windows.Application.Current.Dispatcher;

        (TDialog Dialog, bool? Result) ShowOnDispatcher()
        {
            var dialog = dialogFactory();
            dialog.Owner = GetDialogOwner();
            return (dialog, dialog.ShowDialog());
        }

        return dispatcher.CheckAccess()
            ? ShowOnDispatcher()
            : await dispatcher.InvokeAsync(ShowOnDispatcher);
    }

    private async Task ExecuteSaveActionAsync(
        string busyMessage,
        string successMessage,
        string errorTitle,
        string errorStatusPrefix,
        Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsSaving = true;
            StatusMessage = busyMessage;
            await action();
            StatusMessage = successMessage;
        }
        catch (Exception ex)
        {
            StatusMessage = $"{errorStatusPrefix}：{ex.Message}";
            ShowErrorMessage(errorTitle, ex);
        }
        finally
        {
            IsSaving = false;
        }
    }

    private bool CanRefresh()
    {
        return !IsBusy;
    }

    private bool CanAddCustomer()
    {
        return !IsBusy;
    }

    private bool CanAddOrder()
    {
        return _allCustomers.Count > 0 && !IsBusy;
    }

    private bool CanAddNote()
    {
        return SelectedCustomer is not null && !IsBusy;
    }

    private bool CanAddFollowUp()
    {
        return SelectedCustomer is not null && !IsBusy;
    }

    private bool CanAddPriceAdjustment()
    {
        return SelectedCustomer is not null && SelectedOrder is not null && !IsBusy;
    }

    private bool CanChangeDealStage()
    {
        return SelectedCustomer is not null && !IsBusy;
    }

    private bool CanChangeCustomerStatus()
    {
        return SelectedCustomer is not null && !IsBusy;
    }

    private bool CanChangeOrderStatus()
    {
        return SelectedOrder is not null && !IsBusy;
    }

    private bool CanCompleteFollowUp(FollowUp? followUp)
    {
        return followUp is not null && CanTransitionFollowUp(followUp.Status) && !IsBusy;
    }

    private bool CanSnoozeFollowUp(FollowUp? followUp)
    {
        return followUp is not null && CanTransitionFollowUp(followUp.Status) && !IsBusy;
    }

    private bool CanCancelFollowUp(FollowUp? followUp)
    {
        return followUp is not null && CanTransitionFollowUp(followUp.Status) && !IsBusy;
    }

    private static bool CanTransitionFollowUp(FollowUpStatus status)
    {
        return status is FollowUpStatus.Pending or FollowUpStatus.InProgress or FollowUpStatus.Overdue;
    }

    private static string CreateNoteActivityMetadataJson(ReplyTemplate? insertedTemplate)
    {
        return insertedTemplate is null
            ? string.Empty
            : JsonSerializer.Serialize(new
            {
                templateId = insertedTemplate.Id,
                templateTitle = insertedTemplate.Title,
                templateScene = insertedTemplate.Scene
            });
    }

    private void OnDetailStateChanged()
    {
        OnPropertyChanged(nameof(CurrentDealStage));
        OnPropertyChanged(nameof(LatestNote));
        OnPropertyChanged(nameof(HasDeals));
        OnPropertyChanged(nameof(HasFollowUps));
        OnPropertyChanged(nameof(HasCustomerNotes));
        OnPropertyChanged(nameof(HasPriceAdjustments));
        OnPropertyChanged(nameof(HasActivityLogs));
        OnPropertyChanged(nameof(DealsCount));
        OnPropertyChanged(nameof(FollowUpsCount));
        OnPropertyChanged(nameof(CustomerNotesCount));
        OnPropertyChanged(nameof(PriceAdjustmentsCount));
        OnPropertyChanged(nameof(ActivityLogsCount));
    }

    private Task ReloadSelectedCustomerDetailsAsync(Customer? customer = null)
    {
        return LoadSelectedCustomerDetailsAsync(customer ?? SelectedCustomer);
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private void ShowNoSelectionMessage()
    {
        const string message = "请先选择一个客户或订单";
        StatusMessage = message;
        System.Windows.MessageBox.Show(GetDialogOwner(), message, "Orderly", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShowErrorMessage(string title, Exception ex)
    {
        System.Windows.MessageBox.Show(GetDialogOwner(), ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

}
