using System.Text.Json;
using Orderly.Core.Models;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class StringNarrationGatewayOrderService : IStringNarrationOrderService
{
    private const string OrderListAction = "orderList";
    private const string OrderDetailAction = "orderDetail";
    private const string UpdateFulfillmentAction = "updateFulfillment";
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
    private static readonly HashSet<string> ResolvedExceptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "resolved",
        "closed",
        "fixed",
        "done",
        "cleared"
    };

    private readonly StringNarrationGatewayClient _client;

    public StringNarrationGatewayOrderService(StringNarrationGatewayClient client)
    {
        _client = client;
    }

    public async Task<StringNarrationWhoamiResult> WhoamiAsync(CancellationToken cancellationToken = default)
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

    public async Task<StringNarrationOrderListResult> GetOrdersAsync(StringNarrationOrderQuery query, CancellationToken cancellationToken = default)
    {
        query ??= new StringNarrationOrderQuery();
        var payload = BuildQueryPayload(query, includePageInfo: true);

        var root = await _client.InvokeAsync(OrderListAction, payload, cancellationToken);
        var payloadRoot = GetPayloadRoot(root);
        var orders = ParseSummaryList(payloadRoot);
        var stats = ParseFulfillmentStats(payloadRoot);

        return new StringNarrationOrderListResult
        {
            Orders = orders,
            PageInfo = TryGet(payloadRoot, "pageInfo", out var pageInfo) ? ParsePageInfo(pageInfo) : new StringNarrationPageInfo(),
            Stats = stats
        };
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
            throw new InvalidOperationException($"调用串述 adminPcGateway action={FulfillmentStatsAction} 失败：{ex.Message}", ex);
        }
    }

    public async Task<StringNarrationOrderDetail> GetOrderDetailAsync(string orderNo, string tradeNo = "", string id = "", CancellationToken cancellationToken = default)
    {
        var payload = BuildLookupPayload(orderNo, tradeNo, id);
        var root = await _client.InvokeAsync(OrderDetailAction, payload, cancellationToken);
        return ParseDetail(GetPayloadRoot(root));
    }

    public async Task<StringNarrationOrderDetail> UpdateFulfillmentAsync(StringNarrationFulfillmentUpdateRequest request, CancellationToken cancellationToken = default)
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

    public async Task<StringNarrationOrderDetail> GenerateProductionOrderAsync(StringNarrationGenerateProductionOrderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = BuildLookupPayload(request.OrderNo, request.TradeNo, request.Id);
        payload["remark"] = request.Remark.Trim();
        payload["forceRegenerate"] = request.ForceRegenerate;

        try
        {
            await _client.InvokeAsync(GenerateProductionOrderAction, payload, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException($"调用串述 adminPcGateway action={GenerateProductionOrderAction} 失败：{ex.Message}", ex);
        }

        return await GetOrderDetailAsync(request.OrderNo, request.TradeNo, request.Id, cancellationToken);
    }

    private static Dictionary<string, object?> BuildLookupPayload(string orderNo, string tradeNo, string id)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(id))
        {
            payload["_id"] = id.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(orderNo))
        {
            payload["orderNo"] = orderNo.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(tradeNo))
        {
            payload["tradeNo"] = tradeNo.Trim();
        }
        else
        {
            throw new InvalidOperationException("查询串述订单详情必须提供 orderNo、tradeNo 或 _id。");
        }

        return payload;
    }

    private static Dictionary<string, object?> BuildQueryPayload(StringNarrationOrderQuery query, bool includePageInfo)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (includePageInfo)
        {
            payload["page"] = query.Page <= 0 ? 1 : query.Page;
            payload["pageSize"] = query.PageSize <= 0 ? 20 : query.PageSize;
        }

        AddIfPresent(payload, "keyword", query.Keyword);
        AddIfPresent(payload, "status", query.Status);
        AddIfPresent(payload, "fulfillmentStatus", query.FulfillmentStatus);
        AddIfPositive(payload, "startAt", query.StartAt);
        AddIfPositive(payload, "endAt", query.EndAt);
        return payload;
    }

    private static void AddIfPositive(Dictionary<string, object?> payload, string key, long value)
    {
        if (value > 0)
        {
            payload[key] = value;
        }
    }

    private static void AddIfPresent(Dictionary<string, object?> payload, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            payload[key] = value.Trim();
        }
    }

    private static IReadOnlyList<StringNarrationOrderSummary> ParseSummaryList(JsonElement root)
    {
        var listElement = GetFirstArray(root, "list", "orders");
        if (listElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var orders = new List<StringNarrationOrderSummary>();
        foreach (var item in listElement.EnumerateArray())
        {
            orders.Add(ParseSummary(item));
        }

        return orders;
    }

    private static StringNarrationOrderSummary ParseSummary(JsonElement element)
    {
        var fulfillmentElement = GetObjectOrEmpty(element, "fulfillment");
        var customerElement = GetObjectOrEmpty(element, "customer");
        var shippingElement = GetObjectOrFallback(element, "shipping", fulfillmentElement);
        var paymentElement = GetObjectOrEmpty(element, "payment");
        var productionElement = GetFirstObject(
            element,
            "productionOrder",
            "production",
            "workOrderSnapshot",
            "productionSnapshot");

        var fulfillmentStatus = ReadStringWithFallback(element, fulfillmentElement, "fulfillmentStatus");
        var normalizedFulfillmentStatus = StringNarrationFulfillmentStatusCatalog.Normalize(fulfillmentStatus);
        var wxShippingSyncStatus = ReadStringWithFallback(shippingElement, fulfillmentElement, element, "wxShippingSyncStatus");
        var trackingNo = ReadStringWithFallback(shippingElement, fulfillmentElement, element, "trackingNo");
        var address = TryGet(element, "address", out var addressElement)
            ? ParseAddress(addressElement)
            : TryGet(customerElement, "address", out var customerAddress)
                ? ParseAddress(customerAddress)
                : new StringNarrationAddressSnapshot();
        var workOrders = ParseWorkOrders(element, productionElement, fulfillmentElement);
        var productionOrder = ParseProductionOrder(productionElement, workOrders);
        var exceptionSnapshot = ParseExceptionSnapshot(
            element,
            address,
            normalizedFulfillmentStatus,
            wxShippingSyncStatus,
            trackingNo,
            productionOrder,
            workOrders,
            fulfillmentElement,
            shippingElement,
            paymentElement,
            productionElement);

        return new StringNarrationOrderSummary
        {
            Id = ReadString(element, "_id", "id"),
            OrderNo = ReadString(element, "orderNo"),
            WxOutTradeNo = ReadString(element, "wxOutTradeNo", "tradeNo"),
            WxTransactionId = ReadString(element, "wxTransactionId"),
            OwnerOpenid = ReadStringWithFallback(element, customerElement, "ownerOpenid"),
            Status = ReadString(element, "status", "orderStatus"),
            FulfillmentStatus = normalizedFulfillmentStatus,
            WxShippingSyncStatus = wxShippingSyncStatus,
            Amount = ReadDecimal(element, "amount"),
            ItemCount = ReadInt(element, "itemCount"),
            CreatedAt = ReadLong(element, "createdAt"),
            PaidAt = ReadLong(element, "paidAt"),
            TitleSnapshot = ReadString(element, "titleSnapshot"),
            CoverSnapshot = ReadString(element, "coverSnapshot"),
            Address = address,
            GiftConfig = CloneElement(element, "giftConfig"),
            TrackingNo = trackingNo,
            Carrier = ReadStringWithFallback(shippingElement, fulfillmentElement, element, "carrier"),
            ExpressCompanyCode = ReadStringWithFallback(shippingElement, fulfillmentElement, element, "expressCompanyCode"),
            ShippingRemark = ReadStringWithFallback(shippingElement, fulfillmentElement, element, "shippingRemark"),
            AdminRemark = ReadStringWithFallback(element, fulfillmentElement, "adminRemark"),
            ShippedAt = ReadLongWithFallback(shippingElement, fulfillmentElement, element, "shippedAt"),
            CompletedAt = ReadLongWithFallback(shippingElement, fulfillmentElement, element, "completedAt"),
            FulfillmentUpdatedAt = ReadLongWithFallback(element, fulfillmentElement, "fulfillmentUpdatedAt"),
            Exception = exceptionSnapshot,
            HasException = exceptionSnapshot.HasException
        };
    }

    private static StringNarrationOrderDetail ParseDetail(JsonElement root)
    {
        var baseElement = GetObjectOrFallback(root, "base", root);
        var paymentElement = GetObjectOrFallback(root, "payment", root);
        var customerElement = GetObjectOrFallback(root, "customer", root);
        var productElement = GetObjectOrFallback(root, "product", root);
        var fulfillmentElement = GetObjectOrFallback(root, "fulfillment", root);
        var shippingElement = GetObjectOrFallback(root, "shipping", fulfillmentElement);
        var productionElement = GetFirstObject(
            root,
            "productionOrder",
            "production",
            "workOrderSnapshot",
            "productionSnapshot");

        if (productionElement.ValueKind != JsonValueKind.Object)
        {
            productionElement = GetFirstObject(
                fulfillmentElement,
                "productionOrder",
                "production",
                "workOrderSnapshot",
                "productionSnapshot");
        }

        var workOrders = ParseWorkOrders(root, productionElement, fulfillmentElement);
        var address = TryGet(customerElement, "address", out var addressElement)
            ? ParseAddress(addressElement)
            : TryGet(root, "address", out var rootAddress)
                ? ParseAddress(rootAddress)
                : new StringNarrationAddressSnapshot();
        var detail = new StringNarrationOrderDetail
        {
            Id = ReadStringWithFallback(baseElement, root, "_id", "id"),
            OrderNo = ReadStringWithFallback(baseElement, root, "orderNo"),
            WxOutTradeNo = ReadStringWithFallback(paymentElement, root, "wxOutTradeNo", "tradeNo"),
            WxTransactionId = ReadStringWithFallback(paymentElement, root, "wxTransactionId"),
            OwnerOpenid = ReadStringWithFallback(customerElement, root, "ownerOpenid"),
            Status = ReadStringWithFallback(baseElement, root, "status", "orderStatus"),
            FulfillmentStatus = StringNarrationFulfillmentStatusCatalog.Normalize(ReadStringWithFallback(fulfillmentElement, root, "fulfillmentStatus")),
            WxShippingSyncStatus = ReadStringWithFallback(shippingElement, fulfillmentElement, root, "wxShippingSyncStatus"),
            Amount = ReadDecimalWithFallback(baseElement, root, "amount"),
            ItemCount = ReadIntWithFallback(baseElement, root, "itemCount"),
            CreatedAt = ReadLongWithFallback(baseElement, root, "createdAt"),
            PaidAt = ReadLongWithFallback(paymentElement, root, "paidAt"),
            TitleSnapshot = ReadStringWithFallback(productElement, root, "titleSnapshot"),
            CoverSnapshot = ReadStringWithFallback(productElement, root, "coverSnapshot"),
            ItemsSnapshot = ParseItems(productElement, root),
            PricingSnapshot = CloneElement(productElement, "pricingSnapshot") ?? CloneElement(root, "pricingSnapshot"),
            DesignSnapshot = CloneElement(productElement, "designSnapshot") ?? CloneElement(root, "designSnapshot"),
            StorySnapshot = CloneElement(productElement, "storySnapshot") ?? CloneElement(root, "storySnapshot"),
            Address = address,
            Remark = ReadStringWithFallback(customerElement, root, "remark"),
            GiftConfig = CloneElement(root, "giftConfig") ?? CloneElement(productElement, "giftConfig"),
            TrackingNo = ReadStringWithFallback(shippingElement, fulfillmentElement, root, "trackingNo"),
            Carrier = ReadStringWithFallback(shippingElement, fulfillmentElement, root, "carrier"),
            ExpressCompanyCode = ReadStringWithFallback(shippingElement, fulfillmentElement, root, "expressCompanyCode"),
            ShippingRemark = ReadStringWithFallback(shippingElement, fulfillmentElement, root, "shippingRemark"),
            AdminRemark = ReadStringWithFallback(fulfillmentElement, root, "adminRemark"),
            ShippedAt = ReadLongWithFallback(shippingElement, fulfillmentElement, root, "shippedAt"),
            CompletedAt = ReadLongWithFallback(shippingElement, fulfillmentElement, root, "completedAt"),
            FulfillmentUpdatedAt = ReadLongWithFallback(fulfillmentElement, root, "fulfillmentUpdatedAt"),
            ProductionOrder = ParseProductionOrder(productionElement, workOrders),
            WorkOrders = workOrders,
            StatusLogs = ParseStatusLogs(root)
        };

        var exceptionSnapshot = ParseExceptionSnapshot(
            root,
            detail.Address,
            detail.FulfillmentStatus,
            detail.WxShippingSyncStatus,
            detail.TrackingNo,
            detail.ProductionOrder,
            detail.WorkOrders,
            fulfillmentElement,
            shippingElement,
            paymentElement,
            productionElement);
        detail.Exception = exceptionSnapshot;
        detail.HasException = exceptionSnapshot.HasException;
        return detail;
    }

    private static StringNarrationProductionOrderSnapshot ParseProductionOrder(
        JsonElement productionElement,
        IReadOnlyList<StringNarrationWorkOrderSnapshot> workOrders)
    {
        return new StringNarrationProductionOrderSnapshot
        {
            ProductionOrderId = ReadString(productionElement, "_id", "id", "productionOrderId"),
            ProductionOrderNo = ReadString(productionElement, "productionOrderNo", "workOrderNo", "orderNo"),
            Status = ReadString(productionElement, "status", "productionStatus"),
            Source = ReadString(productionElement, "source", "createdBy"),
            Remark = ReadString(productionElement, "remark", "notes"),
            CreatedAt = ReadLong(productionElement, "createdAt"),
            UpdatedAt = ReadLong(productionElement, "updatedAt"),
            WorkOrders = workOrders,
            Raw = productionElement.ValueKind == JsonValueKind.Object ? productionElement.Clone() : null
        };
    }

    private static IReadOnlyList<StringNarrationWorkOrderSnapshot> ParseWorkOrders(
        JsonElement root,
        JsonElement productionElement,
        JsonElement fulfillmentElement)
    {
        var workOrdersElement = GetFirstArray(
            root,
            "workOrders",
            "workOrderList",
            "productionWorkOrders",
            "workOrdersSnapshot");

        if (workOrdersElement.ValueKind != JsonValueKind.Array)
        {
            workOrdersElement = GetFirstArray(
                productionElement,
                "workOrders",
                "items",
                "workOrderList",
                "workOrdersSnapshot");
        }

        if (workOrdersElement.ValueKind != JsonValueKind.Array)
        {
            workOrdersElement = GetFirstArray(fulfillmentElement, "workOrders", "workOrderList");
        }

        if (workOrdersElement.ValueKind != JsonValueKind.Array)
        {
            var singleWorkOrder = GetFirstObject(root, "workOrder", "workOrderSnapshot");
            if (singleWorkOrder.ValueKind != JsonValueKind.Object)
            {
                singleWorkOrder = GetFirstObject(productionElement, "workOrder", "latestWorkOrder");
            }

            if (singleWorkOrder.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            return [ParseWorkOrder(singleWorkOrder)];
        }

        var snapshots = new List<StringNarrationWorkOrderSnapshot>();
        foreach (var item in workOrdersElement.EnumerateArray())
        {
            snapshots.Add(ParseWorkOrder(item));
        }

        return snapshots;
    }

    private static StringNarrationWorkOrderSnapshot ParseWorkOrder(JsonElement element)
    {
        return new StringNarrationWorkOrderSnapshot
        {
            WorkOrderId = ReadString(element, "_id", "id", "workOrderId"),
            WorkOrderNo = ReadString(element, "workOrderNo", "orderNo"),
            ProductionOrderNo = ReadString(element, "productionOrderNo"),
            Status = ReadString(element, "status"),
            Assignee = ReadString(element, "assignee", "operatorId", "operatorOpenid"),
            Remark = ReadString(element, "remark", "notes"),
            CreatedAt = ReadLong(element, "createdAt"),
            UpdatedAt = ReadLong(element, "updatedAt"),
            Raw = element.Clone()
        };
    }

    private static StringNarrationExceptionSnapshot ParseExceptionSnapshot(
        JsonElement root,
        StringNarrationAddressSnapshot address,
        string fulfillmentStatus,
        string wxShippingSyncStatus,
        string trackingNo,
        StringNarrationProductionOrderSnapshot productionOrder,
        IReadOnlyList<StringNarrationWorkOrderSnapshot> workOrders,
        JsonElement fulfillmentElement,
        JsonElement shippingElement,
        JsonElement paymentElement,
        JsonElement productionElement)
    {
        var primaryExceptionElement = GetFirstObjectFromProperty(root, "exception", "exceptions", "risk", "alert", "alertInfo");
        var candidates = new List<JsonElement>();
        if (primaryExceptionElement.ValueKind == JsonValueKind.Object)
        {
            candidates.Add(primaryExceptionElement);
        }

        if (fulfillmentElement.ValueKind == JsonValueKind.Object)
        {
            candidates.Add(fulfillmentElement);
        }

        if (shippingElement.ValueKind == JsonValueKind.Object)
        {
            candidates.Add(shippingElement);
        }

        if (paymentElement.ValueKind == JsonValueKind.Object)
        {
            candidates.Add(paymentElement);
        }

        if (productionElement.ValueKind == JsonValueKind.Object)
        {
            candidates.Add(productionElement);
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            candidates.Add(root);
        }

        var type = ReadString(primaryExceptionElement, "type", "exceptionType", "riskType", "alertType");
        if (string.IsNullOrWhiteSpace(type))
        {
            type = ReadStringFromCandidates(candidates, "exceptionType", "riskType", "alertType");
        }

        var code = ReadString(primaryExceptionElement, "code", "exceptionCode", "riskCode", "alertCode");
        if (string.IsNullOrWhiteSpace(code))
        {
            code = ReadStringFromCandidates(candidates, "exceptionCode", "riskCode", "alertCode");
        }

        var level = ReadString(primaryExceptionElement, "level", "severity", "exceptionLevel", "riskLevel", "alertLevel");
        if (string.IsNullOrWhiteSpace(level))
        {
            level = ReadStringFromCandidates(candidates, "exceptionLevel", "riskLevel", "alertLevel", "severity");
        }

        var status = ReadString(primaryExceptionElement, "status", "exceptionStatus", "riskStatus", "alertStatus");
        if (string.IsNullOrWhiteSpace(status))
        {
            status = ReadStringFromCandidates(candidates, "exceptionStatus", "riskStatus", "alertStatus");
        }

        var source = ReadString(primaryExceptionElement, "source", "module", "from", "system", "exceptionSource", "riskSource", "alertSource");
        if (string.IsNullOrWhiteSpace(source))
        {
            source = ReadStringFromCandidates(candidates, "exceptionSource", "riskSource", "alertSource");
        }

        var reason = ReadString(primaryExceptionElement, "reason", "message", "description", "detail", "exceptionReason", "riskReason", "alertReason");
        if (string.IsNullOrWhiteSpace(reason))
        {
            reason = ReadStringFromCandidates(candidates, "exceptionReason", "riskReason", "alertReason");
        }

        var suggestedAction = ReadString(primaryExceptionElement, "suggestedAction", "action", "suggestion", "nextAction", "advice");
        if (string.IsNullOrWhiteSpace(suggestedAction))
        {
            suggestedAction = ReadStringFromCandidates(candidates, "exceptionSuggestedAction", "riskSuggestedAction", "alertSuggestedAction", "suggestedAction");
        }

        var adminResolutionRemark = ReadString(primaryExceptionElement, "adminResolutionRemark", "resolutionRemark", "resolution", "resolvedRemark", "remark");
        if (string.IsNullOrWhiteSpace(adminResolutionRemark))
        {
            adminResolutionRemark = ReadStringFromCandidates(candidates, "adminResolutionRemark", "resolutionRemark", "resolvedRemark");
        }

        var detectedAt = ReadLong(primaryExceptionElement, "detectedAt", "occurredAt", "raisedAt", "createdAt");
        if (detectedAt <= 0)
        {
            detectedAt = ReadLongFromCandidates(candidates, "exceptionDetectedAt", "riskDetectedAt", "alertDetectedAt", "detectedAt", "occurredAt", "raisedAt");
        }

        var resolvedAt = ReadLong(primaryExceptionElement, "resolvedAt", "closedAt", "resolvedTime");
        if (resolvedAt <= 0)
        {
            resolvedAt = ReadLongFromCandidates(candidates, "exceptionResolvedAt", "riskResolvedAt", "alertResolvedAt", "resolvedAt", "closedAt", "resolvedTime");
        }

        var tags = ReadStringArray(primaryExceptionElement, "tags");
        if (tags.Count == 0)
        {
            tags = ReadStringArray(primaryExceptionElement, "labels");
        }

        if (tags.Count == 0)
        {
            tags = ReadStringArrayFromCandidates(candidates, "exceptionTags", "riskTags", "alertTags", "tags", "labels", "tagList");
        }

        if (tags.Count == 0)
        {
            var singleTag = ReadString(primaryExceptionElement, "tag");
            if (string.IsNullOrWhiteSpace(singleTag))
            {
                singleTag = ReadStringFromCandidates(candidates, "exceptionTag", "riskTag", "alertTag", "tag");
            }

            if (!string.IsNullOrWhiteSpace(singleTag))
            {
                tags = new[] { singleTag };
            }
        }

        var normalizedFulfillmentStatus = StringNarrationFulfillmentStatusCatalog.Normalize(fulfillmentStatus);
        var normalizedShippingSyncStatus = NormalizeValue(wxShippingSyncStatus);
        var requiresReceiverContact = FulfillmentStatesRequiringReceiverContact.Contains(normalizedFulfillmentStatus);
        var requiresTrackingNo = FulfillmentStatesRequiringTrackingNo.Contains(normalizedFulfillmentStatus);
        var hasMissingAddress = requiresReceiverContact && !HasAddressCoreData(address);
        var hasMissingReceiverPhone = requiresReceiverContact && string.IsNullOrWhiteSpace(address.ReceiverPhone);
        var hasMissingTrackingNo = requiresTrackingNo && string.IsNullOrWhiteSpace(trackingNo);
        var hasShippingSyncFailure = ShippingSyncFailureStates.Contains(normalizedShippingSyncStatus);
        var requiresProductionOrder = FulfillmentStatesRequiringProductionOrder.Contains(normalizedFulfillmentStatus);
        var requiresWorkOrders = FulfillmentStatesRequiringWorkOrders.Contains(normalizedFulfillmentStatus);
        var hasProductionOrderMissing = requiresProductionOrder && !productionOrder.HasData;
        var hasWorkOrderMissing = requiresWorkOrders && workOrders.Count == 0;

        var explicitHasException = ReadBoolFromCandidates(candidates, "hasException", "isException");
        var explicitRequiresManualReview = ReadBoolFromCandidates(candidates, "requiresManualReview", "needManualReview", "manualReviewRequired");
        var explicitIsResolved = ReadBoolFromCandidates(candidates, "isResolved", "resolved");
        var normalizedStatus = NormalizeValue(status);
        var isResolved = explicitIsResolved || resolvedAt > 0 || ResolvedExceptionStates.Contains(normalizedStatus);
        var hasExceptionSignal = explicitHasException
            || !string.IsNullOrWhiteSpace(type)
            || !string.IsNullOrWhiteSpace(code)
            || !string.IsNullOrWhiteSpace(level)
            || !string.IsNullOrWhiteSpace(reason)
            || primaryExceptionElement.ValueKind == JsonValueKind.Object
            || hasMissingAddress
            || hasMissingReceiverPhone
            || hasMissingTrackingNo
            || hasShippingSyncFailure
            || hasProductionOrderMissing
            || hasWorkOrderMissing
            || string.Equals(normalizedFulfillmentStatus, StringNarrationFulfillmentStatusCatalog.Exception, StringComparison.OrdinalIgnoreCase);
        var requiresManualReview = explicitRequiresManualReview
            || string.Equals(normalizedFulfillmentStatus, StringNarrationFulfillmentStatusCatalog.Exception, StringComparison.OrdinalIgnoreCase)
            || hasMissingAddress
            || hasMissingReceiverPhone
            || hasMissingTrackingNo
            || hasShippingSyncFailure
            || hasProductionOrderMissing
            || hasWorkOrderMissing;
        if (hasExceptionSignal && !isResolved)
        {
            requiresManualReview = true;
        }

        return new StringNarrationExceptionSnapshot
        {
            Type = type,
            Code = code,
            Level = level,
            Status = status,
            Source = source,
            Reason = reason,
            SuggestedAction = suggestedAction,
            AdminResolutionRemark = adminResolutionRemark,
            DetectedAt = detectedAt,
            ResolvedAt = resolvedAt,
            HasException = hasExceptionSignal,
            RequiresManualReview = requiresManualReview,
            IsResolved = isResolved,
            HasMissingAddress = hasMissingAddress,
            HasMissingReceiverPhone = hasMissingReceiverPhone,
            HasMissingTrackingNo = hasMissingTrackingNo,
            HasShippingSyncFailure = hasShippingSyncFailure,
            HasProductionOrderMissing = hasProductionOrderMissing,
            HasWorkOrderMissing = hasWorkOrderMissing,
            Tags = tags,
            Raw = primaryExceptionElement.ValueKind == JsonValueKind.Object ? primaryExceptionElement.Clone() : null
        };
    }

    private static StringNarrationFulfillmentStats ParseFulfillmentStats(JsonElement root)
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

        return BuildStatsFromCounts(
            counts,
            ReadInt(statsSource, "total", "totalCount"),
            ReadLong(statsSource, "calculatedAt", "at", "updatedAt"));
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
        long calculatedAt)
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
            CalculatedAt = calculatedAt
        };
    }

    private static StringNarrationPageInfo ParsePageInfo(JsonElement element)
    {
        return new StringNarrationPageInfo
        {
            PageSize = ReadInt(element, "pageSize"),
            HasMore = ReadBool(element, "hasMore"),
            NextCursor = ReadString(element, "nextCursor"),
            Total = ReadInt(element, "total")
        };
    }

    private static StringNarrationAddressSnapshot ParseAddress(JsonElement element)
    {
        return new StringNarrationAddressSnapshot
        {
            ReceiverName = ReadString(element, "receiverName", "userName", "name"),
            ReceiverPhone = ReadString(element, "receiverPhone", "telNumber", "phone", "mobile"),
            Region = ReadString(element, "region"),
            Detail = ReadString(element, "detail", "detailInfo", "addressDetail"),
            FullAddress = ReadString(element, "fullAddress", "addressSummary"),
            AddressSummary = ReadString(element, "addressSummary", "fullAddress")
        };
    }

    private static IReadOnlyList<StringNarrationOrderItemSnapshot> ParseItems(JsonElement productElement, JsonElement root)
    {
        var itemsElement = GetFirstArray(productElement, "itemsSnapshot", "items");
        if (itemsElement.ValueKind != JsonValueKind.Array)
        {
            itemsElement = GetFirstArray(root, "itemsSnapshot", "items");
        }

        if (itemsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var items = new List<StringNarrationOrderItemSnapshot>();
        foreach (var item in itemsElement.EnumerateArray())
        {
            items.Add(new StringNarrationOrderItemSnapshot
            {
                DesignId = ReadString(item, "designId", "id"),
                Title = ReadString(item, "titleSnapshot", "designTitle", "title"),
                Cover = ReadString(item, "coverSnapshot", "cover", "image", "thumb", "coverUrl", "imageUrl", "thumbnail"),
                Count = ReadInt(item, "count", "quantity"),
                Raw = item.Clone()
            });
        }

        return items;
    }

    private static IReadOnlyList<StringNarrationStatusLog> ParseStatusLogs(JsonElement root)
    {
        var logsElement = GetFirstArray(root, "logs", "statusLogs", "timeline", "history");
        if (logsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var logs = new List<StringNarrationStatusLog>();
        foreach (var log in logsElement.EnumerateArray())
        {
            logs.Add(new StringNarrationStatusLog
            {
                Type = ReadString(log, "type", "eventType"),
                At = ReadLong(log, "at", "createdAt", "updatedAt"),
                Source = ReadString(log, "source"),
                OperatorId = ReadString(log, "operatorId"),
                OperatorOpenid = ReadString(log, "operatorOpenid"),
                Changes = CloneElement(log, "changes") ?? CloneElement(log, "payload")
            });
        }

        return logs;
    }

    private static JsonElement GetPayloadRoot(JsonElement root)
    {
        return TryGet(root, "data", out var data) && data.ValueKind == JsonValueKind.Object
            ? data
            : root;
    }

    private static JsonElement GetObjectOrFallback(JsonElement element, string name, JsonElement fallback)
    {
        return TryGet(element, name, out var property) && property.ValueKind == JsonValueKind.Object
            ? property
            : fallback;
    }

    private static JsonElement GetObjectOrEmpty(JsonElement element, string name)
    {
        return TryGet(element, name, out var property) && property.ValueKind == JsonValueKind.Object
            ? property
            : default;
    }

    private static JsonElement GetFirstObject(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGet(element, name, out var property) && property.ValueKind == JsonValueKind.Object)
            {
                return property;
            }
        }

        return default;
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

    private static JsonElement GetFirstObjectFromProperty(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGet(element, name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Object)
            {
                return property;
            }

            if (property.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in property.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    return item;
                }
            }
        }

        return default;
    }

    private static bool TryGet(JsonElement element, string name, out JsonElement property)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out property))
        {
            return true;
        }

        property = default;
        return false;
    }

    private static string ReadStringWithFallback(JsonElement primary, JsonElement fallback, params string[] names)
    {
        var value = ReadString(primary, names);
        return string.IsNullOrWhiteSpace(value) ? ReadString(fallback, names) : value;
    }

    private static string ReadStringWithFallback(JsonElement primary, JsonElement secondary, JsonElement fallback, params string[] names)
    {
        var value = ReadString(primary, names);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        value = ReadString(secondary, names);
        return string.IsNullOrWhiteSpace(value) ? ReadString(fallback, names) : value;
    }

    private static int ReadIntWithFallback(JsonElement primary, JsonElement fallback, params string[] names)
    {
        var value = ReadInt(primary, names);
        return value == 0 ? ReadInt(fallback, names) : value;
    }

    private static long ReadLongWithFallback(JsonElement primary, JsonElement fallback, params string[] names)
    {
        var value = ReadLong(primary, names);
        return value == 0 ? ReadLong(fallback, names) : value;
    }

    private static long ReadLongWithFallback(JsonElement primary, JsonElement secondary, JsonElement fallback, params string[] names)
    {
        var value = ReadLong(primary, names);
        if (value > 0)
        {
            return value;
        }

        value = ReadLong(secondary, names);
        return value == 0 ? ReadLong(fallback, names) : value;
    }

    private static decimal ReadDecimalWithFallback(JsonElement primary, JsonElement fallback, params string[] names)
    {
        var value = ReadDecimal(primary, names);
        return value == 0 ? ReadDecimal(fallback, names) : value;
    }

    private static string ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGet(element, name, out var property))
            {
                continue;
            }

            return property.ValueKind switch
            {
                JsonValueKind.String => property.GetString()?.Trim() ?? string.Empty,
                JsonValueKind.Number => property.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => string.Empty
            };
        }

        return string.Empty;
    }

    private static int ReadInt(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGet(element, name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
            {
                return value;
            }

            if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out value))
            {
                return value;
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

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var value))
            {
                return value;
            }

            if (property.ValueKind == JsonValueKind.String && long.TryParse(property.GetString(), out value))
            {
                return value;
            }
        }

        return 0;
    }

    private static decimal ReadDecimal(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGet(element, name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var value))
            {
                return value;
            }

            if (property.ValueKind == JsonValueKind.String && decimal.TryParse(property.GetString(), out value))
            {
                return value;
            }
        }

        return 0;
    }

    private static bool ReadBool(JsonElement element, string name)
    {
        if (!TryGet(element, name, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(property.GetString(), out var value) && value,
            _ => false
        };
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string name)
    {
        if (!TryGet(element, name, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()?.Trim() ?? string.Empty : item.GetRawText())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
    }

    private static string ReadStringFromCandidates(IReadOnlyList<JsonElement> candidates, params string[] names)
    {
        foreach (var candidate in candidates)
        {
            var value = ReadString(candidate, names);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static long ReadLongFromCandidates(IReadOnlyList<JsonElement> candidates, params string[] names)
    {
        foreach (var candidate in candidates)
        {
            var value = ReadLong(candidate, names);
            if (value > 0)
            {
                return value;
            }
        }

        return 0;
    }

    private static IReadOnlyList<string> ReadStringArrayFromCandidates(IReadOnlyList<JsonElement> candidates, params string[] names)
    {
        foreach (var candidate in candidates)
        {
            foreach (var name in names)
            {
                var values = ReadStringArray(candidate, name);
                if (values.Count > 0)
                {
                    return values;
                }
            }
        }

        return [];
    }

    private static bool ReadBoolFromCandidates(IReadOnlyList<JsonElement> candidates, params string[] names)
    {
        foreach (var candidate in candidates)
        {
            foreach (var name in names)
            {
                if (!TryGet(candidate, name, out var property))
                {
                    continue;
                }

                if (property.ValueKind == JsonValueKind.True)
                {
                    return true;
                }

                if (property.ValueKind == JsonValueKind.False)
                {
                    return false;
                }

                if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var intValue))
                {
                    return intValue != 0;
                }

                if (property.ValueKind == JsonValueKind.String)
                {
                    var stringValue = property.GetString();
                    if (bool.TryParse(stringValue, out var boolValue))
                    {
                        return boolValue;
                    }

                    if (int.TryParse(stringValue, out intValue))
                    {
                        return intValue != 0;
                    }
                }
            }
        }

        return false;
    }

    private static bool HasAddressCoreData(StringNarrationAddressSnapshot address)
    {
        if (!string.IsNullOrWhiteSpace(address.FullAddress))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(address.AddressSummary))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(address.Region) && !string.IsNullOrWhiteSpace(address.Detail);
    }

    private static string NormalizeValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
    }

    private static JsonElement? CloneElement(JsonElement element, string name)
    {
        if (!TryGet(element, name, out var property) || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return property.Clone();
    }
}
