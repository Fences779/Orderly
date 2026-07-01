using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Core.Commerce.Services;
using Orderly.Core.Repositories;
using AppPreferences = Orderly.Core.Models.AppPreferences;
using CommerceCustomer = Orderly.Core.Commerce.Customer;
using CommerceOrder = Orderly.Core.Commerce.Order;

namespace Orderly.Tests.Ui;

// UI page tests share the Orderly.Tests.Ui namespace and import both Commerce and legacy model
// namespaces. These lightweight shells provide an unambiguous local Order/Customer symbol while
// remaining implicitly convertible to the Commerce entities expected by the repositories/services.
internal sealed class Order
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid WorkspaceId { get; init; }
    public string OrderNo { get; init; } = string.Empty;

    public static implicit operator CommerceOrder(Order order) => new()
    {
        Id = order.Id,
        WorkspaceId = order.WorkspaceId,
        OrderNo = order.OrderNo
    };
}

internal sealed class Customer
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid WorkspaceId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Phone { get; init; }

    public static implicit operator CommerceCustomer(Customer customer) => new()
    {
        Id = customer.Id,
        WorkspaceId = customer.WorkspaceId,
        Name = customer.Name,
        Phone = customer.Phone
    };
}

/// <summary>
/// Lightweight, configurable test doubles for the Commerce Service Layer interfaces the nine-entry
/// shell's per-page ViewModels source their data from (Req 6.2–6.8). Each fake records that it was
/// invoked (so a test can assert the page sourced its data from the Commerce service) and can be
/// configured to return supplied data, return an empty result set (Req 6.14), or throw (Req 6.13).
/// </summary>
internal abstract class FakeCommerceRepositoryBase<TEntity> : ICommerceRepository<TEntity>
    where TEntity : CommerceEntity
{
    /// <summary>The active rows the repository returns from <see cref="GetAllAsync"/>.</summary>
    public List<TEntity> Items { get; } = new();

    /// <summary>When set, <see cref="GetAllAsync"/> throws this to drive the page error state (Req 6.13).</summary>
    public Exception? GetAllException { get; set; }

    /// <summary>The number of times <see cref="GetAllAsync"/> was invoked (data-source assertion).</summary>
    public int GetAllCallCount { get; private set; }

    public Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        GetAllCallCount++;
        if (GetAllException is not null)
        {
            throw GetAllException;
        }

        return Task.FromResult<IReadOnlyList<TEntity>>(Items.ToList());
    }

    public Task<TEntity> CreateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        Items.Add(entity);
        return Task.FromResult(entity);
    }

    public Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.FirstOrDefault(e => e.Id == id));

    public Task<TEntity?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.FirstOrDefault(e => e.Id == id));

    public Task<IReadOnlyList<TEntity>> GetAllIncludingDeletedAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<TEntity>>(Items.ToList());

    public Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class FakeCommerceOrderRepository : FakeCommerceRepositoryBase<CommerceOrder>, ICommerceOrderRepository { }

internal sealed class FakeInventoryItemRepository : FakeCommerceRepositoryBase<InventoryItem>, IInventoryItemRepository { }

internal sealed class FakeCommerceCustomerRepository : FakeCommerceRepositoryBase<CommerceCustomer>, ICommerceCustomerRepository { }

internal sealed class FakeCashFlowEntryRepository : FakeCommerceRepositoryBase<CashFlowEntry>, ICashFlowEntryRepository { }

internal sealed class FakeAppSettingRepository : IAppSettingRepository
{
    private AppPreferences _preferences;

    public FakeAppSettingRepository(AppPreferences preferences) => _preferences = preferences;

