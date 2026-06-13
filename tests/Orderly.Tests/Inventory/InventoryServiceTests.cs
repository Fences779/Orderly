using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Core.Commerce.Services;
using Orderly.Data.Commerce.Repositories;
using Orderly.Data.Commerce.Services;
using Orderly.Data.Sqlite;
using Xunit;

namespace Orderly.Tests.Inventory;

/// <summary>
/// Example/unit tests for <see cref="CommerceInventoryService"/> (Task 8.1). They exercise the real
/// SQLCipher-backed repositories against an unencrypted temp database (no mocks) to verify movement
/// quantity updates per <see cref="InventoryMovementType"/> (Req 4.8), low-stock detection, fixed
/// 7-day / 30-day average daily usage, the <c>CoverageDays</c> null rule (Req 4.9, 4.10), reorder
/// suggestions, and inventory insight generation. The universal CoverageDays property is covered
/// separately by the Property 11 test (Task 8.2).
/// </summary>
public sealed class InventoryServiceTests
{
    private static readonly DateTime AsOf = new(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Inbound_movement_increases_available_quantity()
    {
        await WithServiceAsync(async (service, items, _, workspaceId) =>
        {
            InventoryItem item = await CreateItemAsync(items, workspaceId, quantity: 10m, threshold: 2m);

            await service.RecordMovementAsync(Movement(item, InventoryMovementType.Inbound, 5m, AsOf));

            InventoryItem? reloaded = await items.GetByIdAsync(item.Id);
            Assert.Equal(15m, reloaded!.QuantityAvailable);
        });
    }

    [Fact]
    public async Task Outbound_movement_decreases_available_quantity()
    {
        await WithServiceAsync(async (service, items, _, workspaceId) =>
        {
            InventoryItem item = await CreateItemAsync(items, workspaceId, quantity: 10m, threshold: 2m);

            await service.RecordMovementAsync(Movement(item, InventoryMovementType.Outbound, 4m, AsOf));

            InventoryItem? reloaded = await items.GetByIdAsync(item.Id);
            Assert.Equal(6m, reloaded!.QuantityAvailable);
        });
    }

    [Fact]
    public async Task Adjustment_movement_applies_signed_correction()
    {
        await WithServiceAsync(async (service, items, _, workspaceId) =>
        {
            InventoryItem item = await CreateItemAsync(items, workspaceId, quantity: 10m, threshold: 2m);

            // Adjustment applies the (signed) quantity as a correction: +3 raises 10 to 13.
            await service.RecordMovementAsync(Movement(item, InventoryMovementType.Adjustment, 3m, AsOf));
            Assert.Equal(13m, (await items.GetByIdAsync(item.Id))!.QuantityAvailable);

            // A negative adjustment lowers the quantity: -5 brings 13 down to 8.
            await service.RecordMovementAsync(Movement(item, InventoryMovementType.Adjustment, -5m, AsOf));
            Assert.Equal(8m, (await items.GetByIdAsync(item.Id))!.QuantityAvailable);
        });
    }

    [Fact]
    public async Task Recording_movement_for_missing_item_throws()
    {
        await WithServiceAsync(async (service, _, _, workspaceId) =>
        {
            var orphan = new InventoryMovement
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                InventoryItemId = Guid.NewGuid(),
                MovementType = InventoryMovementType.Inbound,
                Quantity = 1m,
                OccurredAt = AsOf,
            };

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.RecordMovementAsync(orphan));
        });
    }

    [Fact]
    public async Task Low_stock_is_true_when_available_at_or_below_threshold()
    {
        await WithServiceAsync(async (service, items, _, workspaceId) =>
        {
            InventoryItem atThreshold = await CreateItemAsync(items, workspaceId, quantity: 5m, threshold: 5m);
            InventoryItem aboveThreshold = await CreateItemAsync(items, workspaceId, quantity: 6m, threshold: 5m);

            InventoryMetrics low = await service.GetMetricsAsync(atThreshold.Id, AsOf);
            InventoryMetrics ok = await service.GetMetricsAsync(aboveThreshold.Id, AsOf);

            Assert.True(low.IsLowStock);
            Assert.False(ok.IsLowStock);
        });
    }

    [Fact]
    public async Task Average_daily_usage_counts_only_outbound_within_windows()
    {
        await WithServiceAsync(async (service, items, movements, workspaceId) =>
        {
            InventoryItem item = await CreateItemAsync(items, workspaceId, quantity: 100m, threshold: 1m);

            // Outbound 7 (3 days ago) -> inside both windows.
            await movements.CreateAsync(Movement(item, InventoryMovementType.Outbound, 7m, AsOf.AddDays(-3)));
            // Outbound 23 (20 days ago) -> inside 30-day window only.
            await movements.CreateAsync(Movement(item, InventoryMovementType.Outbound, 23m, AsOf.AddDays(-20)));
            // Outbound 9 (40 days ago) -> outside both windows.
            await movements.CreateAsync(Movement(item, InventoryMovementType.Outbound, 9m, AsOf.AddDays(-40)));
            // Inbound is never usage even though it is recent.
            await movements.CreateAsync(Movement(item, InventoryMovementType.Inbound, 50m, AsOf.AddDays(-1)));

            InventoryMetrics metrics = await service.GetMetricsAsync(item.Id, AsOf);

            Assert.Equal(7m / 7m, metrics.AvgDailyUsage7d);
            Assert.Equal((7m + 23m) / 30m, metrics.AvgDailyUsage30d);
        });
    }

    [Fact]
    public async Task CoverageDays_is_null_when_no_thirty_day_usage()
    {
        await WithServiceAsync(async (service, items, movements, workspaceId) =>
        {
            InventoryItem item = await CreateItemAsync(items, workspaceId, quantity: 50m, threshold: 1m);

            // Only an out-of-window outbound exists, so 30-day usage is zero.
            await movements.CreateAsync(Movement(item, InventoryMovementType.Outbound, 12m, AsOf.AddDays(-45)));

            InventoryMetrics metrics = await service.GetMetricsAsync(item.Id, AsOf);

            Assert.Equal(0m, metrics.AvgDailyUsage30d);
            Assert.Null(metrics.CoverageDays);
        });
    }

    [Fact]
    public async Task CoverageDays_equals_available_over_avg_when_usage_positive()
    {
        await WithServiceAsync(async (service, items, movements, workspaceId) =>
        {
            InventoryItem item = await CreateItemAsync(items, workspaceId, quantity: 60m, threshold: 1m);
            await movements.CreateAsync(Movement(item, InventoryMovementType.Outbound, 30m, AsOf.AddDays(-10)));

            InventoryMetrics metrics = await service.GetMetricsAsync(item.Id, AsOf);

            Assert.NotNull(metrics.CoverageDays);
            Assert.Equal(item.QuantityAvailable / metrics.AvgDailyUsage30d, metrics.CoverageDays);
        });
    }

    [Fact]
    public async Task Reorder_suggestion_is_off_when_not_low_stock_and_positive_when_low()
    {
        await WithServiceAsync(async (service, items, movements, workspaceId) =>
        {
            InventoryItem healthy = await CreateItemAsync(items, workspaceId, quantity: 100m, threshold: 5m);
            InventoryItem low = await CreateItemAsync(items, workspaceId, quantity: 2m, threshold: 5m);
            await movements.CreateAsync(Movement(low, InventoryMovementType.Outbound, 30m, AsOf.AddDays(-10)));

            InventoryMetrics healthyMetrics = await service.GetMetricsAsync(healthy.Id, AsOf);
            InventoryMetrics lowMetrics = await service.GetMetricsAsync(low.Id, AsOf);

            Assert.False(healthyMetrics.ReorderSuggestion.ShouldReorder);
            Assert.Equal(0m, healthyMetrics.ReorderSuggestion.SuggestedQuantity);

            Assert.True(lowMetrics.ReorderSuggestion.ShouldReorder);
            Assert.True(lowMetrics.ReorderSuggestion.SuggestedQuantity > 0m);
        });
    }

    [Fact]
    public async Task GenerateInsights_flags_out_of_stock_item_as_critical()
    {
        await WithServiceAsync(async (service, items, _, workspaceId) =>
        {
            InventoryItem outOfStock = await CreateItemAsync(items, workspaceId, quantity: 0m, threshold: 5m);
            await CreateItemAsync(items, workspaceId, quantity: 100m, threshold: 5m);

            IReadOnlyList<BusinessInsight> insights = await service.GenerateInventoryInsightsAsync(AsOf);

            BusinessInsight insight = Assert.Single(insights);
            Assert.Equal($"inventory:out-of-stock:{outOfStock.Id}", insight.BusinessKey);
            Assert.Equal(InsightSeverity.Critical, insight.Severity);
            Assert.Equal(workspaceId, insight.WorkspaceId);
        });
    }

    [Fact]
    public async Task GenerateInsights_flags_low_stock_item_as_warning()
    {
        await WithServiceAsync(async (service, items, _, workspaceId) =>
        {
            InventoryItem low = await CreateItemAsync(items, workspaceId, quantity: 3m, threshold: 5m);

            IReadOnlyList<BusinessInsight> insights = await service.GenerateInventoryInsightsAsync(AsOf);

            BusinessInsight insight = Assert.Single(insights);
            Assert.Equal($"inventory:low-stock:{low.Id}", insight.BusinessKey);
            Assert.Equal(InsightSeverity.Warning, insight.Severity);
        });
    }

    // --- Helpers ---

    private static InventoryMovement Movement(
        InventoryItem item,
        InventoryMovementType type,
        decimal quantity,
        DateTime occurredAt)
        => new()
        {
            Id = Guid.NewGuid(),
            WorkspaceId = item.WorkspaceId,
            InventoryItemId = item.Id,
            MovementType = type,
            Quantity = quantity,
            OccurredAt = occurredAt,
        };

    private static async Task<InventoryItem> CreateItemAsync(
        IInventoryItemRepository items,
        Guid workspaceId,
        decimal quantity,
        decimal threshold)
    {
        var item = new InventoryItem
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Name = "库存项 " + Guid.NewGuid().ToString("N")[..6],
            QuantityAvailable = quantity,
            ReorderThreshold = threshold,
        };

        return await items.CreateAsync(item);
    }

    private static async Task WithServiceAsync(
        Func<CommerceInventoryService, IInventoryItemRepository, IInventoryMovementRepository, Guid, Task> body)
    {
        string path = Path.Combine(Path.GetTempPath(), $"orderly-inventory-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new SqliteConnectionFactory(path);
            await new CommerceSchemaInitializer(factory).InitializeAsync();

            var items = new InventoryItemRepository(factory);
            var movements = new InventoryMovementRepository(factory);
            var service = new CommerceInventoryService(items, movements);

            await body(service, items, movements, Guid.NewGuid());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (string file in new[] { path, path + "-wal", path + "-shm", path + "-journal" })
            {
                try
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                }
                catch (IOException)
                {
                    // Best-effort cleanup of temp files.
                }
            }
        }
    }
}
