using System.Text.Json;
using Orderly.Contracts.Commerce;
using Orderly.Contracts.Offline;
using Orderly.Contracts.Permissions;
using Orderly.Contracts.Sync;
using Orderly.Remote.Auth;
using Orderly.Remote.Clients;

namespace Orderly.Remote.Sync;

public sealed class RemoteWorkspaceSyncClient : IAsyncDisposable
{
    private const string SyncStateEntityType = "__sync";
    private const string SyncStateEntityId = "workspace";
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(45);
    private static readonly JsonSerializerOptions CacheJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly RemoteCommerceClient _client;
    private readonly CloudAuthSession _session;
    private readonly ICloudCacheStore _cacheStore;
    private readonly TimeSpan _interval;
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    public event Action? CacheChanged;

    public RemoteWorkspaceSyncClient(
        RemoteCommerceClient client,
        CloudAuthSession session,
        ICloudCacheStore cacheStore,
        TimeSpan? interval = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
        _interval = interval ?? DefaultInterval;
    }

    public void Start()
    {
        if (_loopCts is not null)
        {
            return;
        }

        _loopCts = new CancellationTokenSource();
        _loopTask = RunLoopAsync(_loopCts.Token);
        _ = TriggerSyncSafelyAsync(_loopCts.Token);
    }

    public async Task TriggerSyncAsync(CancellationToken cancellationToken = default)
    {
        if (!await _syncGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await SyncOnceAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
                await TriggerSyncAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (HttpRequestException)
            {
                // Offline is expected; cached data stays readable and the next tick retries.
            }
        }
    }

