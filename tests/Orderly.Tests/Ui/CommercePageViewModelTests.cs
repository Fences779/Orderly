using System;
using System.Threading.Tasks;
using Orderly.App.ViewModels.Pages;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Services;
using Xunit;

namespace Orderly.Tests.Ui;

/// <summary>
/// ViewModel-level example/smoke tests for the nine-entry commerce shell (task 19.4). Because the
/// WPF surface cannot be rendered headlessly, these tests exercise the per-page ViewModels directly
/// to verify that:
/// <list type="bullet">
///   <item><description>each page sources its data from its Commerce service (Req 6.2–6.8);</description></item>
///   <item><description>a Commerce service that throws drives the page into <c>HasError</c> without
///   throwing/terminating, and <c>RefreshCommand</c> retries (Req 6.13);</description></item>
///   <item><description>an empty Commerce result set yields an explicit empty state (Req 6.14).</description></item>
/// </list>
/// </summary>
public sealed class CommercePageViewModelTests
{
    private static readonly Guid Workspace = Guid.NewGuid();

    // ----- Workbench (工作台) → IDashboardService (Req 6.2) -----

    [Fact]
    public async Task Workbench_loads_metrics_from_dashboard_service()
    {
        var dashboard = new FakeDashboardService
        {
            Snapshot = new DashboardSnapshot
            {
                AsOfUtc = DateTime.UtcNow,
                Metrics = new DashboardMetrics
                {
                    TotalOrders = 5,
                    CompletedOrders = 3,
                    TotalRevenue = CommerceMoney.From(1234.50m),
                    CustomerCount = 4,
                    LowStockItemCount = 1
                },
                Trend = Array.Empty<DashboardTrendPoint>()
            }
        };
        var vm = new WorkbenchPageViewModel(dashboard);

        await vm.LoadAsync();

        Assert.Equal(1, dashboard.GetSnapshotCallCount);
        Assert.True(vm.IsLoaded);
        Assert.False(vm.HasError);
        Assert.Equal(5, vm.TotalOrders);
        Assert.Equal(3, vm.CompletedOrders);
        Assert.Equal("1234.50", vm.TotalRevenue);
        Assert.True(vm.ShowContent);
    }

    [Fact]
    public async Task Workbench_service_failure_sets_error_without_throwing_and_retry_recovers()
    {
        var dashboard = new FakeDashboardService { SnapshotException = new InvalidOperationException("dashboard down") };
        var vm = new WorkbenchPageViewModel(dashboard);

        await vm.LoadAsync(); // must not throw / terminate (Req 6.13)

        Assert.True(vm.HasError);
        Assert.Equal("dashboard down", vm.ErrorMessage);
        Assert.False(vm.ShowContent);

        // Retry affordance recovers once the service is healthy again.
        dashboard.SnapshotException = null;
        dashboard.Snapshot = new DashboardSnapshot
        {
            AsOfUtc = DateTime.UtcNow,
            Metrics = new DashboardMetrics { TotalOrders = 2 },
            Trend = Array.Empty<DashboardTrendPoint>()
        };

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.False(vm.HasError);
        Assert.True(vm.ShowContent);
        Assert.Equal(2, vm.TotalOrders);
    }

    // ----- Orders (订单) → Commerce order repository (Req 6.3) -----

