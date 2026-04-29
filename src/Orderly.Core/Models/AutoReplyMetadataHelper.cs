using System.Text.Json;

namespace Orderly.Core.Models;

public static class AutoReplyMetadataHelper
{
    public static string? ReadState(string metadataJson)
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
}
