using System.Net;
using System.Text.Json;

namespace Orderly.Core.Models;

public sealed partial class StringNarrationProductionSheetSnapshot
{
    private const int MaxDisplayImageUrlLength = 2048;

    private static readonly string[] LocalImageHostSuffixes =
    [
        ".local",
        ".localhost",
        ".internal",
        ".lan",
        ".home",
        ".corp",
        ".test",
        ".invalid",
        ".example"
    ];

    private static string FindNamedValue(JsonElement element, IReadOnlyList<string> fieldNames, int depth)
    {
        if (depth > 5)
        {
            return string.Empty;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var fieldName in fieldNames)
            {
                if (!TryGetPropertyIgnoreCase(element, fieldName, out var value))
                {
                    continue;
                }

                var extracted = ExtractMeaningfulText(value);
                if (!string.IsNullOrWhiteSpace(extracted))
                {
                    return extracted;
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                {
                    continue;
                }

                var nested = FindNamedValue(property.Value, fieldNames, depth + 1);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindNamedValue(item, fieldNames, depth + 1);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return string.Empty;
    }

    private static string ExtractMeaningfulText(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
        {
            return NormalizeScalar(value);
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                var nested = ExtractMeaningfulText(item);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }

            return string.Empty;
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            var direct = ReadFirstNonEmptyString(value);
            if (!string.IsNullOrWhiteSpace(direct))
            {
                return direct;
            }

            foreach (var property in value.EnumerateObject())
            {
                var nested = ExtractMeaningfulText(property.Value);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return string.Empty;
    }

    private static int ReadPositiveInt(JsonElement? element, params string[] fieldNames)
    {
        if (element is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        foreach (var fieldName in fieldNames)
        {
            if (!TryGetPropertyIgnoreCase(json, fieldName, out var value))
            {
                continue;
            }

            var parsed = ParsePositiveInt(value);
            if (parsed > 0)
            {
                return parsed;
            }
        }

        return 0;
    }

    private static string ReadScalarProperty(JsonElement element, IReadOnlyList<string> fieldNames)
    {
        foreach (var fieldName in fieldNames)
        {
            if (!TryGetPropertyIgnoreCase(element, fieldName, out var value))
            {
                continue;
            }

            var scalar = NormalizeScalar(value);
            if (!string.IsNullOrWhiteSpace(scalar))
            {
                return scalar;
            }
        }

        return string.Empty;
    }

    private static string ReadNonEmptyString(JsonElement? element, params string[] fieldNames)
    {
        if (element is not JsonElement json || json.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        return ReadScalarProperty(json, fieldNames);
    }

    private static string ReadFirstNonEmptyString(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        foreach (var property in element.EnumerateObject())
        {
            var scalar = NormalizeScalar(property.Value);
            if (!string.IsNullOrWhiteSpace(scalar))
            {
                return scalar;
            }
        }

        return string.Empty;
    }

    private static int ParsePositiveInt(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsedByNumber))
        {
            return parsedByNumber > 0 ? parsedByNumber : 0;
        }

        var scalar = NormalizeScalar(value);
        return int.TryParse(scalar, out var parsedByString) && parsedByString > 0 ? parsedByString : 0;
    }

    private static string NormalizeScalar(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string NormalizeImageUrl(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Length > MaxDisplayImageUrlLength
            || normalized.Any(char.IsControl)
            || string.Equals(normalized, "[object Object]", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment)
            || uri.HostNameType == UriHostNameType.Unknown
            || IsLocalOrPrivateImageHost(uri.Host))
        {
            return string.Empty;
        }

        return uri.AbsoluteUri;
    }

    private static bool IsLocalOrPrivateImageHost(string host)
    {
        var normalizedHost = host.Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(normalizedHost))
        {
            return true;
        }

        if (IPAddress.TryParse(normalizedHost, out var address))
        {
            return IsLocalOrPrivateImageAddress(address);
        }

        if (string.Equals(normalizedHost, "localhost", StringComparison.OrdinalIgnoreCase)
            || !normalizedHost.Contains('.'))
        {
            return true;
        }

        return LocalImageHostSuffixes.Any(suffix => normalizedHost.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLocalOrPrivateImageAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return IsLocalOrPrivateImageAddress(address.MapToIPv4());
        }

        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
        {
            return bytes[0] == 0
                || bytes[0] == 10
                || bytes[0] == 127
                || (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
                || (bytes[0] == 169 && bytes[1] == 254)
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 0)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 198 && (bytes[1] == 18 || bytes[1] == 19))
                || bytes[0] >= 224;
        }

        if (bytes.Length == 16)
        {
            return bytes.All(value => value == 0)
                || bytes[0] == 0xff
                || (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80)
                || (bytes[0] & 0xfe) == 0xfc;
        }

        return true;
    }

    private static string NormalizeLoopType(string value)
    {
        return value.Trim() switch
        {
            "single" => "单圈",
            "double" => "双圈",
            _ => value.Trim()
        };
    }

    private static int ParseLeadingInt(string value)
    {
        var digits = new string(value.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var parsed) ? parsed : 0;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private static string MapStatusTextToCode(string? statusText, string defaultCode)
    {
        if (string.IsNullOrWhiteSpace(statusText)) return defaultCode;
        statusText = statusText.Trim();
        return statusText switch
        {
            "已支付待确认" or "paid_pending_confirm" => "paid_pending_confirm",
            "待制作" or "pending_make" => "pending_make",
            "制作中" or "making" => "making",
            "待发货" or "ready_to_ship" => "ready_to_ship",
            "已发货" or "shipped" => "shipped",
            "已完成" or "completed" => "completed",
            "异常" or "exception" => "exception",
            _ => defaultCode
        };
    }

    private static string BuildValue(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private sealed record ProductionBeadToken(string DisplayName, string GroupKey);
}
