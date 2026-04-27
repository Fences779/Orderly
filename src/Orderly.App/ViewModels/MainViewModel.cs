using System.Collections.ObjectModel;
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
        _dealService = dealService;
        _followUpService = followUpService;
        _noteService = noteService;
        _activityLogService = activityLogService;
        _priceAdjustmentService = priceAdjustmentService;
        _replyTemplateRepository = replyTemplateRepository;
        _settingRepository = settingRepository;
        _clipboardService = clipboardService;
        DatabasePath = databasePath;
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

    public string DatabasePath { get; }

    [ObservableProperty]
    private string selectedSection = "工作台";

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
    [NotifyCanExecuteChangedFor(nameof(AddPriceAdjustmentCommand))]
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
    [NotifyCanExecuteChangedFor(nameof(AddPriceAdjustmentCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeDealStageCommand))]
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
    [NotifyCanExecuteChangedFor(nameof(AddPriceAdjustmentCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeDealStageCommand))]
    private bool isLoading;

    [ObservableProperty]
    private string? loadError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddNoteCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddFollowUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddPriceAdjustmentCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeDealStageCommand))]
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
    public int PendingCount => Orders.Count(item => item.Order.Status is OrderStatus.PendingCommunication or OrderStatus.PendingQuote or OrderStatus.PendingFollowUp);
    public int WonCount => Orders.Count(item => item.Order.Status == OrderStatus.Won);
    public decimal TotalAmount => Orders.Sum(item => item.Order.Amount);
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
            var templates = await _replyTemplateRepository.GetAllAsync(cancellationToken);

            ReplaceCollection(Customers, customers);
            ReplaceCollection(Orders, orders.Select(order => new OrderListItem(order)));
            ReplaceCollection(ReplyTemplates, templates);

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

        if (_isSynchronizingSelection)
        {
            return;
        }

        _ = SelectCustomerForOrderAsync(value?.Order);
    }

    partial void OnSelectedCustomerChanged(Customer? value)
    {
        StatusMessage = value is null ? "未选择客户" : $"已选择客户：{value.Name}";

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
            var (dialog, result) = await ShowDialogAsync(() => new AddNoteDialog());

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
                    await _noteService.SaveNoteAsync(new CustomerNote
                    {
                        CustomerId = customer.Id,
                        DealId = SelectedDeal?.Id,
                        OrderId = SelectedOrder?.Id,
                        Type = dialog.SelectedNoteType,
                        Content = dialog.NoteContent
                    });

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
            var deal = await EnsureSelectedDealAsync(customer);
            var nextStage = GetNextStage(deal.Stage);
            await _dealService.UpdateStageAsync(deal.Id, nextStage);
            await ReloadSelectedCustomerDetailsAsync(customer);
            StatusMessage = $"成交阶段已更新为 {GetDealStageLabel(nextStage)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"更新成交阶段失败：{ex.Message}";
        }
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
            if (order.Customer is not null)
            {
                customer = order.Customer;
            }
            else if (order.CustomerId > 0)
            {
                customer = Customers.FirstOrDefault(item => item.Id == order.CustomerId)
                    ?? await _customerRepository.GetByIdAsync(order.CustomerId);
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

    private static string GetCustomerStatusLabel(CustomerStatus status)
    {
        return status switch
        {
            CustomerStatus.Active => "活跃",
            CustomerStatus.Dormant => "沉默",
            CustomerStatus.Blocked => "受限",
            CustomerStatus.Archived => "已归档",
            _ => status.ToString()
        };
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
