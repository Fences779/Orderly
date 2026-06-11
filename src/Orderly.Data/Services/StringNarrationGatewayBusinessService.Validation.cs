using System.Globalization;
using System.Text.Json;
using Orderly.Core.Models;

namespace Orderly.Data.Services;

public sealed partial class StringNarrationGatewayBusinessService
{
    private const int MaxGatewayStringCharacters = 4096;
    private const int MaxGatewayStringArrayItems = 50;

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

            var value = ReadStringValue(property);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
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

            if (TryReadBoundedString(property, out var stringValue)
                && decimal.TryParse(stringValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
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

            if (TryReadBoundedString(property, out var stringValue)
                && int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
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
                if (!TryReadBoundedString(property, out var value))
                {
                    continue;
                }

                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLong))
                {
                    return NormalizeTimestamp(parsedLong);
                }

                if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDate))
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
                JsonValueKind.String => TryReadBoundedString(element, out var stringValue)
                    && bool.TryParse(stringValue, out var parsed)
                    && parsed,
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

            if (TryReadBoundedString(property, out var stringValue)
                && decimal.TryParse(stringValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
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

            if (TryReadBoundedString(property, out var stringValue)
                && int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
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
            if (property.GetArrayLength() > MaxGatewayStringArrayItems)
            {
                throw new InvalidOperationException($"网关返回字符串数组 {name} 超过客户端处理上限。");
            }

            return property.EnumerateArray()
                .Select(item => ReadStringValue(item))
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToArray();
        }

        var raw = ReadStringValue(property);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var values = raw.Split([',', '，', '/', '、', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length > MaxGatewayStringArrayItems)
        {
            throw new InvalidOperationException($"网关返回字符串数组 {name} 超过客户端处理上限。");
        }

        return values
            .Select(NormalizeGatewayString)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
    }

    private static string ReadStringValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => NormalizeGatewayString(element.GetString()),
            JsonValueKind.Number => NormalizeGatewayString(element.GetRawText()),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    private static bool TryReadBoundedString(JsonElement element, out string value)
    {
        value = string.Empty;
        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = NormalizeGatewayString(element.GetString());
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string NormalizeGatewayString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        if (normalized.Length > MaxGatewayStringCharacters)
        {
            throw new InvalidOperationException("网关返回字符串超过客户端处理上限。");
        }

        if (normalized.Any(static ch => char.IsControl(ch) && ch is not '\r' and not '\n' and not '\t'))
        {
            throw new InvalidOperationException("网关返回字符串包含不允许的控制字符。");
        }

        return normalized;
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
