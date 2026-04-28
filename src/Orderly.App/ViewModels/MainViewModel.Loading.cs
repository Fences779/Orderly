using CommunityToolkit.Mvvm.Input;
using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
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

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        await LoadAsync();
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
            var messages = await LoadConversationMessagesAsync(customer);
            var suggestions = await LoadAiSuggestionsAsync(customer);
            var adjustments = await _priceAdjustmentService.GetCustomerAdjustmentsAsync(customer.Id);
            var activities = await _activityLogService.GetCustomerActivitiesAsync(customer.Id);

            if (loadVersion != _detailLoadVersion || SelectedCustomer?.Id != customer.Id)
            {
                return;
            }

            ReplaceCollection(Deals, deals);
            ReplaceCollection(FollowUps, followUps.OrderByDescending(followUp => followUp.ScheduledAt));
            ReplaceCollection(CustomerNotes, notes.OrderByDescending(note => note.CreatedAt));
            ReplaceCollection(ConversationMessages, messages);
            var selectedSuggestionId = SelectedAiSuggestion?.Id;
            ReplaceCollection(AiSuggestions, suggestions);
            ReplaceCollection(PriceAdjustments, adjustments.OrderByDescending(adjustment => adjustment.CreatedAt));
            ReplaceCollection(ActivityLogs, activities.OrderByDescending(activity => activity.CreatedAt));

            SelectedDeal = Deals.FirstOrDefault(deal => deal.Id == SelectedOrder?.DealId) ?? Deals.FirstOrDefault();
            SelectedAiSuggestion = AiSuggestions.FirstOrDefault(item => item.Id == selectedSuggestionId) ?? AiSuggestions.FirstOrDefault();
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

    private Task ReloadSelectedCustomerDetailsAsync(Customer? customer = null)
    {
        return LoadSelectedCustomerDetailsAsync(customer ?? SelectedCustomer);
    }

    private async Task<IEnumerable<ConversationMessageListItem>> LoadConversationMessagesAsync(Customer customer)
    {
        var order = SelectedOrder;
        IReadOnlyList<ConversationMessage> messages;

        if (order is not null && order.CustomerId == customer.Id)
        {
            messages = await _conversationService.ListByOrderAsync(order.Id);
        }
        else
        {
            messages = await _conversationService.ListByCustomerAsync(customer.Id);
        }

        return messages
            .OrderByDescending(message => message.MessageTime)
            .ThenByDescending(message => message.Id)
            .Take(8)
            .Select(message => new ConversationMessageListItem(message));
    }

    private async Task<IEnumerable<AiSuggestionListItem>> LoadAiSuggestionsAsync(Customer customer)
    {
        var order = SelectedOrder;
        IReadOnlyList<AiSuggestion> suggestions;

        if (order is not null && order.CustomerId == customer.Id)
        {
            suggestions = await _aiAssistantService.ListSuggestionsAsync(customer.Id, order.Id);
        }
        else
        {
            suggestions = await _aiAssistantService.ListSuggestionsAsync(customer.Id);
        }

        return suggestions
            .OrderByDescending(suggestion => suggestion.CreatedAt)
            .ThenByDescending(suggestion => suggestion.Id)
            .Take(8)
            .Select(suggestion => new AiSuggestionListItem(suggestion));
    }
}
