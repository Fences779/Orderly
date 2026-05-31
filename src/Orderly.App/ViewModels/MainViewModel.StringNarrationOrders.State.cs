using CommunityToolkit.Mvvm.ComponentModel;
using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

// Observable backing fields and paging state for the string-narration order surface.
// Field names, [ObservableProperty]/[NotifyPropertyChangedFor]/[NotifyCanExecuteChangedFor]
// attributes, and initializers are relocated verbatim from the core partial; the generated
// observable/command contract is therefore unchanged.
public partial class MainViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStringNarrationBusy))]
    [NotifyPropertyChangedFor(nameof(IsStringNarrationWorkAreaBusy))]
    [NotifyPropertyChangedFor(nameof(IsStringNarrationDetailBusyVisible))]
    [NotifyCanExecuteChangedFor(nameof(LoadStringNarrationOrdersCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadStringNarrationStatsCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestStringNarrationGatewayCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyStringNarrationFulfillmentFilterCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearStringNarrationFiltersCommand))]
    [NotifyCanExecuteChangedFor(nameof(SearchStringNarrationOrderDetailCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshStringNarrationOrderDetailCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateStringNarrationFulfillmentCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerateStringNarrationProductionOrderCommand))]
    [NotifyCanExecuteChangedFor(nameof(NavigateStringNarrationPrevPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NavigateStringNarrationNextPageCommand))]
    private bool isStringNarrationLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStringNarrationBusy))]
    [NotifyPropertyChangedFor(nameof(IsStringNarrationWorkAreaBusy))]
    [NotifyPropertyChangedFor(nameof(IsStringNarrationDetailBusyVisible))]
    [NotifyCanExecuteChangedFor(nameof(LoadStringNarrationOrdersCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadStringNarrationStatsCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestStringNarrationGatewayCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyStringNarrationFulfillmentFilterCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearStringNarrationFiltersCommand))]
    [NotifyCanExecuteChangedFor(nameof(SearchStringNarrationOrderDetailCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshStringNarrationOrderDetailCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateStringNarrationFulfillmentCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerateStringNarrationProductionOrderCommand))]
    [NotifyCanExecuteChangedFor(nameof(NavigateStringNarrationPrevPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NavigateStringNarrationNextPageCommand))]
    private bool isStringNarrationSaving;

    [ObservableProperty]
    private bool isStringNarrationInitializing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StringNarrationEmptyStateText))]
    private string stringNarrationError = string.Empty;

    [ObservableProperty]
    private string stringNarrationStatusMessage = "串述订单未加载";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStringNarrationDetailBusyVisible))]
    [NotifyPropertyChangedFor(nameof(IsStringNarrationProductionOrderOverlayVisible))]
