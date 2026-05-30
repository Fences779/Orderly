using Orderly.Core.Models;

namespace Orderly.Data.Services;

public sealed partial class StringNarrationGatewayOrderService
{
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
}
