using Orderly.Contracts.Offline;
using Orderly.Data.Cloud;
using Orderly.Data.Sqlite;
using Xunit;

namespace Orderly.Tests.Cloud;

public sealed class CloudOutboxStoreTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"orderly-outbox-{Guid.NewGuid():N}.db");
    private readonly SqliteConnectionFactory _factory;

    public CloudOutboxStoreTests()
    {
        _factory = new SqliteConnectionFactory(_databasePath);
        using var connection = _factory.CreateConnection();
        connection.Open();
        CloudCacheSchemaInitializer.InitializeSchemaAsync(connection).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Outbox_persists_ready_entries_and_honors_retry_time()
    {
        var createdAt = DateTime.UtcNow.AddMinutes(-5);
        var entry = new CloudOutboxEntryDto
        {
            Id = "outbox-1",
            EntityType = "customer",
            EntityId = "customer-1",
            OperationType = "update",
            PayloadJson = "{\"name\":\"A\"}",
            BaseRevision = 3,
            ClientRequestId = "client-request-1",
            CreatedAtUtc = createdAt
        };

        var firstStore = new CloudOutboxStore(_factory);
        await firstStore.AddAsync(entry);

        var secondStore = new CloudOutboxStore(_factory);
        var ready = await secondStore.ListReadyAsync(DateTime.UtcNow);

        var persisted = Assert.Single(ready);
        Assert.Equal("outbox-1", persisted.Id);
        Assert.Equal("client-request-1", persisted.ClientRequestId);

        var retryAt = DateTime.UtcNow.AddMinutes(10);
        await secondStore.MarkFailedAsync("outbox-1", "offline", retryAt);
        Assert.Empty(await secondStore.ListReadyAsync(DateTime.UtcNow));

        var retryReady = await secondStore.ListReadyAsync(retryAt.AddSeconds(1));
        var retryEntry = Assert.Single(retryReady);
        Assert.Equal(1, retryEntry.AttemptCount);
        Assert.Equal("offline", retryEntry.LastSubmitError);

        await secondStore.MarkSubmittedAsync("outbox-1");
        Assert.Empty(await secondStore.ListReadyAsync(DateTime.UtcNow.AddHours(1)));
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }
        }
        catch
        {
            // Best-effort cleanup for Windows file handles held by failed tests.
        }
    }
}
