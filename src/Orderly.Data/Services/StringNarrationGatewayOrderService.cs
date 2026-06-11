using System.Text.Json;
using Orderly.Core.Models;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed partial class StringNarrationGatewayOrderService : IStringNarrationOrderService
{
    private const string OrderListAction = "orderList";
    private const string OrderDetailAction = "orderDetail";
    private const string UpdateFulfillmentAction = "updateFulfillment";
    private const string UpdateExceptionAction = "updateException";
    private const string FulfillmentStatsAction = "fulfillmentStats";
    private const string GenerateProductionOrderAction = "generateProductionOrder";
    private static readonly HashSet<string> ShippingSyncFailureStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "failed",
        "error",
        "exception",
        "sync_failed"
    };
    private static readonly HashSet<string> FulfillmentStatesRequiringProductionOrder = new(StringComparer.OrdinalIgnoreCase)
    {
        StringNarrationFulfillmentStatusCatalog.Making,
        StringNarrationFulfillmentStatusCatalog.ReadyToShip,
        StringNarrationFulfillmentStatusCatalog.Shipped,
        StringNarrationFulfillmentStatusCatalog.Exception
    };
    private static readonly HashSet<string> FulfillmentStatesRequiringWorkOrders = new(StringComparer.OrdinalIgnoreCase)
    {
        StringNarrationFulfillmentStatusCatalog.Making,
        StringNarrationFulfillmentStatusCatalog.ReadyToShip,
        StringNarrationFulfillmentStatusCatalog.Shipped,
        StringNarrationFulfillmentStatusCatalog.Exception
    };
    private static readonly HashSet<string> FulfillmentStatesRequiringReceiverContact = new(StringComparer.OrdinalIgnoreCase)
    {
        StringNarrationFulfillmentStatusCatalog.ReadyToShip,
        StringNarrationFulfillmentStatusCatalog.Shipped,
        StringNarrationFulfillmentStatusCatalog.Completed,
        StringNarrationFulfillmentStatusCatalog.Exception
    };
    private static readonly HashSet<string> FulfillmentStatesRequiringTrackingNo = new(StringComparer.OrdinalIgnoreCase)
    {
        StringNarrationFulfillmentStatusCatalog.Shipped,
        StringNarrationFulfillmentStatusCatalog.Completed,
        StringNarrationFulfillmentStatusCatalog.Exception
    };
    private readonly StringNarrationGatewayClient _client;

    public StringNarrationGatewayOrderService(StringNarrationGatewayClient client)
    {
        _client = client;
    }

    public async Task<StringNarrationWhoamiResult> WhoamiAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var root = await _client.InvokeAsync("whoami", new { }, cancellationToken);
            var payloadRoot = GetPayloadRoot(root);
            return new StringNarrationWhoamiResult
            {
                Authorized = ReadBool(payloadRoot, "authorized"),
                Gateway = ReadString(payloadRoot, "gateway"),
                OperatorId = ReadString(payloadRoot, "operatorId"),
                OperatorOpenid = ReadString(payloadRoot, "operatorOpenid"),
                Permissions = ReadStringArray(payloadRoot, "permissions")
            };
        }
        catch (InvalidOperationException ex)
        {
            throw WrapActionException("whoami", ex);
        }
    }

    public async Task<StringNarrationOrderListResult> GetOrdersAsync(StringNarrationOrderQuery query, CancellationToken cancellationToken = default)
    {
        try
        {
            query ??= new StringNarrationOrderQuery();
            var payload = BuildQueryPayload(query, includePageInfo: true);

            var root = await _client.InvokeAsync(OrderListAction, payload, cancellationToken);
            var payloadRoot = GetPayloadRoot(root);
            var orders = ParseSummaryList(payloadRoot);
            var stats = ParseFulfillmentStats(payloadRoot, requireWorkbenchDashboardContract: false);

            return new StringNarrationOrderListResult
            {
                Orders = orders,
                PageInfo = TryGet(payloadRoot, "pageInfo", out var pageInfo) ? ParsePageInfo(pageInfo) : new StringNarrationPageInfo(),
                Stats = stats
            };
        }
        catch (InvalidOperationException ex)
        {
            throw WrapActionException(OrderListAction, ex);
        }
    }

    public async Task<StringNarrationFulfillmentStats> GetFulfillmentStatsAsync(StringNarrationOrderQuery query, CancellationToken cancellationToken = default)
    {
        query ??= new StringNarrationOrderQuery();
        var payload = BuildQueryPayload(query, includePageInfo: false);

        try
        {
            var root = await _client.InvokeAsync(FulfillmentStatsAction, payload, cancellationToken);
            var payloadRoot = GetPayloadRoot(root);
            var stats = ParseFulfillmentStats(payloadRoot);
            if (stats.Metrics.Count > 0)
            {
                return stats;
            }

            var orders = ParseSummaryList(payloadRoot);
            return BuildStatsFromOrders(orders, ReadLong(payloadRoot, "statsAt", "calculatedAt", "updatedAt"));
        }
        catch (InvalidOperationException ex)
        {
            throw WrapActionException(FulfillmentStatsAction, ex);
        }
    }

    public async Task<StringNarrationOrderDetail> GetOrderDetailAsync(string orderNo, string tradeNo = "", string id = "", CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = BuildLookupPayload(orderNo, tradeNo, id);
            var root = await _client.InvokeAsync(OrderDetailAction, payload, cancellationToken);
            return ParseDetail(GetPayloadRoot(root));
        }
        catch (InvalidOperationException ex)
        {
            throw WrapActionException(OrderDetailAction, ex);
        }
    }

    public async Task<StringNarrationOrderDetail> UpdateFulfillmentAsync(StringNarrationFulfillmentUpdateRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);

            var payload = BuildLookupPayload(request.OrderNo, request.TradeNo, request.Id);
            AddIfPresent(payload, "fulfillmentStatus", request.FulfillmentStatus);
            payload["trackingNo"] = request.TrackingNo.Trim();
            payload["carrier"] = request.Carrier.Trim();
            payload["expressCompanyCode"] = request.ExpressCompanyCode.Trim();
            payload["shippingRemark"] = request.ShippingRemark.Trim();
            payload["adminRemark"] = request.AdminRemark.Trim();

            await _client.InvokeAsync(UpdateFulfillmentAction, payload, cancellationToken);
            return await GetOrderDetailAsync(request.OrderNo, request.TradeNo, request.Id, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            throw WrapActionException(UpdateFulfillmentAction, ex);
        }
    }

    public async Task<StringNarrationExceptionActionResult> ApplyExceptionActionAsync(StringNarrationExceptionActionRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);

            var payload = BuildLookupPayload(request.OrderNo, request.TradeNo, request.Id);
            AddIfPresent(payload, "action", request.NormalizedAction);
            AddIfPresent(payload, "resolutionStatus", request.TargetResolutionStatus);
            AddIfPresent(payload, "resolutionAction", request.ResolutionAction);
            AddIfPresent(payload, "adminResolutionRemark", request.AdminResolutionRemark, StringNarrationGatewayInputSafety.MaxRemarkCharacters);
            AddIfPresent(payload, "owner", request.Owner);
            AddIfPresent(payload, "assignee", request.Assignee);
            AddIfPresent(payload, "priority", request.Priority);
            AddIfPresent(payload, "resolvedBy", request.ResolvedBy);
            AddIfPresent(payload, "operatorId", request.OperatorId);
            AddIfPresent(payload, "operatorOpenid", request.OperatorOpenid);
            AddIfPositive(payload, "slaDueAt", request.SlaDueAt);
            AddIfPositive(payload, "lastCheckedAt", request.LastCheckedAt);
            AddIfPositive(payload, "actionAt", request.ActionAt);

            await _client.InvokeAsync(UpdateExceptionAction, payload, cancellationToken);
            var detail = await GetOrderDetailAsync(request.OrderNo, request.TradeNo, request.Id, cancellationToken);
            return new StringNarrationExceptionActionResult
            {
                Ok = true,
                Message = $"异常处理动作已提交：{request.NormalizedAction}",
                Detail = detail,
                AuditEntry = BuildAuditEntryFromRequest(request, detail.Exception)
            };
        }
        catch (InvalidOperationException ex)
        {
            throw WrapActionException(UpdateExceptionAction, ex);
        }
    }

    public Task<StringNarrationExceptionSampleReplayResult> ReplayExceptionSamplesAsync(StringNarrationExceptionSampleReplayRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var items = new List<StringNarrationExceptionSampleReplayItem>();
        foreach (var sample in request.Samples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.Add(ReplayExceptionSample(sample));
        }

        return Task.FromResult(new StringNarrationExceptionSampleReplayResult
        {
            Items = items
        });
    }

    public async Task<StringNarrationOrderDetail> GenerateProductionOrderAsync(StringNarrationGenerateProductionOrderRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);

            var payload = BuildLookupPayload(request.OrderNo, request.TradeNo, request.Id);
            payload["remark"] = request.Remark.Trim();
            payload["forceRegenerate"] = request.ForceRegenerate;

            await _client.InvokeAsync(GenerateProductionOrderAction, payload, cancellationToken);
            return await GetOrderDetailAsync(request.OrderNo, request.TradeNo, request.Id, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            throw WrapActionException(GenerateProductionOrderAction, ex);
        }
    }

    private static StringNarrationFulfillmentStats ParseFulfillmentStats(
        JsonElement root,
        bool requireWorkbenchDashboardContract = true)
    {
        var statsSource = GetFirstObject(root, "stats", "fulfillmentStats", "metrics");
        if (statsSource.ValueKind != JsonValueKind.Object)
        {
            statsSource = root;
        }

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var metricsArray = GetFirstArray(statsSource, "metrics", "items", "list");
        if (metricsArray.ValueKind != JsonValueKind.Array && statsSource.ValueKind == JsonValueKind.Array)
        {
            metricsArray = statsSource;
        }

        if (metricsArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var metric in metricsArray.EnumerateArray())
            {
                var status = ReadString(metric, "fulfillmentStatus", "status", "key", "name");
                if (string.IsNullOrWhiteSpace(status))
                {
                    continue;
                }

                counts[StringNarrationFulfillmentStatusCatalog.Normalize(status)] =
                    ReadInt(metric, "count", "value", "total");
            }
        }

        var byStatus = GetFirstObject(statsSource, "byStatus", "counts", "map");
        if (byStatus.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in byStatus.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var count))
                {
                    counts[StringNarrationFulfillmentStatusCatalog.Normalize(property.Name)] = count;
                    continue;
                }

                if (property.Value.ValueKind == JsonValueKind.String
                    && int.TryParse(property.Value.GetString(), out count))
                {
                    counts[StringNarrationFulfillmentStatusCatalog.Normalize(property.Name)] = count;
                }
            }
        }

        foreach (var definition in StringNarrationFulfillmentStatusCatalog.GetDefinitions())
        {
            if (TryGet(statsSource, definition.FulfillmentStatus, out var value))
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var count))
                {
                    counts[definition.FulfillmentStatus] = count;
                }
                else if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out count))
                {
                    counts[definition.FulfillmentStatus] = count;
                }
            }
        }

        if (counts.Count == 0)
        {
            return new StringNarrationFulfillmentStats();
        }

        var totalCount = ReadInt(statsSource, "total", "totalCount");
        var calculatedAt = ReadLong(statsSource, "calculatedAt", "at", "updatedAt");
        return BuildStatsFromCounts(
            counts,
            totalCount,
            calculatedAt,
            ParseWorkbenchDashboard(root, statsSource, counts, totalCount, calculatedAt, requireWorkbenchDashboardContract));
    }

    private static StringNarrationFulfillmentStats BuildStatsFromOrders(
        IReadOnlyList<StringNarrationOrderSummary> orders,
        long calculatedAt = 0)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
        {
            var status = StringNarrationFulfillmentStatusCatalog.Normalize(order.FulfillmentStatus);
            if (string.IsNullOrWhiteSpace(status))
            {
                continue;
            }

            counts.TryGetValue(status, out var current);
            counts[status] = current + 1;
        }

        return BuildStatsFromCounts(counts, counts.Values.Sum(), calculatedAt);
    }

    private static StringNarrationFulfillmentStats BuildStatsFromCounts(
        IReadOnlyDictionary<string, int> counts,
        int totalCount,
        long calculatedAt,
        StringNarrationWorkbenchDashboardStats? dashboard = null)
    {
        var metrics = new List<StringNarrationFulfillmentStatusMetric>();
        foreach (var definition in StringNarrationFulfillmentStatusCatalog.GetDefinitions().OrderBy(item => item.SortOrder))
        {
            counts.TryGetValue(definition.FulfillmentStatus, out var count);
            metrics.Add(new StringNarrationFulfillmentStatusMetric
            {
                FulfillmentStatus = definition.FulfillmentStatus,
                Label = definition.Label,
                SortOrder = definition.SortOrder,
                Count = count,
                IsTerminal = definition.IsTerminal,
                IsException = definition.IsException,
                IsUnknown = definition.IsUnknown
            });
        }

        var unknownStatuses = counts.Keys
            .Where(status => StringNarrationFulfillmentStatusCatalog.Resolve(status).IsUnknown)
            .OrderBy(status => status, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var nextSort = metrics.Count == 0 ? 1 : metrics.Max(item => item.SortOrder) + 1;
        foreach (var status in unknownStatuses)
        {
            counts.TryGetValue(status, out var count);
            var definition = StringNarrationFulfillmentStatusCatalog.Resolve(status);
            metrics.Add(new StringNarrationFulfillmentStatusMetric
            {
                FulfillmentStatus = status,
                Label = definition.Label,
                SortOrder = nextSort++,
                Count = count,
                IsTerminal = definition.IsTerminal,
                IsException = definition.IsException,
                IsUnknown = true
            });
        }

        var normalizedTotal = totalCount > 0 ? totalCount : metrics.Sum(item => item.Count);
        return new StringNarrationFulfillmentStats
        {
            Metrics = metrics,
            TotalCount = normalizedTotal,
            CalculatedAt = calculatedAt,
            WorkbenchDashboard = dashboard ?? new StringNarrationWorkbenchDashboardStats()
        };
    }

    private static StringNarrationWorkbenchDashboardStats ParseWorkbenchDashboard(
        JsonElement root,
        JsonElement statsSource,
        IReadOnlyDictionary<string, int> counts,
        int totalCount,
        long calculatedAt,
        bool requireWorkbenchDashboardContract)
    {
        var dashboardSource = GetFirstObject(root, "workbenchDashboard", "dashboard", "businessDashboard", "summary");
        if (dashboardSource.ValueKind != JsonValueKind.Object)
        {
            dashboardSource = GetFirstObject(statsSource, "workbenchDashboard", "dashboard", "businessDashboard", "summary");
        }

        if (requireWorkbenchDashboardContract)
        {
            ValidateWorkbenchDashboardContract(dashboardSource);
        }

        var sources = dashboardSource.ValueKind == JsonValueKind.Object
            ? new[] { dashboardSource, statsSource, root }
            : new[] { statsSource, root };

        counts.TryGetValue(StringNarrationFulfillmentStatusCatalog.PendingMake, out var pendingMakeCount);
        counts.TryGetValue(StringNarrationFulfillmentStatusCatalog.Making, out var makingCount);
        counts.TryGetValue(StringNarrationFulfillmentStatusCatalog.ReadyToShip, out var readyToShipCount);
        counts.TryGetValue(StringNarrationFulfillmentStatusCatalog.Exception, out var exceptionCount);

        var derivedUnfinishedOrderCount = CountFromMap(counts,
            StringNarrationFulfillmentStatusCatalog.PaidPendingConfirm,
            StringNarrationFulfillmentStatusCatalog.PendingMake,
            StringNarrationFulfillmentStatusCatalog.Making,
            StringNarrationFulfillmentStatusCatalog.ReadyToShip,
            StringNarrationFulfillmentStatusCatalog.Exception);

        var unfinishedOrderCount = ReadIntFromCandidates(sources, "unfinishedOrderCount", "unfinishedCount", "openOrderCount");
        if (unfinishedOrderCount <= 0)
        {
            unfinishedOrderCount = derivedUnfinishedOrderCount;
        }

        var lastSyncedAt = ReadLongFromCandidates(sources, "lastSyncedAt", "lastSyncAt", "calculatedAt", "at", "updatedAt");
        if (lastSyncedAt <= 0)
        {
            lastSyncedAt = calculatedAt;
        }

        var dashboard = new StringNarrationWorkbenchDashboardStats
        {
            TodayOrderCount = ReadIntFromCandidates(sources, "todayOrderCount", "todayOrders", "orderCountToday"),
            TodayOrderCountDelta = ReadIntFromCandidates(sources, "todayOrderCountDelta", "todayOrdersDelta", "orderCountDelta"),
            TodayRevenueAmount = ReadDecimalFromCandidates(sources, "todayRevenueAmount", "todayRevenue", "todayAmount", "revenueToday"),
            TodayRevenueAmountDelta = ReadDecimalFromCandidates(sources, "todayRevenueAmountDelta", "todayRevenueDelta", "revenueDelta"),
            PendingMakeCount = pendingMakeCount,
            PendingMakeDelta = ReadIntFromCandidates(sources, "pendingMakeDelta", "pendingProductionDelta"),
            MakingCount = makingCount,
            MakingDelta = ReadIntFromCandidates(sources, "makingDelta", "inProductionDelta"),
            ReadyToShipCount = readyToShipCount,
            ReadyToShipDelta = ReadIntFromCandidates(sources, "readyToShipDelta", "pendingShipDelta"),
            ExceptionOrderCount = exceptionCount,
            ExceptionOrderDelta = ReadIntFromCandidates(sources, "exceptionOrderDelta", "exceptionDelta", "abnormalOrderDelta"),
            UnfinishedOrderCount = unfinishedOrderCount,
            InventoryHealthStatus = ReadStringFromCandidates(sources, "inventoryHealthStatus", "inventoryStatus", "stockHealthStatus"),
            InventoryHealthSummary = ReadStringFromCandidates(sources, "inventoryHealthSummary", "inventorySummary", "stockHealthSummary"),
            InventoryWarningCount = ReadIntFromCandidates(sources, "inventoryWarningCount", "stockWarningCount", "inventoryAlertCount"),
            CashFlowScore = ReadIntFromCandidates(sources, "cashFlowScore", "cashflowScore"),
            CashFlowStatus = ReadStringFromCandidates(sources, "cashFlowStatus", "cashflowStatus", "cashFlowSummary"),
            CashFlowDelta = ReadIntFromCandidates(sources, "cashFlowDelta", "cashflowDelta"),
            LastSyncedAt = lastSyncedAt,
            RecentBusinessTrendItems = ParseBusinessTrendItems(sources),
            FulfillmentPressureItems = ParseFulfillmentPressureItems(sources, counts, unfinishedOrderCount, totalCount)
        };

        return dashboard;
    }

    private static void ValidateWorkbenchDashboardContract(JsonElement dashboardSource)
    {
        if (dashboardSource.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("fulfillmentStats 返回缺少 workbenchDashboard 对象字段。");
        }

        RequireProperties(dashboardSource, "workbenchDashboard",
            "todayOrderCount",
            "todayOrderCountDelta",
            "todayRevenueAmount",
            "todayRevenueAmountDelta",
            "pendingMakeCount",
            "pendingMakeDelta",
            "readyToShipCount",
            "readyToShipDelta",
            "exceptionOrderCount",
            "exceptionOrderDelta",
            "unfinishedOrderCount",
            "lastSyncedAt",
            "recentBusinessTrendItems",
            "fulfillmentPressureItems",
            "inventoryHealthStatus",
            "inventoryHealthSummary",
            "inventoryWarningCount",
            "cashFlowScore",
            "cashFlowStatus",
            "cashFlowDelta");

        RequireArray(dashboardSource, "recentBusinessTrendItems", "workbenchDashboard");
        RequireArray(dashboardSource, "fulfillmentPressureItems", "workbenchDashboard");
    }

}