[NotifyPropertyChangedFor(nameof(IsStringNarrationWorkAreaBusy))]
    private bool isStringNarrationGeneratingProductionOrder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStringNarrationBusy))]
    [NotifyPropertyChangedFor(nameof(IsStringNarrationDetailBusyVisible))]
    [NotifyPropertyChangedFor(nameof(IsStringNarrationProductionOrderOverlayVisible))]
    [NotifyCanExecuteChangedFor(nameof(LoadStringNarrationOrdersCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadStringNarrationStatsCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestStringNarrationGatewayCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyStringNarrationFulfillmentFilterCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearStringNarrationFiltersCommand))]
    [NotifyCanExecuteChangedFor(nameof(SearchStringNarrationOrderDetailCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshStringNarrationOrderDetailCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateStringNarrationFulfillmentCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerateStringNarrationProductionOrderCommand))]
    private bool isStringNarrationProductionOrderErrorVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StringNarrationStatsTotalText))]
    private StringNarrationFulfillmentStats stringNarrationFulfillmentStats = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchStringNarrationOrderDetailCommand))]
    private string stringNarrationLookupInput = string.Empty;

    [ObservableProperty]
    private string stringNarrationListKeyword = string.Empty;

    [ObservableProperty]
    private string selectedStringNarrationStatusFilter = "全部";

    [ObservableProperty]
    private string selectedStringNarrationFulfillmentStatusFilter = "全部";

    [ObservableProperty]
    private long stringNarrationStartAt;

    [ObservableProperty]
    private long stringNarrationEndAt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedStringNarrationOrderDetail))]
    [NotifyPropertyChangedFor(nameof(IsStringNarrationDetailPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsStringNarrationListExpanded))]
    [NotifyPropertyChangedFor(nameof(StringNarrationSelectedTitle))]
    [NotifyPropertyChangedFor(nameof(StringNarrationSelectedOrderNo))]
    [NotifyPropertyChangedFor(nameof(StringNarrationSelectedTradeNo))]
    [NotifyPropertyChangedFor(nameof(StringNarrationSelectedAmountText))]
    [NotifyPropertyChangedFor(nameof(StringNarrationSelectedPaidAtText))]
    [NotifyPropertyChangedFor(nameof(StringNarrationSelectedCreatedAtText))]
    [NotifyPropertyChangedFor(nameof(StringNarrationAddressText))]
    [NotifyPropertyChangedFor(nameof(StringNarrationRemarkText))]
    [NotifyPropertyChangedFor(nameof(StringNarrationShippingStateText))]
    [NotifyPropertyChangedFor(nameof(StringNarrationTrackingText))]
    [NotifyPropertyChangedFor(nameof(StringNarrationFulfillmentTimeText))]
    [NotifyPropertyChangedFor(nameof(StringNarrationDetailOrderNo))]
    [NotifyPropertyChangedFor(nameof(StringNarrationDetailTransactionId))]
    [NotifyPropertyChangedFor(nameof(StringNarrationDetailStatus))]
    [NotifyPropertyChangedFor(nameof(StringNarrationDetailProduct))]
    [NotifyPropertyChangedFor(nameof(StringNarrationDetailReceiver))]
    [NotifyPropertyChangedFor(nameof(StringNarrationDetailProduction))]
    [NotifyCanExecuteChangedFor(nameof(RefreshStringNarrationOrderDetailCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateStringNarrationFulfillmentCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerateStringNarrationProductionOrderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyStringNarrationOrderFieldCommand))]
    private StringNarrationOrderDetail? selectedStringNarrationOrderDetail;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshStringNarrationOrderDetailCommand))]
    private StringNarrationOrderSummary? selectedStringNarrationOrder;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateStringNarrationFulfillmentCommand))]
    private string stringNarrationFulfillmentStatusInput = StringNarrationFulfillmentStatusCatalog.PendingMake;

    [ObservableProperty]
    private string stringNarrationTrackingNoInput = string.Empty;

    [ObservableProperty]
    private string stringNarrationCarrierInput = string.Empty;

    [ObservableProperty]
    private string stringNarrationExpressCompanyCodeInput = string.Empty;

    [ObservableProperty]
    private string stringNarrationShippingRemarkInput = string.Empty;

    [ObservableProperty]
    private string stringNarrationAdminRemarkInput = string.Empty;

    [ObservableProperty]
    private string stringNarrationProductionOrderRemarkInput = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateStringNarrationProductionOrderCommand))]
    private bool stringNarrationProductionOrderForceRegenerate;

    [ObservableProperty]
    private bool isStringNarrationProductionRemarkEditing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStringNarrationOrderListVisible))]
    [NotifyPropertyChangedFor(nameof(IsStringNarrationProductionSheetVisible))]
    private string stringNarrationLeftPaneMode = StringNarrationLeftPaneOrderList;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStringNarrationProductionSheet))]
    [NotifyPropertyChangedFor(nameof(StringNarrationProductionSheetProductionOrderNoText))]
    [NotifyPropertyChangedFor(nameof(StringNarrationProductionSheetWorkOrderNoText))]
    [NotifyPropertyChangedFor(nameof(StringNarrationProductionSheetStatusText))]
    [NotifyPropertyChangedFor(nameof(StringNarrationProductionSheetArrangementText))]
    [NotifyPropertyChangedFor(nameof(StringNarrationProductionSheetRemarkText))]
    [NotifyPropertyChangedFor(nameof(StringNarrationProductionSheetExampleImageUrl))]
    [NotifyPropertyChangedFor(nameof(StringNarrationProductionSheetExampleFallbackText))]
    private StringNarrationProductionSheetSnapshot? selectedStringNarrationProductionSheet;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StringNarrationPageLabel))]
    [NotifyCanExecuteChangedFor(nameof(NavigateStringNarrationPrevPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NavigateStringNarrationNextPageCommand))]
    private int stringNarrationCurrentPage = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StringNarrationTotalPages))]
    [NotifyPropertyChangedFor(nameof(StringNarrationPageLabel))]
    [NotifyCanExecuteChangedFor(nameof(NavigateStringNarrationPrevPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NavigateStringNarrationNextPageCommand))]
    private int stringNarrationTotalCount = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StringNarrationTotalPages))]
    [NotifyPropertyChangedFor(nameof(StringNarrationPageLabel))]
    [NotifyCanExecuteChangedFor(nameof(NavigateStringNarrationPrevPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NavigateStringNarrationNextPageCommand))]
    private int stringNarrationPageSize = 20;

    [ObservableProperty]
    private bool isStringNarrationPageSizePopupOpen;

    [ObservableProperty]
    private bool isStringNarrationPageSizePopupExpandedOpen;

    public int StringNarrationTotalPages => Math.Max(1, (int)Math.Ceiling((double)StringNarrationTotalCount / StringNarrationPageSize));

    public string StringNarrationPageLabel => $"{StringNarrationCurrentPage} / {StringNarrationTotalPages}";
}
