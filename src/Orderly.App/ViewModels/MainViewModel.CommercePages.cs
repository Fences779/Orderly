using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Orderly.App.ViewModels.Pages;

namespace Orderly.App.ViewModels;

/// <summary>
/// Hosts the dedicated per-page ViewModels of the nine-entry commerce shell (Req 7.1). Each page
/// ViewModel obtains its business data only through the Commerce Service Layer (Req 7.3, 7.4); the
/// Settings (设置) and Me (我的) pages remain served by their existing clearly delimited
/// <c>MainViewModel</c> partials, satisfying the "one dedicated ViewModel or one delimited region
/// per page" rule (Req 7.1).
///
/// <para>The page ViewModels are constructed in the App composition root from the Commerce services
/// and attached here via <see cref="AttachCommercePages"/>. Binding each page to its ViewModel and
/// triggering load-on-navigation with error/empty states is completed by task 19.3.</para>
/// </summary>
public partial class MainViewModel
{
    /// <summary>Workbench (工作台) page ViewModel, backed by <c>IDashboardService</c>.</summary>
    [ObservableProperty]
    private WorkbenchPageViewModel? _workbenchPage;

    /// <summary>Orders (订单) page ViewModel, backed by <c>IOrderService</c> and the Commerce order repository.</summary>
    [ObservableProperty]
    private OrdersPageViewModel? _ordersPage;

    /// <summary>Products (商品) page ViewModel, backed by <c>IProductService</c>.</summary>
    [ObservableProperty]
    private ProductsPageViewModel? _productsPage;

    /// <summary>Inventory (库存) page ViewModel, backed by <c>IInventoryService</c>.</summary>
    [ObservableProperty]
    private InventoryPageViewModel? _inventoryPage;

    /// <summary>Customers (客户) page ViewModel, backed by <c>ICustomerService</c>.</summary>
    [ObservableProperty]
    private CustomersPageViewModel? _customersPage;

    /// <summary>Cash Flow (现金流) page ViewModel, backed by <c>ICashFlowService</c>.</summary>
    [ObservableProperty]
    private CashflowPageViewModel? _cashflowPage;

    /// <summary>Business Advice (经营建议) page ViewModel, backed by <c>IBusinessInsightService</c>.</summary>
    [ObservableProperty]
    private BusinessAdvicePageViewModel? _businessAdvicePage;

    /// <summary>Archive Data (归档数据) page ViewModel, backed by <c>IArchiveService</c>.</summary>
    [ObservableProperty]
    private ArchivePageViewModel? _archivePage;

    /// <summary>
    /// Attaches the dedicated per-page ViewModels constructed in the composition root from the
    /// Commerce Service Layer. Called once during workspace initialization.
    /// </summary>
    public void AttachCommercePages(
        WorkbenchPageViewModel workbenchPage,
        OrdersPageViewModel ordersPage,
        ProductsPageViewModel productsPage,
        InventoryPageViewModel inventoryPage,
        CustomersPageViewModel customersPage,
        CashflowPageViewModel cashflowPage,
        BusinessAdvicePageViewModel businessAdvicePage,
        ArchivePageViewModel? archivePage = null)
    {
        WorkbenchPage = workbenchPage;
        OrdersPage = ordersPage;
        ProductsPage = productsPage;
        InventoryPage = inventoryPage;
        CustomersPage = customersPage;
        CashflowPage = cashflowPage;
        BusinessAdvicePage = businessAdvicePage;
        ArchivePage = archivePage;
    }

    // Tracks which commerce pages have already loaded successfully, so navigating back to a page
    // does not re-query the Commerce service every time (load-on-navigation, once per page). A page
    // whose previous load surfaced an error is intentionally NOT recorded, so re-selecting it (or
    // pressing its retry affordance) re-attempts the load (Req 6.13 retry).
    private readonly HashSet<string> _loadedCommercePages = new(StringComparer.Ordinal);

    /// <summary>
    /// Resolves the dedicated page ViewModel that backs a top-level navigation section, or
    /// <c>null</c> for sections (Settings/Me and non-top-level destinations) served by the existing
    /// delimited <c>MainViewModel</c> partials.
    /// </summary>
    private CommercePageViewModel? ResolveCommercePage(string section) => section switch
    {
        SectionWorkbench => WorkbenchPage,
        SectionOrders => OrdersPage,
        SectionProducts => ProductsPage,
        SectionInventory => InventoryPage,
        SectionCustomers => CustomersPage,
        SectionCashflow => CashflowPage,
        SectionBusinessAdvice => BusinessAdvicePage,
        SectionArchive => ArchivePage,
        _ => null
    };

    /// <summary>
    /// Loads the Commerce data for the page that backs <paramref name="section"/> when it becomes the
    /// selected section (load-on-navigation, Req 6.2–6.8). The page's own load lifecycle turns a
    /// service failure into the page-level error state and retains the last known valid state without
    /// terminating the application or falling back to any legacy service (Req 6.13, 7.5). Pages load
    /// once on success; a page whose last load errored is re-attempted on the next navigation.
    /// </summary>
    public async Task EnsureCommercePageLoadedAsync(string section)
    {
        CommercePageViewModel? page = ResolveCommercePage(section);
        if (page is null)
        {
            return;
        }

        if (_loadedCommercePages.Contains(section) && !page.HasError)
        {
            return;
        }

        await page.LoadAsync();

        if (!page.HasError)
        {
            _loadedCommercePages.Add(section);
        }
    }

    /// <summary>
    /// Clears the loaded marker for a single commerce page so the next navigation refreshes it.
    /// Used by cloud real-time event handlers when the server reports a change to that page's data.
    /// </summary>
    public void MarkCommercePageDirty(string section)
    {
        _loadedCommercePages.Remove(section);
    }

    /// <summary>
    /// Clears the loaded markers for all commerce pages so the next navigation refreshes everything.
    /// Used by cloud real-time event handlers when the exact affected page is unknown.
    /// </summary>
    public void MarkAllCommercePagesDirty()
    {
        _loadedCommercePages.Clear();
    }
}
