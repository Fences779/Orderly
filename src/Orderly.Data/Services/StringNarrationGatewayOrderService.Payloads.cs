using Orderly.Core.Models;

namespace Orderly.Data.Services;

public sealed partial class StringNarrationGatewayOrderService
{
    private static Dictionary<string, object?> BuildLookupPayload(string orderNo, string tradeNo, string id)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal);
        var safeId = StringNarrationGatewayInputSafety.NormalizeIdentifier(id, "_id");
        var safeOrderNo = StringNarrationGatewayInputSafety.NormalizeIdentifier(orderNo, "orderNo");
        var safeTradeNo = StringNarrationGatewayInputSafety.NormalizeIdentifier(tradeNo, "tradeNo");

        if (!string.IsNullOrWhiteSpace(safeId))
        {
            payload["_id"] = safeId;
        }
        else if (!string.IsNullOrWhiteSpace(safeOrderNo))
        {
            payload["orderNo"] = safeOrderNo;
        }
        else if (!string.IsNullOrWhiteSpace(safeTradeNo))
        {
            payload["tradeNo"] = safeTradeNo;
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
            payload["page"] = StringNarrationGatewayInputSafety.NormalizePage(query.Page);
            payload["pageSize"] = StringNarrationGatewayInputSafety.NormalizePageSize(query.PageSize, fallback: 20);
        }

        AddIfPresent(payload, "keyword", query.Keyword, StringNarrationGatewayInputSafety.MaxKeywordCharacters);
        AddIfPresent(payload, "status", query.Status, StringNarrationGatewayInputSafety.MaxFilterCharacters);
        AddIfPresent(payload, "fulfillmentStatus", query.FulfillmentStatus, StringNarrationGatewayInputSafety.MaxFilterCharacters);
        AddIfPositive(payload, "startAt", StringNarrationGatewayInputSafety.NormalizeTimestamp(query.StartAt));
        AddIfPositive(payload, "endAt", StringNarrationGatewayInputSafety.NormalizeTimestamp(query.EndAt));
        return payload;
    }

    private static void AddIfPositive(Dictionary<string, object?> payload, string key, long value)
    {
        if (value > 0)
        {
            payload[key] = value;
        }
    }

    private static void AddIfPresent(
        Dictionary<string, object?> payload,
        string key,
        string? value,
        int maxCharacters = StringNarrationGatewayInputSafety.MaxFilterCharacters)
    {
        var normalized = maxCharacters == StringNarrationGatewayInputSafety.MaxRemarkCharacters
            ? StringNarrationGatewayInputSafety.NormalizeRemark(value, key)
            : maxCharacters == StringNarrationGatewayInputSafety.MaxKeywordCharacters
                ? StringNarrationGatewayInputSafety.NormalizeKeyword(value, key)
                : StringNarrationGatewayInputSafety.NormalizeFilter(value, key);

        if (!string.IsNullOrWhiteSpace(normalized))
        {
            payload[key] = normalized;
        }
    }
}
