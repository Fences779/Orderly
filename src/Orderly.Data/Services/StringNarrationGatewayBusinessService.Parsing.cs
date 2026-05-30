using System.Text.Json;
using Orderly.Core.Models;

namespace Orderly.Data.Services;

public sealed partial class StringNarrationGatewayBusinessService
{
    private static IReadOnlyList<StringNarrationInventoryItem> ParseInventoryItems(JsonElement root)
    {
        var array = GetFirstArray(root, "items", "inventory", "skus", "list");
        if (array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var items = new List<StringNarrationInventoryItem>();
        foreach (var element in array.EnumerateArray())
        {
            items.Add(new StringNarrationInventoryItem
            {
                Id = ReadString(element, "_id", "id"),
                SkuId = ReadString(element, "skuId", "_id", "id"),
                Name = ReadString(element, "name", "title"),
                Category = ReadString(element, "category"),
                BasePrice = ReadDecimal(element, "basePrice"),
                CostPrice = ReadDecimal(element, "costPrice"),
                PurchasePrice = ReadDecimal(element, "purchasePrice"),
                StockOnHand = ReadDecimal(element, "stockOnHand", "stock", "quantity"),
                StockReserved = ReadDecimal(element, "stockReserved", "reservedStock"),
                SafetyStock = ReadDecimal(element, "safetyStock", "stockWarningThreshold"),
                StockUnit = ReadString(element, "stockUnit", "unit"),
                StockLocation = ReadString(element, "stockLocation", "location"),
                SupplierName = ReadString(element, "supplierName", "supplier"),
                InventoryRemark = ReadString(element, "inventoryRemark", "remark"),
                ReorderEnabled = ReadBool(element, "reorderEnabled"),
                Enabled = !TryGet(element, "enabled", out var enabledElement) || ReadBool(enabledElement),
                LastRestockedAt = ReadLong(element, "lastRestockedAt", "restockedAt"),
                UpdatedAt = ReadLong(element, "updatedAt"),
                Tags = ReadStringArray(element, "tags")
            });
        }

        return items;
    }

    private static IReadOnlyList<StringNarrationInventoryMovement> ParseInventoryMovements(JsonElement root)
    {
        var array = GetFirstArray(root, "recentMovements", "movements", "inventoryMovements");
        if (array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var items = new List<StringNarrationInventoryMovement>();
        foreach (var element in array.EnumerateArray())
        {
            items.Add(new StringNarrationInventoryMovement
            {
                Id = ReadString(element, "_id", "id"),
                SkuId = ReadString(element, "skuId"),
                SkuName = ReadString(element, "skuName", "name"),
                MovementType = ReadString(element, "movementType", "type"),
                Quantity = ReadDecimal(element, "quantity"),
                UnitCost = ReadDecimal(element, "unitCost"),
                TotalCost = ReadDecimal(element, "totalCost"),
                RelatedOrderNo = ReadString(element, "relatedOrderNo", "orderNo"),
                OperatorId = ReadString(element, "operatorId", "operator"),
                Note = ReadString(element, "note", "remark"),
                OccurredAt = ReadLong(element, "occurredAt", "createdAt")
            });
        }

        return items;
    }

    private static StringNarrationInventoryManagementDashboardSummary ParseInventoryManagementDashboardSummary(
        JsonElement root,
        IReadOnlyList<StringNarrationInventoryManagementDashboardItem> items)
    {
        var summaryElement = TryGet(root, "summary", out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : default;

        var lowStockCount = items.Count(item => string.Equals(item.Status, "low_stock", StringComparison.OrdinalIgnoreCase));
        var fastSellingCount = items.Count(item => string.Equals(item.Status, "fast_selling", StringComparison.OrdinalIgnoreCase));
        var lowSellingCount = items.Count(item => string.Equals(item.Status, "low_selling", StringComparison.OrdinalIgnoreCase));

        if (summaryElement.ValueKind != JsonValueKind.Object)
        {
            return new StringNarrationInventoryManagementDashboardSummary
            {
                LowStockCount = lowStockCount,
                FastSellingCount = fastSellingCount,
                LowSellingCount = lowSellingCount,
                InventoryHealthStatus = "暂不可用",
                InventoryHealthSummary = "库存看板摘要暂不可用",
                InventoryWarningCount = lowStockCount
            };
        }

        return new StringNarrationInventoryManagementDashboardSummary
        {
            AvgOrderMaterialUsage = ReadNullableDecimal(summaryElement, "avgOrderMaterialUsage"),
            AvgMaterialUnitCost = ReadNullableDecimal(summaryElement, "avgMaterialUnitCost"),
            AvgBraceletSalePrice = ReadNullableDecimal(summaryElement, "avgBraceletSalePrice"),
            AvgBraceletCostPrice = ReadNullableDecimal(summaryElement, "avgBraceletCostPrice"),
            GrossMarginRate = ReadNullableDecimal(summaryElement, "grossMarginRate"),
            LowStockCount = HasAnyProperty(summaryElement, "lowStockCount") ? ReadInt(summaryElement, "lowStockCount") : lowStockCount,
            FastSellingCount = HasAnyProperty(summaryElement, "fastSellingCount") ? ReadInt(summaryElement, "fastSellingCount") : fastSellingCount,
            LowSellingCount = HasAnyProperty(summaryElement, "lowSellingCount") ? ReadInt(summaryElement, "lowSellingCount") : lowSellingCount,
            InventoryHealthStatus = ReadString(summaryElement, "inventoryHealthStatus"),
            InventoryHealthSummary = ReadString(summaryElement, "inventoryHealthSummary"),
            InventoryWarningCount = ReadInt(summaryElement, "inventoryWarningCount")
        };
    }

    private static StringNarrationInventoryManagementDashboardFilterOptions ParseInventoryManagementDashboardFilterOptions(JsonElement root)
    {
        if (!(TryGet(root, "filterOptions", out var filterOptions) && filterOptions.ValueKind == JsonValueKind.Object))
        {
            return new StringNarrationInventoryManagementDashboardFilterOptions
            {
                Statuses = BuildDefaultDashboardStatuses()
            };
        }

        var categories = GetFirstArray(filterOptions, "categories").ValueKind == JsonValueKind.Array
            ? GetFirstArray(filterOptions, "categories").EnumerateArray()
                .Select(ReadStringValue)
                .Where(category => !string.IsNullOrWhiteSpace(category))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];

        return new StringNarrationInventoryManagementDashboardFilterOptions
        {
            Categories = categories,
            Statuses = ParseInventoryManagementDashboardStatuses(filterOptions),
            DefaultSortBy = ReadString(filterOptions, "defaultSortBy") is var sortBy && !string.IsNullOrWhiteSpace(sortBy) ? sortBy : "sold30dRatio",
            DefaultSortDirection = ReadString(filterOptions, "defaultSortDirection") is var sortDirection && !string.IsNullOrWhiteSpace(sortDirection) ? sortDirection : "desc"
        };
    }

    private static IReadOnlyList<StringNarrationInventoryManagementDashboardFilterOption> ParseInventoryManagementDashboardStatuses(JsonElement filterOptions)
    {
        var statusesArray = GetFirstArray(filterOptions, "statuses");
        if (statusesArray.ValueKind != JsonValueKind.Array)
        {
            return BuildDefaultDashboardStatuses();
        }

        var statuses = new List<StringNarrationInventoryManagementDashboardFilterOption>();
        foreach (var item in statusesArray.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                var value = ReadString(item, "value", "code");
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                statuses.Add(new StringNarrationInventoryManagementDashboardFilterOption
                {
                    Value = value,
                    Label = ReadString(item, "label", "name")
                });
            }
            else
            {
                var value = ReadStringValue(item);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                statuses.Add(new StringNarrationInventoryManagementDashboardFilterOption
                {
                    Value = value
                });
            }
        }

        return statuses.Count == 0 ? BuildDefaultDashboardStatuses() : statuses;
    }

    private static IReadOnlyList<StringNarrationInventoryManagementDashboardFilterOption> BuildDefaultDashboardStatuses()
    {
        return
        [
            new StringNarrationInventoryManagementDashboardFilterOption { Value = "all", Label = "全部状态" },
            new StringNarrationInventoryManagementDashboardFilterOption { Value = "fast_selling", Label = "动销偏快" },
            new StringNarrationInventoryManagementDashboardFilterOption { Value = "low_selling", Label = "低动销" },
            new StringNarrationInventoryManagementDashboardFilterOption { Value = "normal", Label = "正常" },
            new StringNarrationInventoryManagementDashboardFilterOption { Value = "low_stock", Label = "低库存" },
            new StringNarrationInventoryManagementDashboardFilterOption { Value = "disabled", Label = "停用" }
        ];
    }

    private static IReadOnlyList<StringNarrationInventoryManagementDashboardItem> ParseInventoryManagementDashboardItems(JsonElement root)
    {
        var array = GetFirstArray(root, "items", "list", "inventory");
        if (array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var items = new List<StringNarrationInventoryManagementDashboardItem>();
        foreach (var element in array.EnumerateArray())
        {
            var currentStockQty = ReadDecimal(element, "currentStockQty", "availableStock", "stockOnHand", "stock", "quantity");
            var sold7dQty = ReadDecimal(element, "sold7dQty");
            var sold7dRatio = ReadDecimal(element, "sold7dRatio");
            var sold30dQty = ReadDecimal(element, "sold30dQty");
            var sold30dRatio = ReadDecimal(element, "sold30dRatio");
            var consumed7dQty = ReadDecimal(element, "consumed7dQty", "sold7dQty");
            var consumed30dQty = ReadDecimal(element, "consumed30dQty", "sold30dQty");
            var safeStockSuggestedQty = ReadNullableDecimal(element, "safeStockSuggestedQty", "safeStockSuggested", "safetyStock");
            var normalizedStatus = NormalizeInventoryDashboardStatus(
                ReadString(element, "status"),
                currentStockQty,
                safeStockSuggestedQty,
                sold30dQty,
                sold30dRatio);
            items.Add(new StringNarrationInventoryManagementDashboardItem
            {
                MaterialId = ReadString(element, "materialId", "id", "_id", "skuId"),
                MaterialName = ReadString(element, "materialName", "name", "title", "skuName"),
                Category = ReadString(element, "category"),
                CurrentStockQty = currentStockQty,
                StockUnit = ReadString(element, "stockUnit", "unit"),
                Sold7dQty = sold7dQty,
                Sold7dRatio = sold7dRatio,
                Sold30dQty = sold30dQty,
                Sold30dRatio = sold30dRatio,
                Consumed7dQty = consumed7dQty,
                Consumed30dQty = consumed30dQty,
                SafeStockSuggestedQty = safeStockSuggestedQty,
                Status = normalizedStatus,
                StatusLabel = BuildInventoryDashboardStatusLabel(normalizedStatus),
                UnitCost = ReadNullableDecimal(element, "unitCost", "purchasePrice", "costPrice"),
                LastRestockedAt = ReadLong(element, "lastRestockedAt", "restockedAt", "updatedAt"),
                SupplierName = ReadString(element, "supplierName", "supplier"),
                Remark = ReadString(element, "remark", "inventoryRemark", "note")
            });
        }

        return items;
    }

    private static StringNarrationInventoryManagementDashboardPageInfo ParseInventoryManagementDashboardPageInfo(
        JsonElement root,
        int? totalOverride = null,
        int requestedPage = 1,
        int requestedPageSize = 10)
    {
        if (!(TryGet(root, "pageInfo", out var pageInfo) && pageInfo.ValueKind == JsonValueKind.Object))
        {
            var page = ReadInt(root, "page");
            var pageSize = ReadInt(root, "pageSize");
            var total = totalOverride ?? ReadInt(root, "total", "count");
            return new StringNarrationInventoryManagementDashboardPageInfo
            {
                Page = page > 0 ? page : Math.Max(requestedPage, 1),
                PageSize = pageSize > 0 ? pageSize : Math.Max(requestedPageSize, 1),
                Total = total,
                TotalPages = (pageSize > 0 ? pageSize : Math.Max(requestedPageSize, 1)) > 0
                    ? (int)Math.Ceiling(total / (decimal)(pageSize > 0 ? pageSize : Math.Max(requestedPageSize, 1)))
                    : 0
            };
        }

        var parsedPage = ReadInt(pageInfo, "page");
        var parsedPageSize = ReadInt(pageInfo, "pageSize");
        var parsedTotal = totalOverride ?? ReadInt(pageInfo, "total", "count");
        var parsedTotalPages = ReadInt(pageInfo, "totalPages");
        return new StringNarrationInventoryManagementDashboardPageInfo
        {
            Page = parsedPage > 0 ? parsedPage : Math.Max(requestedPage, 1),
            PageSize = parsedPageSize > 0 ? parsedPageSize : Math.Max(requestedPageSize, 1),
            Total = parsedTotal,
            TotalPages = parsedTotalPages > 0
                ? parsedTotalPages
                : ((parsedPageSize > 0 ? parsedPageSize : Math.Max(requestedPageSize, 1)) > 0
                    ? (int)Math.Ceiling(parsedTotal / (decimal)(parsedPageSize > 0 ? parsedPageSize : Math.Max(requestedPageSize, 1)))
                    : 0)
        };
    }

    private static StringNarrationCashflowHealthDashboardResult ParseCashflowHealthDashboard(JsonElement root)
    {
        var dataAvailability = ParseDataAvailability(root);
        return new StringNarrationCashflowHealthDashboardResult
        {
            Range = ReadString(root, "range"),
            StartAt = ReadLong(root, "startAt"),
            EndAt = ReadLong(root, "endAt"),
            UpdatedAt = ReadLong(root, "updatedAt", "calculatedAt", "syncedAt"),
            DataAvailability = dataAvailability,
            Summary = ParseCashflowHealthDashboardSummary(root, dataAvailability),
            TrendItems = ParseCashflowHealthDashboardTrendItems(root),
            IncomeBreakdown = ParseCashflowHealthDashboardBreakdown(root, "incomeBreakdown"),
            ExpenseBreakdown = ParseCashflowHealthDashboardBreakdown(root, "expenseBreakdown"),
            UpcomingCashItems = ParseCashflowHealthDashboardUpcomingCashItems(root, dataAvailability),
            Advice = ParseCashflowHealthDashboardAdvice(root)
        };
    }

    private static StringNarrationCashflowHealthDashboardSummary ParseCashflowHealthDashboardSummary(
        JsonElement root,
        StringNarrationBusinessDataAvailability dataAvailability)
    {
        if (!(TryGet(root, "summary", out var summary) && summary.ValueKind == JsonValueKind.Object))
        {
            return new StringNarrationCashflowHealthDashboardSummary();
        }

        return new StringNarrationCashflowHealthDashboardSummary
        {
            CashFlowHealthScore = ReadNullableInt(summary, "cashFlowHealthScore"),
            CashFlowHealthLevel = ReadString(summary, "cashFlowHealthLevel"),
            CashFlowHealthSummary = ReadString(summary, "cashFlowHealthSummary"),
            CashBalanceAmount = ReadNullableDecimal(summary, "cashBalanceAmount"),
            AvailableCashAmount = ReadNullableDecimal(summary, "availableCashAmount"),
            ReceivableAmount = dataAvailability.Receivable.IsAvailable ? ReadNullableDecimal(summary, "receivableAmount") : null,
            PayableAmount = dataAvailability.Payable.IsAvailable ? ReadNullableDecimal(summary, "payableAmount") : null,
            AvgDailyExpense7d = ReadDecimal(summary, "avgDailyExpense7d"),
            SupportDays = ReadNullableInt(summary, "supportDays")
        };
    }

    private static IReadOnlyList<StringNarrationCashflowHealthDashboardTrendItem> ParseCashflowHealthDashboardTrendItems(JsonElement root)
    {
        if (!(TryGet(root, "trendItems", out var array) && array.ValueKind == JsonValueKind.Array))
        {
            return [];
        }

        var items = new List<StringNarrationCashflowHealthDashboardTrendItem>();
        foreach (var element in array.EnumerateArray())
        {
            items.Add(new StringNarrationCashflowHealthDashboardTrendItem
            {
                Date = ReadString(element, "date"),
                IncomeAmount = ReadDecimal(element, "incomeAmount"),
                ExpenseAmount = ReadDecimal(element, "expenseAmount"),
                NetCashflowAmount = ReadDecimal(element, "netCashflowAmount")
            });
        }

        return items;
    }

    private static StringNarrationCashflowHealthDashboardBreakdown ParseCashflowHealthDashboardBreakdown(JsonElement root, string name)
    {
        if (!(TryGet(root, name, out var breakdown) && breakdown.ValueKind == JsonValueKind.Object))
        {
            return new StringNarrationCashflowHealthDashboardBreakdown();
        }

        return new StringNarrationCashflowHealthDashboardBreakdown
        {
            TotalAmount = ReadDecimal(breakdown, "totalAmount"),
            Items = ParseCashflowHealthDashboardBreakdownItems(breakdown)
        };
    }

    private static IReadOnlyList<StringNarrationCashflowHealthDashboardBreakdownItem> ParseCashflowHealthDashboardBreakdownItems(JsonElement root)
    {
        var array = GetFirstArray(root, "items");
        if (array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var items = new List<StringNarrationCashflowHealthDashboardBreakdownItem>();
        foreach (var element in array.EnumerateArray())
        {
            items.Add(new StringNarrationCashflowHealthDashboardBreakdownItem
            {
                Category = ReadString(element, "category"),
                Amount = ReadDecimal(element, "amount"),
                Percent = ReadDecimal(element, "percent")
            });
        }

        return items;
    }

    private static IReadOnlyList<StringNarrationCashflowHealthDashboardUpcomingCashItem> ParseCashflowHealthDashboardUpcomingCashItems(
        JsonElement root,
        StringNarrationBusinessDataAvailability dataAvailability)
    {
        var array = GetFirstArray(root, "upcomingCashItems");
        if (array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var items = new List<StringNarrationCashflowHealthDashboardUpcomingCashItem>();
        foreach (var element in array.EnumerateArray())
        {
            var type = ReadString(element, "type");
            var availability = ResolveCashflowAvailabilityByType(dataAvailability, type);
            var isCompatPlaceholder = availability.IsCompat || availability.IsUnavailable;
            items.Add(new StringNarrationCashflowHealthDashboardUpcomingCashItem
            {
                Type = type,
                Label = BuildUpcomingCashItemLabel(ReadString(element, "label"), type, isCompatPlaceholder),
                Amount = ReadDecimal(element, "amount"),
                Count = ReadInt(element, "count"),
                Note = BuildUpcomingCashItemNote(ReadString(element, "note"), availability, isCompatPlaceholder),
                IsCompatPlaceholder = isCompatPlaceholder
            });
        }

        return items;
    }

    private static StringNarrationCashflowHealthDashboardAdvice ParseCashflowHealthDashboardAdvice(JsonElement root)
    {
        if (!(TryGet(root, "advice", out var advice) && advice.ValueKind == JsonValueKind.Object))
        {
            return new StringNarrationCashflowHealthDashboardAdvice();
        }

        return new StringNarrationCashflowHealthDashboardAdvice
        {
            HealthTitle = ReadString(advice, "healthTitle"),
            HealthDescription = ReadString(advice, "healthDescription"),
            RestockSuggestionAmount = ReadNullableDecimal(advice, "restockSuggestionAmount"),
            RiskHint = ReadString(advice, "riskHint"),
            NextFocus = ReadStringArray(advice, "nextFocus")
        };
    }

    private static IReadOnlyList<StringNarrationCashflowEntry> ParseCashflowEntries(JsonElement root)
    {
        var array = GetFirstArray(root, "entries", "cashflow", "cashflows", "list");
        if (array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var entries = new List<StringNarrationCashflowEntry>();
        foreach (var element in array.EnumerateArray())
        {
            entries.Add(new StringNarrationCashflowEntry
            {
                Id = ReadString(element, "_id", "id"),
                Direction = ReadString(element, "direction", "type"),
                Amount = ReadDecimal(element, "amount"),
                Category = ReadString(element, "category"),
                PaymentMethod = ReadString(element, "paymentMethod", "channel"),
                Status = ReadString(element, "status"),
                RelatedOrderId = ReadString(element, "relatedOrderId", "orderId"),
                RelatedOrderNo = ReadString(element, "relatedOrderNo", "orderNo"),
                RelatedQuoteId = ReadString(element, "relatedQuoteId", "quoteId"),
                RelatedSkuId = ReadString(element, "relatedSkuId", "skuId"),
                CounterpartyName = ReadString(element, "counterpartyName", "counterparty"),
                OperatorId = ReadString(element, "operatorId", "operator"),
                Note = ReadString(element, "note", "remark"),
                OccurredAt = ReadLong(element, "occurredAt", "paidAt", "createdAt"),
                CreatedAt = ReadLong(element, "createdAt")
            });
        }

        return entries;
    }

    private static StringNarrationCashflowSummary ParseCashflowSummary(JsonElement element, IReadOnlyList<StringNarrationCashflowEntry> entries)
    {
        var fallback = BuildCashflowSummary(entries);
        return new StringNarrationCashflowSummary
        {
            IncomeTotal = ReadDecimal(element, "incomeTotal", "income") is var income && income != 0 ? income : fallback.IncomeTotal,
            ExpenseTotal = ReadDecimal(element, "expenseTotal", "expense") is var expense && expense != 0 ? expense : fallback.ExpenseTotal,
            NetAmount = ReadDecimal(element, "netAmount", "net") is var net && net != 0 ? net : fallback.NetAmount,
            ReceivableAmount = ReadNullableDecimal(element, "receivableAmount", "receivable"),
            PayableAmount = ReadNullableDecimal(element, "payableAmount", "payable"),
            EntryCount = ReadInt(element, "entryCount", "count") is var count && count > 0 ? count : fallback.EntryCount
        };
    }

    private static StringNarrationCashflowSummary BuildCashflowSummary(IReadOnlyList<StringNarrationCashflowEntry> entries)
    {
        var income = entries.Where(entry => entry.IsIncome).Sum(entry => entry.Amount);
        var expense = entries.Where(entry => !entry.IsIncome).Sum(entry => entry.Amount);
        return new StringNarrationCashflowSummary
        {
            IncomeTotal = income,
            ExpenseTotal = expense,
            NetAmount = income - expense,
            EntryCount = entries.Count
        };
    }
}