    private async Task TriggerSyncSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await TriggerSyncAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (HttpRequestException)
        {
        }
    }

    private async Task SyncOnceAsync(CancellationToken cancellationToken)
    {
        var state = await LoadStateAsync(cancellationToken).ConfigureAwait(false);
        if (state.LastSequence <= 0)
        {
            await FullResyncAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var changes = await _client.GetAsync<ChangesResponse>(
            $"api/workspaces/{_session.WorkspaceId:N}/sync/changes?afterSequence={state.LastSequence}&limit=500",
            cancellationToken).ConfigureAwait(false);

        if (changes is null)
        {
            return;
        }

        if (changes.FullResyncRequired || changes.Changes.Count > 0)
        {
            await FullResyncAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (changes.ToSequence > state.LastSequence)
        {
            await SaveStateAsync(changes.ToSequence, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task FullResyncAsync(CancellationToken cancellationToken)
    {
        var entries = new List<CloudCacheEntryDto>();
        long latestSequence = 0;

        foreach (var entity in GetSnapshotEntities())
        {
            var token = await _client.PostAsync<SnapshotRequest, SnapshotTokenResponse>(
                $"api/workspaces/{_session.WorkspaceId:N}/sync/snapshots",
                new SnapshotRequest { EntityType = entity.ServerEntityType },
                cancellationToken).ConfigureAwait(false);

            if (token is null || string.IsNullOrWhiteSpace(token.SnapshotToken))
            {
                continue;
            }

            latestSequence = Math.Max(latestSequence, token.SnapshotSequence);
            var entityEntries = new List<CloudCacheEntryDto>();
            var page = 1;
            SnapshotPageResponse<JsonElement>? response;
            do
            {
                response = await _client.GetAsync<SnapshotPageResponse<JsonElement>>(
                    $"api/workspaces/{_session.WorkspaceId:N}/sync/snapshots/{Uri.EscapeDataString(token.SnapshotToken)}?entityType={entity.ServerEntityType}&page={page}&pageSize=200",
                    cancellationToken).ConfigureAwait(false);

                if (response is null)
                {
                    break;
                }

                latestSequence = Math.Max(latestSequence, response.SnapshotSequence);
                foreach (var item in response.Items)
                {
                    if (!TryGetGuid(item, "id", out var entityId))
                    {
                        continue;
                    }

                    var revision = TryGetInt64(item, "revision", out var parsedRevision)
                        ? parsedRevision
                        : response.SnapshotSequence;

                    entityEntries.Add(new CloudCacheEntryDto
                    {
                        EntityType = entity.CacheEntityType,
                        EntityId = entityId.ToString("N"),
                        PayloadJson = item.GetRawText(),
                        Revision = revision,
                        CachedAtUtc = DateTime.UtcNow
                    });
                }

                page++;
            }
            while (response is not null && (page - 1) * response.PageSize < response.TotalCount);

            entries.AddRange(entityEntries);
            entries.Add(new CloudCacheEntryDto
            {
                EntityType = entity.CacheEntityType,
                EntityId = "all",
                PayloadJson = BuildJsonArray(entityEntries),
                Revision = entityEntries.Count > 0 ? entityEntries.Max(static e => e.Revision) : latestSequence,
                CachedAtUtc = DateTime.UtcNow
            });
        }

        entries.Add(CreateStateEntry(latestSequence));
        await _cacheStore.ReplaceAllAsync(entries, cancellationToken).ConfigureAwait(false);
        CacheChanged?.Invoke();
    }

    private async Task<SyncState> LoadStateAsync(CancellationToken cancellationToken)
    {
        var entry = await _cacheStore.GetAsync(SyncStateEntityType, SyncStateEntityId, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return new SyncState();
        }

        return JsonSerializer.Deserialize<SyncState>(entry.PayloadJson, CacheJsonOptions) ?? new SyncState();
    }

    private Task SaveStateAsync(long sequence, CancellationToken cancellationToken)
        => _cacheStore.SetAsync(CreateStateEntry(sequence), cancellationToken);

    private static CloudCacheEntryDto CreateStateEntry(long sequence) => new()
    {
        EntityType = SyncStateEntityType,
        EntityId = SyncStateEntityId,
        PayloadJson = JsonSerializer.Serialize(new SyncState { LastSequence = sequence }, CacheJsonOptions),
        Revision = sequence,
        CachedAtUtc = DateTime.UtcNow
    };

    private IReadOnlyList<SnapshotEntity> GetSnapshotEntities()
    {
        var entities = new List<SnapshotEntity>
        {
            new("orders", EntityType.Order),
            new("products", EntityType.Product),
            new("inventory", EntityType.InventoryItem),
            new("customers", EntityType.Customer),
            new("task", EntityType.BusinessTask)
        };

        if (string.Equals(_session.Role, CloudRole.Admin, StringComparison.OrdinalIgnoreCase))
        {
            entities.Add(new SnapshotEntity("cashflow", EntityType.CashFlowEntry));
            entities.Add(new SnapshotEntity("insight", EntityType.BusinessInsight));
        }

        return entities;
    }

    private static string BuildJsonArray(IReadOnlyList<CloudCacheEntryDto> entries)
        => entries.Count == 0 ? "[]" : "[" + string.Join(",", entries.Select(static e => e.PayloadJson)) + "]";

    private static bool TryGetGuid(JsonElement element, string propertyName, out Guid value)
    {
        if (TryGetProperty(element, propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            && Guid.TryParse(property.GetString(), out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetInt64(JsonElement element, string propertyName, out long value)
    {
        if (TryGetProperty(element, propertyName, out var property)
            && property.TryGetInt64(out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement property)
    {
        if (element.TryGetProperty(propertyName, out property))
        {
            return true;
        }

        var pascalName = char.ToUpperInvariant(propertyName[0]) + propertyName[1..];
        return element.TryGetProperty(pascalName, out property);
    }

    public async ValueTask DisposeAsync()
    {
        if (_loopCts is not null)
        {
            _loopCts.Cancel();
        }

        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _loopCts?.Dispose();
        _syncGate.Dispose();
    }

    private sealed record SnapshotEntity(string ServerEntityType, string CacheEntityType);

    private sealed class SyncState
    {
        public long LastSequence { get; set; }
    }
}
