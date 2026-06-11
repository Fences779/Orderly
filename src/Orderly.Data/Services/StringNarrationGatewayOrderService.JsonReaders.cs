using System.Globalization;
using System.Text.Json;
using Orderly.Core.Models;

namespace Orderly.Data.Services;

public sealed partial class StringNarrationGatewayOrderService
{
    private const int MaxGatewayStringCharacters = 4096;
    private const int MaxGatewayStringArrayItems = 50;

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

    private static void RequireArray(JsonElement element, string name, string contractName)
    {
        if (!TryGet(element, name, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"{contractName} 返回缺少数组字段 {name}。");
        }
    }

    private static void RequireProperties(JsonElement element, string contractName, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGet(element, name, out _))
            {
                throw new InvalidOperationException($"{contractName} 返回缺少字段 {name}。");
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

    private static JsonElement GetFirstArrayFromCandidates(IReadOnlyList<JsonElement> candidates, params string[] names)
    {
        foreach (var candidate in candidates)
        {
            var value = GetFirstArray(candidate, names);
            if (value.ValueKind == JsonValueKind.Array)
            {
                return value;
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
                JsonValueKind.String => NormalizeGatewayString(property.GetString()),
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

            if (TryReadBoundedString(property, out var stringValue)
                && int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
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

            if (TryReadBoundedString(property, out var stringValue)
                && long.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
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

            if (TryReadBoundedString(property, out var stringValue)
                && decimal.TryParse(stringValue, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
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
            JsonValueKind.String => TryReadBoundedString(property, out var stringValue)
                && bool.TryParse(stringValue, out var value)
                && value,
            _ => false
        };
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string name)
    {
        if (!TryGet(element, name, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        if (property.GetArrayLength() > MaxGatewayStringArrayItems)
        {
            throw new InvalidOperationException($"网关返回字符串数组 {name} 超过客户端处理上限。");
        }

        return property.EnumerateArray()
            .Select(ReadStringValue)
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

    private static int ReadIntFromCandidates(IReadOnlyList<JsonElement> candidates, params string[] names)
    {
        foreach (var candidate in candidates)
        {
            var value = ReadInt(candidate, names);
            if (value != 0)
            {
                return value;
            }
        }

        return 0;
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

    private static decimal ReadDecimalFromCandidates(IReadOnlyList<JsonElement> candidates, params string[] names)
    {
        foreach (var candidate in candidates)
        {
            var value = ReadDecimal(candidate, names);
            if (value != 0)
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
                    if (!TryReadBoundedString(property, out var stringValue))
                    {
                        continue;
                    }

                    if (bool.TryParse(stringValue, out var boolValue))
                    {
                        return boolValue;
                    }

                    if (int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
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
        var normalized = NormalizeGatewayString(value);
        return string.IsNullOrWhiteSpace(normalized) ? string.Empty : normalized.ToLowerInvariant();
    }

    private static JsonElement? CloneElement(JsonElement element, string name)
    {
        if (!TryGet(element, name, out var property) || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return property.Clone();
    }

    private static InvalidOperationException WrapActionException(string action, InvalidOperationException ex)
    {
        return ex.Message.Contains($"action={action}", StringComparison.OrdinalIgnoreCase)
            ? ex
            : new InvalidOperationException($"调用串述 adminPcGateway action={action} 失败：{ex.Message}", ex);
    }

    private static string ReadStringValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => NormalizeGatewayString(element.GetString()),
            JsonValueKind.Number => element.GetRawText(),
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
}
