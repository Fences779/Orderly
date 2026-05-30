using System.Text.Json;
using Orderly.Core.Models;

namespace Orderly.Data.Services;

public sealed partial class StringNarrationGatewayOrderService
{
    private static StringNarrationExceptionAuditEntry BuildAuditEntryFromRequest(
        StringNarrationExceptionActionRequest request,
        StringNarrationExceptionSnapshot snapshot)
    {
        var at = request.ActionAt > 0
            ? request.ActionAt
            : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        return new StringNarrationExceptionAuditEntry
        {
            Action = request.NormalizedAction,
            FromStatus = snapshot.ResolutionStatus,
            ToStatus = request.TargetResolutionStatus,
            OperatorId = request.OperatorId,
            OperatorOpenid = request.OperatorOpenid,
            Remark = request.AdminResolutionRemark,
            ResolutionAction = request.ResolutionAction,
            Assignee = request.Assignee,
            Priority = request.Priority,
            Source = "orderly_desktop",
            At = at
        };
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

    private static IReadOnlyList<StringNarrationBusinessTrendPoint> ParseBusinessTrendItems(IReadOnlyList<JsonElement> sources)
    {
        var trendArray = GetFirstArrayFromCandidates(
            sources,
            "recentBusinessTrendItems",
            "recentBusinessTrend",
            "businessTrend",
            "trend",
            "last7Days");
        if (trendArray.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<StringNarrationBusinessTrendPoint>();
        foreach (var item in trendArray.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            result.Add(new StringNarrationBusinessTrendPoint
            {
                DateKey = ReadString(item, "dateKey", "date", "day"),
                Label = ReadString(item, "label", "dateLabel", "dayLabel"),
                OrderCount = ReadInt(item, "orderCount", "orders", "count"),
                RevenueAmount = ReadDecimal(item, "revenueAmount", "revenue", "amount", "salesAmount")
            });
        }

        return result;
    }

    private static IReadOnlyList<StringNarrationFulfillmentPressureMetric> ParseFulfillmentPressureItems(
        IReadOnlyList<JsonElement> sources,
        IReadOnlyDictionary<string, int> counts,
        int unfinishedOrderCount,
        int totalCount)
    {
        var pressureArray = GetFirstArrayFromCandidates(
            sources,
            "fulfillmentPressureItems",
            "fulfillmentPressure",
            "pressureItems",
            "pressure");
        if (pressureArray.ValueKind == JsonValueKind.Array)
        {
            var parsed = new List<StringNarrationFulfillmentPressureMetric>();
            foreach (var item in pressureArray.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var status = ReadString(item, "fulfillmentStatus", "status", "key");
                var count = ReadInt(item, "count", "value", "orderCount");
                var targetCount = ReadInt(item, "targetCount", "target", "total");
                var ratio = ReadDecimal(item, "ratio", "percent", "percentage");
                parsed.Add(new StringNarrationFulfillmentPressureMetric
                {
                    FulfillmentStatus = status,
                    Label = ReadString(item, "label", "name"),
                    Count = count,
                    TargetCount = targetCount,
                    Ratio = NormalizeRatio(ratio)
                });
            }

            return parsed;
        }

        return BuildDefaultFulfillmentPressureItems(counts, unfinishedOrderCount, totalCount);
    }

    private static IReadOnlyList<StringNarrationFulfillmentPressureMetric> BuildDefaultFulfillmentPressureItems(
        IReadOnlyDictionary<string, int> counts,
        int unfinishedOrderCount,
        int totalCount)
    {
        var targetCount = unfinishedOrderCount > 0 ? unfinishedOrderCount : totalCount;
        return new[]
        {
            BuildDefaultFulfillmentPressureItem(StringNarrationFulfillmentStatusCatalog.PendingMake, counts, targetCount),
            BuildDefaultFulfillmentPressureItem(StringNarrationFulfillmentStatusCatalog.ReadyToShip, counts, targetCount)
        };
    }

    private static StringNarrationFulfillmentPressureMetric BuildDefaultFulfillmentPressureItem(
        string fulfillmentStatus,
        IReadOnlyDictionary<string, int> counts,
        int targetCount)
    {
        counts.TryGetValue(fulfillmentStatus, out var count);
        var definition = StringNarrationFulfillmentStatusCatalog.Resolve(fulfillmentStatus);
        return new StringNarrationFulfillmentPressureMetric
        {
            FulfillmentStatus = fulfillmentStatus,
            Label = definition.Label,
            Count = count,
            TargetCount = targetCount,
            Ratio = targetCount <= 0 ? 0 : (decimal)count / targetCount
        };
    }

    private static int CountFromMap(IReadOnlyDictionary<string, int> counts, params string[] statuses)
    {
        var total = 0;
        foreach (var status in statuses)
        {
            counts.TryGetValue(status, out var count);
            total += count;
        }

        return total;
    }

    private static decimal NormalizeRatio(decimal value)
    {
        return value > 1 ? value / 100 : value;
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
}
