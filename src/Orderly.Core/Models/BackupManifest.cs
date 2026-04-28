using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orderly.Core.Models;

public sealed class BackupManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("app")]
    public string App { get; set; } = "Orderly";

    [JsonPropertyName("exportedAt")]
    public DateTimeOffset ExportedAt { get; set; }

    [JsonPropertyName("tables")]
    public Dictionary<string, JsonElement> Tables { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("counts")]
    public Dictionary<string, int> Counts { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("checksum")]
    public string Checksum { get; set; } = string.Empty;
}
