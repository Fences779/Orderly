using Orderly.App.ViewModels.Helpers;
using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
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

    private void InitializeFilterOptions()
    {
        SearchFilterOptions.Add(SearchFilterOption.All);
        foreach (var status in Enum.GetValues<CustomerStatus>())
        {
            SearchFilterOptions.Add(new SearchFilterOption($"客户：{StatusLabelHelper.GetCustomerStatusLabel(status)}", SearchFilterKind.CustomerStatus, status));
        }

        foreach (var priority in Enum.GetValues<CustomerPriority>())
        {
            SearchFilterOptions.Add(new SearchFilterOption($"优先级：{StatusLabelHelper.GetCustomerPriorityLabel(priority)}", SearchFilterKind.CustomerPriority, priority));
        }

        foreach (var status in Enum.GetValues<OrderStatus>())
        {
            SearchFilterOptions.Add(new SearchFilterOption($"订单：{OrderStatusCatalog.GetLabel(status)}", SearchFilterKind.OrderStatus, status));
        }

        foreach (var stage in Enum.GetValues<DealStage>())
        {
            SearchFilterOptions.Add(new SearchFilterOption($"Deal：{StatusLabelHelper.GetDealStageLabel(stage)}", SearchFilterKind.DealStage, stage));
        }

        foreach (var status in Enum.GetValues<FollowUpStatus>())
        {
            SearchFilterOptions.Add(new SearchFilterOption($"跟进：{StatusLabelHelper.GetFollowUpStatusLabel(status)}", SearchFilterKind.FollowUpStatus, status));
        }

        QuickFilterOptions.Add(QuickFilterOption.All);
        QuickFilterOptions.Add(new QuickFilterOption("今日跟进", QuickFilterKind.TodayFollowUp));
        QuickFilterOptions.Add(new QuickFilterOption("逾期跟进", QuickFilterKind.OverdueFollowUp));
        QuickFilterOptions.Add(new QuickFilterOption("明日跟进", QuickFilterKind.TomorrowFollowUp));
        QuickFilterOptions.Add(new QuickFilterOption("待处理订单", QuickFilterKind.PendingOrders));
        QuickFilterOptions.Add(new QuickFilterOption("已成交订单", QuickFilterKind.WonOrders));
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
            StatusLabelHelper.GetCustomerStatusLabel(customer.Status),
            StatusLabelHelper.GetCustomerPriorityLabel(customer.Priority)) ||
            relatedOrders.Any(order => ContainsAny(keyword, order.Title, order.Requirement, order.SourcePlatform, order.Channel, OrderStatusCatalog.GetLabel(order.Status))) ||
            relatedDeals.Any(deal => ContainsAny(keyword, deal.Title, deal.Requirement, StatusLabelHelper.GetDealStageLabel(deal.Stage))) ||
            relatedFollowUps.Any(followUp => ContainsAny(keyword, followUp.Title, followUp.Content, StatusLabelHelper.GetFollowUpStatusLabel(followUp.Status))) ||
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
            relatedDeals.Any(deal => ContainsAny(keyword, deal.Title, deal.Requirement, StatusLabelHelper.GetDealStageLabel(deal.Stage))) ||
            relatedFollowUps.Any(followUp => ContainsAny(keyword, followUp.Title, followUp.Content, StatusLabelHelper.GetFollowUpStatusLabel(followUp.Status))) ||
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
            QuickFilterKind.PendingOrders => _allOrders.Any(order => order.Order.CustomerId == customer.Id && FollowUpDateHelper.IsPendingOrder(order.Order.Status)),
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
            QuickFilterKind.TodayFollowUp => HasFollowUpOn(order.CustomerId, DateTime.Today, order.Id) || FollowUpDateHelper.IsOrderFollowUpOn(order, DateTime.Today),
            QuickFilterKind.OverdueFollowUp => HasOverdueFollowUp(order.CustomerId, order.Id) || FollowUpDateHelper.IsOrderFollowUpOverdue(order, DateTime.Today),
            QuickFilterKind.TomorrowFollowUp => HasFollowUpOn(order.CustomerId, DateTime.Today.AddDays(1), order.Id) || FollowUpDateHelper.IsOrderFollowUpOn(order, DateTime.Today.AddDays(1)),
            QuickFilterKind.PendingOrders => FollowUpDateHelper.IsPendingOrder(order.Status),
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
            FollowUpDateHelper.CanTransitionFollowUp(followUp.Status) &&
            followUp.ScheduledAt.Date == date.Date);
    }

    private bool HasOverdueFollowUp(int customerId, int? orderId = null)
    {
        return _allFollowUps.Any(followUp =>
            followUp.CustomerId == customerId &&
            (orderId is null || followUp.OrderId == orderId) &&
            FollowUpDateHelper.CanTransitionFollowUp(followUp.Status) &&
            followUp.ScheduledAt.Date < DateTime.Today);
    }
}