    public Task<AppPreferences> GetPreferencesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_preferences);

    public Task SavePreferencesAsync(AppPreferences preferences, CancellationToken cancellationToken = default)
    {
        _preferences = preferences;
        return Task.CompletedTask;
    }

    public Task UpsertAsync(string key, string value, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>Dashboard fake: backs the Workbench page (Req 6.2).</summary>
internal sealed class FakeDashboardService : IDashboardService
{
    public int GetSnapshotCallCount { get; private set; }
    public Exception? SnapshotException { get; set; }
    public DashboardSnapshot? Snapshot { get; set; }

    public Task<DashboardSnapshot> GetSnapshotAsync(DateTime asOfUtc, CancellationToken cancellationToken = default)
    {
        GetSnapshotCallCount++;
        if (SnapshotException is not null)
        {
            throw SnapshotException;
        }

        DashboardSnapshot snapshot = Snapshot ?? new DashboardSnapshot
        {
            AsOfUtc = asOfUtc,
            Metrics = new DashboardMetrics(),
            Trend = Array.Empty<DashboardTrendPoint>()
        };
        return Task.FromResult(snapshot);
    }

    public Task<IReadOnlyList<BusinessMetricSnapshot>> PersistMetricSnapshotsAsync(
        Guid workspaceId,
        DateTime asOfUtc,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<BusinessMetricSnapshot>>(Array.Empty<BusinessMetricSnapshot>());
}

/// <summary>Product service fake: backs the Products page (Req 6.4).</summary>
internal sealed class FakeProductService : IProductService
{
    public int GetAllCallCount { get; private set; }
    public Exception? GetAllException { get; set; }
    public List<Product> Products { get; } = new();

    public Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        GetAllCallCount++;
        if (GetAllException is not null)
        {
            throw GetAllException;
        }

        return Task.FromResult<IReadOnlyList<Product>>(Products.ToList());
    }

    public Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(Products.FirstOrDefault(p => p.Id == id));

    public Task<Product> CreateAsync(Product product, CancellationToken cancellationToken = default)
    {
        Products.Add(product);
        return Task.FromResult(product);
    }

    public Task UpdateAsync(Product product, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>Inventory service fake: backs the Inventory page (Req 6.5).</summary>
internal sealed class FakeInventoryService : IInventoryService
{
    public int GetAllMetricsCallCount { get; private set; }
    public Exception? MetricsException { get; set; }
    public List<InventoryMetrics> Metrics { get; } = new();

    public Task<IReadOnlyList<InventoryMetrics>> GetAllMetricsAsync(DateTime asOfUtc, CancellationToken cancellationToken = default)
    {
        GetAllMetricsCallCount++;
        if (MetricsException is not null)
        {
            throw MetricsException;
        }

        return Task.FromResult<IReadOnlyList<InventoryMetrics>>(Metrics.ToList());
    }

    public Task<InventoryItem> RecordMovementAsync(InventoryMovement movement, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<InventoryMetrics> GetMetricsAsync(Guid inventoryItemId, DateTime asOfUtc, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<BusinessInsight>> GenerateInventoryInsightsAsync(DateTime asOfUtc, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<BusinessInsight>>(Array.Empty<BusinessInsight>());
}

/// <summary>Customer service fake: backs the Customers page (Req 6.6).</summary>
internal sealed class FakeCustomerService : ICustomerService
{
    public int GetAllMetricsCallCount { get; private set; }
    public Exception? MetricsException { get; set; }
    public List<CustomerRfmMetrics> Metrics { get; } = new();

    public Task<IReadOnlyList<CustomerRfmMetrics>> GetAllMetricsAsync(DateTime asOfUtc, CancellationToken cancellationToken = default)
    {
        GetAllMetricsCallCount++;
        if (MetricsException is not null)
        {
            throw MetricsException;
        }

        return Task.FromResult<IReadOnlyList<CustomerRfmMetrics>>(Metrics.ToList());
    }

    public Task<CustomerRfmMetrics> GetMetricsAsync(Guid customerId, DateTime asOfUtc, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<RepurchaseReminder>> GetRepurchaseRemindersAsync(
        DateTime asOfUtc,
        int reminderThresholdDays = ICustomerService.DefaultReminderThresholdDays,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<RepurchaseReminder>>(Array.Empty<RepurchaseReminder>());
}

/// <summary>Cash-flow service fake: backs the Cash Flow page (Req 6.7).</summary>
internal sealed class FakeCashFlowService : ICashFlowService
{
    public int GetPeriodSummaryCallCount { get; private set; }
    public Exception? SummaryException { get; set; }
    public CashFlowPeriodSummary? Summary { get; set; }

    public Task<CashFlowPeriodSummary> GetPeriodSummaryAsync(DateRange period, CancellationToken cancellationToken = default)
    {
        GetPeriodSummaryCallCount++;
        if (SummaryException is not null)
        {
            throw SummaryException;
        }

        CashFlowPeriodSummary summary = Summary ?? new CashFlowPeriodSummary { Period = period };
        return Task.FromResult(summary);
    }

    public Task<CashFlowEntry> RecordIncomeAsync(CashFlowEntryInput input, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<CashFlowEntry> RecordExpenseAsync(CashFlowEntryInput input, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<CashFlowEntry> RecordReceivableAsync(CashFlowEntryInput input, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<CashFlowEntry> RecordPayableAsync(CashFlowEntryInput input, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<CashFlowEntry> SettleAsync(Guid entryId, CommerceMoney amount, DateTime asOfUtc, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<int> ComputeHealthScoreAsync(DateRange period, CancellationToken cancellationToken = default)
        => Task.FromResult(0);
}

/// <summary>Business insight service fake: backs the Business Advice page (Req 6.8).</summary>
internal sealed class FakeBusinessInsightService : IBusinessInsightService
{
    public int GenerateCallCount { get; private set; }
    public Exception? GenerateException { get; set; }
    public List<BusinessInsight> Insights { get; } = new();

    public Task<IReadOnlyList<BusinessInsight>> GenerateInsightsAsync(DateTime asOfUtc, CancellationToken cancellationToken = default)
    {
        GenerateCallCount++;
        if (GenerateException is not null)
        {
            throw GenerateException;
        }

        return Task.FromResult<IReadOnlyList<BusinessInsight>>(Insights.ToList());
    }

    public Task<IReadOnlyList<BusinessInsight>> PersistInsightsAsync(DateTime asOfUtc, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<BusinessInsight>>(Insights.ToList());
}

/// <summary>
/// Minimal <see cref="IOrderService"/> fake. The Orders page sources its rows from the Commerce
/// order repository; the order service is only held for command wiring, so the fake's behavioral
/// methods are intentionally inert.
/// </summary>
internal sealed class FakeOrderService : IOrderService
{
    public void RecalculateOrder(
        CommerceOrder order,
        IReadOnlyCollection<OrderItem> items,
        IReadOnlyCollection<PaymentRecord> payments)
    {
    }

    public OrderStageTransitionResult ApplyStageTransition(
        CommerceOrder order,
        OrderStageTransitionRequest request,
        OrderWorkflowConfiguration workflow)
        => OrderStageTransitionResult.Applied();

    public Task<OrderCompletionResult> CompleteOrderAsync(
        Guid orderId,
        DateTime completedAtUtc,
        CancellationToken cancellationToken = default)
        => Task.FromResult(OrderCompletionResult.Completed());
}
