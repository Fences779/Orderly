using System.Text.Json;

namespace Orderly.Core.Models;

public static class AutoReplyMetadataHelper
{
    private const int MaxMetadataJsonCharacters = 8192;
    private const int MaxMetadataStateCharacters = 64;

    private static readonly JsonDocumentOptions MetadataJsonDocumentOptions = new()
    {
        MaxDepth = 16
    };

    public static string? ReadState(string metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson) || metadataJson.Length > MaxMetadataJsonCharacters)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(metadataJson, MetadataJsonDocumentOptions);
            if (!document.RootElement.TryGetProperty("autoReply", out var autoReply))
            {
                return null;
            }

            return autoReply.TryGetProperty("state", out var stateElement) && stateElement.ValueKind == JsonValueKind.String
                ? NormalizeMetadataState(stateElement.GetString())
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? NormalizeMetadataState(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > MaxMetadataStateCharacters
            || normalized.Any(static ch => char.IsControl(ch) && ch is not '\r' and not '\n' and not '\t'))
        {
            return null;
        }

        return normalized;
    }
}