    [Fact]
    public async Task Orders_loads_rows_from_commerce_order_repository()
    {
        var repo = new FakeCommerceOrderRepository();
        repo.Items.Add(new Order { WorkspaceId = Workspace, OrderNo = "订单 001" });
        var vm = new OrdersPageViewModel(new FakeOrderService(), repo);

        await vm.LoadAsync();

        Assert.Equal(1, repo.GetAllCallCount);
        Assert.Single(vm.Orders);
        Assert.Equal("订单 001", vm.Orders[0].OrderNo);
        Assert.True(vm.ShowContent);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public async Task Orders_empty_result_yields_empty_state()
    {
        var repo = new FakeCommerceOrderRepository();
        var vm = new OrdersPageViewModel(new FakeOrderService(), repo);

        await vm.LoadAsync();

        Assert.Equal(1, repo.GetAllCallCount);
        Assert.True(vm.IsEmpty);          // Req 6.14
        Assert.False(vm.ShowContent);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task Orders_service_failure_sets_error_and_retry_recovers()
    {
        var repo = new FakeCommerceOrderRepository { GetAllException = new InvalidOperationException("orders down") };
        var vm = new OrdersPageViewModel(new FakeOrderService(), repo);

        await vm.LoadAsync();

        Assert.True(vm.HasError);
        Assert.Equal("orders down", vm.ErrorMessage);

        repo.GetAllException = null;
        repo.Items.Add(new Order { WorkspaceId = Workspace, OrderNo = "订单 002" });
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.False(vm.HasError);
        Assert.Single(vm.Orders);
    }

    // ----- Products (商品) → IProductService (Req 6.4) -----

    [Fact]
    public async Task Products_loads_rows_from_product_service()
    {
        var service = new FakeProductService();
        service.Products.Add(new Product { WorkspaceId = Workspace, Name = "商品 A" });
        var vm = new ProductsPageViewModel(service);

        await vm.LoadAsync();

        Assert.Equal(1, service.GetAllCallCount);
        Assert.Single(vm.Products);
        Assert.Equal("商品 A", vm.Products[0].Name);
        Assert.True(vm.ShowContent);
    }

    [Fact]
    public async Task Products_empty_result_yields_empty_state()
    {
        var service = new FakeProductService();
        var vm = new ProductsPageViewModel(service);

        await vm.LoadAsync();

        Assert.True(vm.IsEmpty);
        Assert.False(vm.ShowContent);
    }

    [Fact]
    public async Task Products_service_failure_sets_error_and_retry_recovers()
    {
        var service = new FakeProductService { GetAllException = new InvalidOperationException("products down") };
        var vm = new ProductsPageViewModel(service);

        await vm.LoadAsync();

        Assert.True(vm.HasError);

        service.GetAllException = null;
        service.Products.Add(new Product { WorkspaceId = Workspace, Name = "商品 B" });
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.False(vm.HasError);
        Assert.Single(vm.Products);
    }

    // ----- Inventory (库存) → IInventoryService (Req 6.5) -----

    [Fact]
    public async Task Inventory_loads_items_with_metrics_from_inventory_service()
    {
        var item = new InventoryItem { WorkspaceId = Workspace, Name = "库存项 A", QuantityAvailable = 2m, ReorderThreshold = 5m };
        var itemRepo = new FakeInventoryItemRepository();
        itemRepo.Items.Add(item);

        var service = new FakeInventoryService();
        service.Metrics.Add(new InventoryMetrics
        {
            InventoryItemId = item.Id,
            QuantityAvailable = 2m,
            ReorderThreshold = 5m,
            IsLowStock = true,
            CoverageDays = null,
            ReorderSuggestion = new ReorderSuggestion { InventoryItemId = item.Id, ShouldReorder = true, SuggestedQuantity = 10m }
        });

        var vm = new InventoryPageViewModel(service, itemRepo);

        await vm.LoadAsync();

        Assert.Equal(1, service.GetAllMetricsCallCount);
        Assert.Equal(1, itemRepo.GetAllCallCount);
        Assert.Single(vm.Items);
        Assert.True(vm.Items[0].IsLowStock);
        Assert.True(vm.Items[0].ShouldReorder);
        Assert.True(vm.ShowContent);
    }

    [Fact]
    public async Task Inventory_empty_result_yields_empty_state()
    {
        var vm = new InventoryPageViewModel(new FakeInventoryService(), new FakeInventoryItemRepository());

        await vm.LoadAsync();

        Assert.True(vm.IsEmpty);
        Assert.False(vm.ShowContent);
    }

    [Fact]
    public async Task Inventory_service_failure_sets_error_without_throwing()
    {
        var service = new FakeInventoryService { MetricsException = new InvalidOperationException("inventory down") };
        var vm = new InventoryPageViewModel(service, new FakeInventoryItemRepository());

        await vm.LoadAsync();

        Assert.True(vm.HasError);
        Assert.Equal("inventory down", vm.ErrorMessage);
    }

    // ----- Customers (客户) → ICustomerService (Req 6.6) -----

    [Fact]
    public async Task Customers_loads_rows_with_rfm_from_customer_service()
    {
        var customer = new Customer { WorkspaceId = Workspace, Name = "客户 A", Phone = "10086" };
        var repo = new FakeCommerceCustomerRepository();
        repo.Items.Add(customer);

        var service = new FakeCustomerService();
        service.Metrics.Add(new CustomerRfmMetrics
        {
            CustomerId = customer.Id,
            RecencyDays = 7,
            Frequency = 3,
            Monetary = CommerceMoney.From(900m)
        });

        var vm = new CustomersPageViewModel(service, repo);

        await vm.LoadAsync();

        Assert.Equal(1, service.GetAllMetricsCallCount);
        Assert.Equal(1, repo.GetAllCallCount);
        Assert.Single(vm.Customers);
        Assert.Equal("客户 A", vm.Customers[0].Name);
        Assert.Equal(3, vm.Customers[0].Frequency);
        Assert.True(vm.ShowContent);
    }

    [Fact]
    public async Task Customers_empty_result_yields_empty_state()
    {
        var vm = new CustomersPageViewModel(new FakeCustomerService(), new FakeCommerceCustomerRepository());

        await vm.LoadAsync();

        Assert.True(vm.IsEmpty);
        Assert.False(vm.ShowContent);
    }

    [Fact]
    public async Task Customers_service_failure_sets_error_without_throwing()
    {
        var repo = new FakeCommerceCustomerRepository { GetAllException = new InvalidOperationException("customers down") };
        var vm = new CustomersPageViewModel(new FakeCustomerService(), repo);

        await vm.LoadAsync();

        Assert.True(vm.HasError);
        Assert.Equal("customers down", vm.ErrorMessage);
    }

    // ----- Cash Flow (现金流) → ICashFlowService (Req 6.7) -----

    [Fact]
    public async Task Cashflow_loads_summary_and_entries_computed_from_entries()
    {
        var entryRepo = new FakeCashFlowEntryRepository();
        entryRepo.Items.Add(new CashFlowEntry
        {
            WorkspaceId = Workspace,
            Direction = CashFlowDirection.Income,
            Amount = CommerceMoney.From(500m),
            SettledAmount = CommerceMoney.From(500m),
            SettlementStatus = CashFlowSettlementStatus.Settled,
            OccurredAt = DateTime.UtcNow.AddDays(-1),
            CategoryName = "收入分类 A"
        });

        var vm = new CashflowPageViewModel(new FakeCashFlowService(), entryRepo);

        await vm.LoadAsync();

        // The page reads the full entry set exactly once and computes the period summary from that
        // same set via the shared calculator (no redundant second read, no service round-trip).
        Assert.Equal(1, entryRepo.GetAllCallCount);
        Assert.Equal("500.00", vm.RealizedIncome);
        // Fully-settled income with no expense or outstanding obligation scores the maximum.
        Assert.Equal(100, vm.HealthScore);
        Assert.Single(vm.Entries);
        Assert.True(vm.ShowContent);
    }

    [Fact]
    public async Task Cashflow_empty_result_yields_empty_state()
    {
        var vm = new CashflowPageViewModel(new FakeCashFlowService(), new FakeCashFlowEntryRepository());

        await vm.LoadAsync();

        Assert.True(vm.IsEmpty);
        Assert.False(vm.ShowContent);
    }

    [Fact]
    public async Task Cashflow_read_failure_sets_error_without_throwing()
    {
        var entryRepo = new FakeCashFlowEntryRepository { GetAllException = new InvalidOperationException("cashflow down") };
        var vm = new CashflowPageViewModel(new FakeCashFlowService(), entryRepo);

        await vm.LoadAsync();

        Assert.True(vm.HasError);
        Assert.Equal("cashflow down", vm.ErrorMessage);
    }

    // ----- Business Advice (经营建议) → IBusinessInsightService (Req 6.8) -----

    [Fact]
    public async Task BusinessAdvice_loads_insights_from_insight_service()
    {
        var service = new FakeBusinessInsightService();
        service.Insights.Add(new BusinessInsight { WorkspaceId = Workspace, Title = "库存偏低", Message = "库存项 A 库存不足" });
        var vm = new BusinessAdvicePageViewModel(service);

        await vm.LoadAsync();

        Assert.Equal(1, service.GenerateCallCount);
        Assert.Single(vm.Insights);
        Assert.Equal("库存偏低", vm.Insights[0].Title);
        Assert.True(vm.ShowContent);
    }

    [Fact]
    public async Task BusinessAdvice_empty_result_yields_empty_state()
    {
        var vm = new BusinessAdvicePageViewModel(new FakeBusinessInsightService());

        await vm.LoadAsync();

        Assert.True(vm.IsEmpty);
        Assert.False(vm.ShowContent);
    }

    [Fact]
    public async Task BusinessAdvice_service_failure_sets_error_and_retry_recovers()
    {
        var service = new FakeBusinessInsightService { GenerateException = new InvalidOperationException("advice down") };
        var vm = new BusinessAdvicePageViewModel(service);

        await vm.LoadAsync();

        Assert.True(vm.HasError);

        service.GenerateException = null;
        service.Insights.Add(new BusinessInsight { WorkspaceId = Workspace, Title = "应收逾期", Message = "客户 A 有逾期应收" });
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.False(vm.HasError);
        Assert.Single(vm.Insights);
    }
}
