using System.Collections.ObjectModel;
using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
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
    public ObservableCollection<string> Sections { get; } = new(new[] { SectionWorkbench, SectionOrders, SectionProducts, SectionInventory, SectionCustomers, SectionCashflow, SectionBusinessAdvice, SectionSettings, SectionMe });
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
}
