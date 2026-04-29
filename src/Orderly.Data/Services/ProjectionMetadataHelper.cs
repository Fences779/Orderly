using System.Text.Json;
using Orderly.Core.Models;

namespace Orderly.Data.Services;

internal static class ProjectionMetadataHelper
{
    public static string? ReadAutoReplyState(string metadataJson)
    {
        return AutoReplyMetadataHelper.ReadState(metadataJson);
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
