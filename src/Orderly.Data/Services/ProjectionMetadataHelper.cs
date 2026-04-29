using System.Text.Json;

namespace Orderly.Data.Services;

internal static class ProjectionMetadataHelper
{
    public static string? ReadAutoReplyState(string metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            if (!document.RootElement.TryGetProperty("autoReply", out var autoReply))
            {
                return null;
            }

            return autoReply.TryGetProperty("state", out var stateElement)
                ? stateElement.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static int? ReadConvertedToMessageId(string metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            if (!document.RootElement.TryGetProperty("convertedToMessageId", out var convertedElement))
            {
                return null;
            }

            return convertedElement.ValueKind == JsonValueKind.Number && convertedElement.TryGetInt32(out var convertedId)
                ? convertedId
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
