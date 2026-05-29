using System.Text.Json;
using Orderly.Core.Models;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class StringNarrationGatewayBusinessService : IStringNarrationBusinessService
{
    private const string InventoryListAction = "inventoryList";
    private const string InventoryManagementDashboardAction = "inventoryManagementDashboard";
    private const string CashflowListAction = "cashflowList";
    private const string CashflowHealthDashboardAction = "cashflowHealthDashboard";

    private readonly StringNarrationGatewayClient _client;

    public StringNarrationGatewayBusinessService(StringNarrationGatewayClient client)
    {
        _client = client;
    }

    public async Task<StringNarrationInventoryListResult> GetInventoryAsync(StringNarrationInventoryQuery query, CancellationToken cancellationToken = default)
    {
        try
        {
            query ??= new StringNarrationInventoryQuery();
            var root = await _client.InvokeAsync(InventoryListAction, query, cancellationToken);
            var payload = GetPayloadRoot(root);
            var items = ParseInventoryItems(payload);
            var movements = ParseInventoryMovements(payload);
            return new StringNarrationInventoryListResult
            {
                Items = items,
                RecentMovements = movements,
                Total = ReadInt(payload, "total", "count"),
                UpdatedAt = ReadLong(payload, "updatedAt", "calculatedAt", "syncedAt")
            };
        }
        catch (InvalidOperationException ex)
        {
            throw WrapActionException(InventoryListAction, ex);
        }
    }

    public async Task<StringNarrationInventoryManagementDashboardResult> GetInventoryManagementDashboardAsync(
        StringNarrationInventoryManagementDashboardRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            request ??= new StringNarrationInventoryManagementDashboardRequest();
            var requestedStatus = NormalizeInventoryDashboardStatusValue(request.Status);
            var shouldFilterClientSide = RequiresClientSideInventoryStatusFilter(requestedStatus);
            var gatewayRequest = CloneInventoryManagementDashboardRequest(request);
            if (shouldFilterClientSide)
            {
                gatewayRequest.Status = "all";
            }

            var root = await _client.InvokeAsync(InventoryManagementDashboardAction, gatewayRequest, cancellationToken);
            var payload = GetPayloadRoot(root);
            ValidateInventoryManagementDashboardPayload(payload);
            var items = ParseInventoryManagementDashboardItems(payload);
            if (shouldFilterClientSide)
            {
                items = ApplyInventoryStatusFilter(items, requestedStatus);
            }

            return new StringNarrationInventoryManagementDashboardResult
            {
                UpdatedAt = ReadLong(payload, "updatedAt", "calculatedAt", "syncedAt"),
                DataAvailability = ParseDataAvailability(payload),
                Summary = ParseInventoryManagementDashboardSummary(payload, items),
                FilterOptions = ParseInventoryManagementDashboardFilterOptions(payload),
                Items = items,
                PageInfo = ParseInventoryManagementDashboardPageInfo(
                    payload,
                    shouldFilterClientSide ? items.Count : null,
                    gatewayRequest.Page,
                    gatewayRequest.PageSize)
            };
        }
        catch (InvalidOperationException ex)
        {
            throw WrapActionException(InventoryManagementDashboardAction, ex);
        }
    }

    public async Task<StringNarrationCashflowListResult> GetCashflowAsync(StringNarrationCashflowQuery query, CancellationToken cancellationToken = default)
    {
        try
        {
            query ??= new StringNarrationCashflowQuery();
            var root = await _client.InvokeAsync(CashflowListAction, query, cancellationToken);
            var payload = GetPayloadRoot(root);
            var entries = ParseCashflowEntries(payload);
            var summary = TryGet(payload, "summary", out var summaryElement) && summaryElement.ValueKind == JsonValueKind.Object
                ? ParseCashflowSummary(summaryElement, entries)
                : BuildCashflowSummary(entries);

            return new StringNarrationCashflowListResult
            {
                Entries = entries,
                Summary = summary,
                Total = ReadInt(payload, "total", "count"),
                UpdatedAt = ReadLong(payload, "updatedAt", "calculatedAt", "syncedAt")
            };
        }
        catch (InvalidOperationException ex)
        {
            throw WrapActionException(CashflowListAction, ex);
        }
    }

    public async Task<StringNarrationCashflowHealthDashboardResult> GetCashflowHealthDashboardAsync(
        StringNarrationCashflowHealthDashboardRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            request ??= new StringNarrationCashflowHealthDashboardRequest();
            var root = await _client.InvokeAsync(CashflowHealthDashboardAction, request, cancellationToken);
            var payload = GetPayloadRoot(root);
            ValidateCashflowHealthDashboardPayload(payload);
            return ParseCashflowHealthDashboard(payload);
        }
        catch (InvalidOperationException ex)
        {
            throw WrapActionException(CashflowHealthDashboardAction, ex);
        }
    }

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

    private static JsonElement GetPayloadRoot(JsonElement root)
    {
        if (TryGet(root, "data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            return data;
        }

        if (TryGet(root, "payload", out var payload) && payload.ValueKind == JsonValueKind.Object)
        {
            return payload;
        }

        return root;
    }

    private static void ValidateInventoryManagementDashboardPayload(JsonElement root)
    {
        RequireObject(root, "summary", InventoryManagementDashboardAction);
        RequireObject(root, "filterOptions", InventoryManagementDashboardAction);
        RequireObject(root, "pageInfo", InventoryManagementDashboardAction);
        RequireArray(root, "items", InventoryManagementDashboardAction);
        RequireObject(root, "dataAvailability", InventoryManagementDashboardAction);

        var summary = root.GetProperty("summary");
        RequireProperties(summary, InventoryManagementDashboardAction,
            "summary.avgOrderMaterialUsage",
            "summary.avgMaterialUnitCost",
            "summary.avgBraceletSalePrice",
            "summary.avgBraceletCostPrice",
            "summary.grossMarginRate",
            "summary.lowStockCount",
            "summary.fastSellingCount",
            "summary.lowSellingCount",
            "summary.inventoryHealthStatus",
            "summary.inventoryHealthSummary",
            "summary.inventoryWarningCount");

        var filterOptions = root.GetProperty("filterOptions");
        RequireArray(filterOptions, "categories", InventoryManagementDashboardAction);
        RequireArray(filterOptions, "statuses", InventoryManagementDashboardAction);
        RequireProperties(filterOptions, InventoryManagementDashboardAction, "filterOptions.defaultSortBy", "filterOptions.defaultSortDirection");

        RequireProperties(root.GetProperty("pageInfo"), InventoryManagementDashboardAction,
            "pageInfo.page",
            "pageInfo.pageSize",
            "pageInfo.total",
            "pageInfo.totalPages");

        var dataAvailability = root.GetProperty("dataAvailability");
        RequireObject(dataAvailability, "inventorySource", InventoryManagementDashboardAction);
        RequireObject(dataAvailability, "materialConsumption", InventoryManagementDashboardAction);

        foreach (var item in root.GetProperty("items").EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"{InventoryManagementDashboardAction} 返回字段 items[] 必须是对象。");
            }

            RequireProperties(item, InventoryManagementDashboardAction,
                "items[].materialId",
                "items[].materialName",
                "items[].category",
                "items[].currentStockQty",
                "items[].stockUnit",
                "items[].sold7dQty",
                "items[].sold7dRatio",
                "items[].sold30dQty",
                "items[].sold30dRatio",
                "items[].consumed7dQty",
                "items[].consumed30dQty",
                "items[].safeStockSuggestedQty",
                "items[].status",
                "items[].statusLabel",
                "items[].unitCost",
                "items[].lastRestockedAt",
                "items[].supplierName",
                "items[].remark");
        }
    }

    private static void ValidateCashflowHealthDashboardPayload(JsonElement root)
    {
        RequireObject(root, "summary", CashflowHealthDashboardAction);
        RequireArray(root, "trendItems", CashflowHealthDashboardAction);
        RequireObject(root, "incomeBreakdown", CashflowHealthDashboardAction);
        RequireObject(root, "expenseBreakdown", CashflowHealthDashboardAction);
        RequireArray(root, "upcomingCashItems", CashflowHealthDashboardAction);
        RequireObject(root, "advice", CashflowHealthDashboardAction);
        RequireObject(root, "dataAvailability", CashflowHealthDashboardAction);

        RequireProperties(root.GetProperty("summary"), CashflowHealthDashboardAction,
            "summary.cashFlowHealthScore",
            "summary.cashFlowHealthLevel",
            "summary.cashFlowHealthSummary",
            "summary.cashBalanceAmount",
            "summary.availableCashAmount",
            "summary.receivableAmount",
            "summary.payableAmount",
            "summary.avgDailyExpense7d",
            "summary.supportDays");

        ValidateCashflowTrendItems(root.GetProperty("trendItems"));
        ValidateCashflowBreakdown(root.GetProperty("incomeBreakdown"), "incomeBreakdown");
        ValidateCashflowBreakdown(root.GetProperty("expenseBreakdown"), "expenseBreakdown");

        foreach (var item in root.GetProperty("upcomingCashItems").EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"{CashflowHealthDashboardAction} 返回字段 upcomingCashItems[] 必须是对象。");
            }

            RequireProperties(item, CashflowHealthDashboardAction,
                "upcomingCashItems[].type",
                "upcomingCashItems[].label",
                "upcomingCashItems[].amount",
                "upcomingCashItems[].count",
                "upcomingCashItems[].note");
        }

        RequireProperties(root.GetProperty("advice"), CashflowHealthDashboardAction,
            "advice.healthTitle",
            "advice.healthDescription",
            "advice.restockSuggestionAmount",
            "advice.riskHint",
            "advice.nextFocus");

        var dataAvailability = root.GetProperty("dataAvailability");
        RequireObject(dataAvailability, "cashBalance", CashflowHealthDashboardAction);
        RequireObject(dataAvailability, "receivable", CashflowHealthDashboardAction);
        RequireObject(dataAvailability, "payable", CashflowHealthDashboardAction);
    }

    private static void ValidateCashflowTrendItems(JsonElement trendItems)
    {
        foreach (var item in trendItems.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"{CashflowHealthDashboardAction} 返回字段 trendItems[] 必须是对象。");
            }

            RequireProperties(item, CashflowHealthDashboardAction,
                "trendItems[].date",
                "trendItems[].incomeAmount",
                "trendItems[].expenseAmount",
                "trendItems[].netCashflowAmount");
        }
    }

    private static void ValidateCashflowBreakdown(JsonElement breakdown, string fieldName)
    {
        RequireProperties(breakdown, CashflowHealthDashboardAction, $"{fieldName}.totalAmount");
        RequireArray(breakdown, "items", CashflowHealthDashboardAction);
    }

    private static void RequireObject(JsonElement root, string propertyName, string action)
    {
        if (!TryGet(root, propertyName, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"{action} 返回缺少对象字段 {propertyName}。");
        }
    }

    private static void RequireArray(JsonElement root, string propertyName, string action)
    {
        if (!TryGet(root, propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"{action} 返回缺少数组字段 {propertyName}。");
        }
    }

    private static void RequireProperties(JsonElement root, string action, params string[] propertyPaths)
    {
        foreach (var path in propertyPaths)
        {
            var propertyName = path[(path.LastIndexOf('.') + 1)..];
            if (!TryGet(root, propertyName, out _))
            {
                throw new InvalidOperationException($"{action} 返回缺少字段 {path}。");
            }
        }
    }

    private static JsonElement GetFirstArray(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGet(element, name, out var property) && property.ValueKind == JsonValueKind.Array)
            {
                return property;
            }
        }

        return default;
    }

    private static bool TryGet(JsonElement element, string name, out JsonElement property)
    {
        property = default;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out property);
    }

    private static bool HasAnyProperty(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGet(element, name, out _))
            {
                return true;
            }
        }

        return false;
    }

    private static InvalidOperationException WrapActionException(string action, InvalidOperationException ex)
    {
        return ex.Message.Contains($"action={action}", StringComparison.OrdinalIgnoreCase)
            ? ex
            : new InvalidOperationException($"调用串述 adminPcGateway action={action} 失败：{ex.Message}", ex);
    }

    private static StringNarrationInventoryManagementDashboardRequest CloneInventoryManagementDashboardRequest(
        StringNarrationInventoryManagementDashboardRequest request)
    {
        return new StringNarrationInventoryManagementDashboardRequest
        {
            Keyword = request.Keyword,
            Category = request.Category,
            Status = request.Status,
            SortBy = request.SortBy,
            SortDirection = request.SortDirection,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }

    private static List<StringNarrationInventoryManagementDashboardItem> ApplyInventoryStatusFilter(
        IReadOnlyList<StringNarrationInventoryManagementDashboardItem> items,
        string status)
    {
        return items
            .Where(item => string.Equals(item.Status, status, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static bool RequiresClientSideInventoryStatusFilter(string status)
    {
        return string.Equals(status, "fast_selling", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "low_selling", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeInventoryDashboardStatusValue(string? status)
    {
        return string.IsNullOrWhiteSpace(status) ? "all" : status.Trim();
    }

    private static string NormalizeInventoryDashboardStatus(
        string rawStatus,
        decimal currentStockQty,
        decimal? safeStockSuggestedQty,
        decimal sold30dQty,
        decimal sold30dRatio)
    {
        var normalized = NormalizeInventoryDashboardStatusValue(rawStatus);
        if (string.Equals(normalized, "disabled", StringComparison.OrdinalIgnoreCase))
        {
            return "disabled";
        }

        if (safeStockSuggestedQty is > 0 && currentStockQty <= safeStockSuggestedQty.Value)
        {
            return "low_stock";
        }

        if (string.Equals(normalized, "low_stock", StringComparison.OrdinalIgnoreCase))
        {
            return "low_stock";
        }

        if (IsFastSellingRatio(sold30dRatio))
        {
            return "fast_selling";
        }

        if (sold30dQty <= 0)
        {
            return "low_selling";
        }

        return string.Equals(normalized, "all", StringComparison.OrdinalIgnoreCase) ? "normal" : normalized;
    }

    private static bool IsFastSellingRatio(decimal ratio)
    {
        return ratio > 1m ? ratio >= 50m : ratio >= 0.5m;
    }

    private static string BuildInventoryDashboardStatusLabel(string status)
    {
        return status switch
        {
            "fast_selling" => "动销偏快",
            "low_selling" => "低动销",
            "low_stock" => "低库存",
            "disabled" => "停用",
            _ => "正常"
        };
    }

    private static StringNarrationBusinessDataAvailabilityItem ResolveCashflowAvailabilityByType(
        StringNarrationBusinessDataAvailability dataAvailability,
        string type)
    {
        return type switch
        {
            "receivable" => dataAvailability.Receivable,
            "payable" => dataAvailability.Payable,
            _ => new StringNarrationBusinessDataAvailabilityItem()
        };
    }

    private static string BuildUpcomingCashItemLabel(string label, string type, bool isCompatPlaceholder)
    {
        var resolvedLabel = !string.IsNullOrWhiteSpace(label)
            ? label.Trim()
            : type switch
            {
                "receivable" => "待收款",
                "payable" => "待付款",
                _ => "未命名"
            };

        return isCompatPlaceholder ? $"{resolvedLabel}（兼容占位）" : resolvedLabel;
    }

    private static string BuildUpcomingCashItemNote(
        string rawNote,
        StringNarrationBusinessDataAvailabilityItem availability,
        bool isCompatPlaceholder)
    {
        if (isCompatPlaceholder)
        {
            var reason = string.IsNullOrWhiteSpace(availability.Reason)
                ? "当前 action 仅返回兼容占位数据。"
                : availability.Reason.Trim();
            return availability.IsUnavailable ? $"字段未接入：{reason}" : $"兼容占位：{reason}";
        }

        return string.IsNullOrWhiteSpace(rawNote) ? "暂无说明" : rawNote.Trim();
    }

    private static StringNarrationBusinessDataAvailability ParseDataAvailability(JsonElement root)
    {
        if (!(TryGet(root, "dataAvailability", out var availability) && availability.ValueKind == JsonValueKind.Object))
        {
            return new StringNarrationBusinessDataAvailability();
        }

        return new StringNarrationBusinessDataAvailability
        {
            CashBalance = ParseDataAvailabilityItem(availability, "cashBalance"),
            Receivable = ParseDataAvailabilityItem(availability, "receivable"),
            Payable = ParseDataAvailabilityItem(availability, "payable"),
            InventorySource = ParseDataAvailabilityItem(availability, "inventorySource"),
            MaterialConsumption = ParseDataAvailabilityItem(availability, "materialConsumption")
        };
    }

    private static StringNarrationBusinessDataAvailabilityItem ParseDataAvailabilityItem(JsonElement root, string name)
    {
        if (!(TryGet(root, name, out var item) && item.ValueKind == JsonValueKind.Object))
        {
            return new StringNarrationBusinessDataAvailabilityItem();
        }

        return new StringNarrationBusinessDataAvailabilityItem
        {
            Status = ReadString(item, "status"),
            SourceType = ReadString(item, "sourceType"),
            Reason = ReadString(item, "reason")
        };
    }

    private static string ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGet(element, name, out var property))
            {
                continue;
            }

            var value = property.ValueKind switch
            {
                JsonValueKind.String => property.GetString(),
                JsonValueKind.Number => property.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => string.Empty
            };
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static decimal ReadDecimal(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGet(element, name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var number))
            {
                return number;
            }

            if (property.ValueKind == JsonValueKind.String && decimal.TryParse(property.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return 0;
    }

    private static int ReadInt(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGet(element, name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
            {
                return number;
            }

            if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return 0;
    }

    private static long ReadLong(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGet(element, name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var number))
            {
                return NormalizeTimestamp(number);
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                var value = property.GetString();
                if (long.TryParse(value, out var parsedLong))
                {
                    return NormalizeTimestamp(parsedLong);
                }

                if (DateTimeOffset.TryParse(value, out var parsedDate))
                {
                    return parsedDate.ToUnixTimeMilliseconds();
                }
            }
        }

        return 0;
    }

    private static bool ReadBool(JsonElement element, params string[] names)
    {
        if (names.Length == 0)
        {
            return element.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(element.GetString(), out var parsed) && parsed,
                JsonValueKind.Number => element.TryGetInt32(out var number) && number != 0,
                _ => false
            };
        }

        foreach (var name in names)
        {
            if (TryGet(element, name, out var property) && ReadBool(property))
            {
                return true;
            }
        }

        return false;
    }

    private static decimal? ReadNullableDecimal(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGet(element, name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var number))
            {
                return number;
            }

            if (property.ValueKind == JsonValueKind.String && decimal.TryParse(property.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static int? ReadNullableInt(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGet(element, name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
            {
                return number;
            }

            if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string name)
    {
        if (!TryGet(element, name, out var property))
        {
            return [];
        }

        if (property.ValueKind == JsonValueKind.Array)
        {
            return property.EnumerateArray()
                .Select(item => ReadStringValue(item))
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList();
        }

        var raw = ReadStringValue(property);
        return string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split([',', '，', '/', '、', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string ReadStringValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    private static long NormalizeTimestamp(long timestamp)
    {
        if (timestamp <= 0)
        {
            return 0;
        }

        return timestamp < 10_000_000_000 ? timestamp * 1000 : timestamp;
    }
}
