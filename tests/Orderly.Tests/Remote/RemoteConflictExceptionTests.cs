using System.Text.Json;
using Orderly.Remote.Clients;
using Xunit;

namespace Orderly.Tests.Remote;

public sealed class RemoteConflictExceptionTests
{
    [Fact]
    public void FromResponseContent_parses_structured_conflict_payload()
    {
        var payload = new
        {
            Error = "Conflict detected",
            Detail = "Price was updated",
            ActorDisplayName = "Alice",
            UpdatedAt = new DateTime(2024, 5, 1, 12, 0, 0, DateTimeKind.Utc),
            LatestRevision = 42L
        };
        var json = JsonSerializer.Serialize(payload);

        var ex = RemoteConflictException.FromResponseContent(json);

        Assert.Equal("Conflict detected", ex.Message);
        Assert.Equal("Price was updated", ex.Detail);
        Assert.Equal("Alice", ex.ActorDisplayName);
        Assert.Equal(payload.UpdatedAt, ex.UpdatedAt);
        Assert.Equal(42L, ex.LatestRevision);
        Assert.Equal(json, ex.RawContent);
    }

    [Fact]
    public void FromResponseContent_falls_back_to_human_friendly_message_when_payload_missing()
    {
        var ex = RemoteConflictException.FromResponseContent(string.Empty);

        Assert.Contains("云端数据已经被其他人更新", ex.Message);
        Assert.Null(ex.ActorDisplayName);
        Assert.Null(ex.UpdatedAt);
        Assert.Null(ex.LatestRevision);
    }

    [Fact]
    public void FromResponseContent_falls_back_to_human_friendly_message_for_malformed_json()
    {
        var ex = RemoteConflictException.FromResponseContent("not json");

        Assert.Contains("云端数据已经被其他人更新", ex.Message);
    }
}
