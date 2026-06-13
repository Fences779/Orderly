using System.Linq;
using Orderly.App.ViewModels;
using Orderly.App.ViewModels.Pages;
using Xunit;

namespace Orderly.Tests.Ui;

/// <summary>
/// Smoke tests for the nine-entry navigation shell wiring (task 19.4). The WPF navigation control
/// marks the active entry by comparing each entry's section key with
/// <see cref="MainViewModel.SelectedSection"/> (Req 6.12). These tests verify, at the ViewModel
/// level, that every per-page ViewModel is keyed to the correct top-level navigation section, so
/// selecting an entry marks exactly the right page active.
/// </summary>
public sealed class CommerceNavigationWiringTests
{
    private static WorkbenchPageViewModel Workbench() => new(new FakeDashboardService());
    private static OrdersPageViewModel Orders() => new(new FakeOrderService(), new FakeCommerceOrderRepository());
    private static ProductsPageViewModel Products() => new(new FakeProductService());
    private static InventoryPageViewModel Inventory() => new(new FakeInventoryService(), new FakeInventoryItemRepository());
    private static CustomersPageViewModel Customers() => new(new FakeCustomerService(), new FakeCommerceCustomerRepository());
    private static CashflowPageViewModel Cashflow() => new(new FakeCashFlowService(), new FakeCashFlowEntryRepository());
    private static BusinessAdvicePageViewModel BusinessAdvice() => new(new FakeBusinessInsightService());

    [Fact]
    public void Each_page_viewmodel_is_keyed_to_its_navigation_section()
    {
        Assert.Equal(MainViewModel.SectionWorkbench, Workbench().PageKey);
        Assert.Equal(MainViewModel.SectionOrders, Orders().PageKey);
        Assert.Equal(MainViewModel.SectionProducts, Products().PageKey);
        Assert.Equal(MainViewModel.SectionInventory, Inventory().PageKey);
        Assert.Equal(MainViewModel.SectionCustomers, Customers().PageKey);
        Assert.Equal(MainViewModel.SectionCashflow, Cashflow().PageKey);
        Assert.Equal(MainViewModel.SectionBusinessAdvice, BusinessAdvice().PageKey);
    }

    [Fact]
    public void Page_keys_are_distinct_so_only_one_entry_is_marked_active()
    {
        string[] keys =
        {
            Workbench().PageKey,
            Orders().PageKey,
            Products().PageKey,
            Inventory().PageKey,
            Customers().PageKey,
            Cashflow().PageKey,
            BusinessAdvice().PageKey
        };

        Assert.Equal(keys.Length, keys.Distinct().Count());
    }

    [Fact]
    public void Nine_top_level_navigation_sections_are_distinct()
    {
        string[] sections =
        {
            MainViewModel.SectionWorkbench,
            MainViewModel.SectionOrders,
            MainViewModel.SectionProducts,
            MainViewModel.SectionInventory,
            MainViewModel.SectionCustomers,
            MainViewModel.SectionCashflow,
            MainViewModel.SectionBusinessAdvice,
            MainViewModel.SectionSettings,
            MainViewModel.SectionMe
        };

        Assert.Equal(9, sections.Length);
        Assert.Equal(9, sections.Distinct().Count());
    }
}
