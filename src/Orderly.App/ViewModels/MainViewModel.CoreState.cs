using CommunityToolkit.Mvvm.ComponentModel;
using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
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
}
