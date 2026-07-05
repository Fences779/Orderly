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
    public event Action<string>? SyncStatusChanged;
    public event Action<bool>? ConnectivityChanged;

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
            PublishStatus("正在补同步");
            await MarkSyncAttemptAsync(cancellationToken).ConfigureAwait(false);
            await SyncOnceAsync(cancellationToken).ConfigureAwait(false);
            await MarkSyncSuccessAsync(cancellationToken).ConfigureAwait(false);
            ConnectivityChanged?.Invoke(true);
            PublishStatus("缓存可看");
        }
        catch (HttpRequestException ex)
        {
            var failureCount = await MarkSyncFailureAsync(ex.Message, cancellationToken).ConfigureAwait(false);
            ConnectivityChanged?.Invoke(false);
            PublishStatus(failureCount > 1
                ? $"同步失败 {failureCount} 次，继续使用本地缓存"
                : "同步失败，继续使用本地缓存");
            throw;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            var failureCount = await MarkSyncFailureAsync("同步请求超时。", cancellationToken).ConfigureAwait(false);
            ConnectivityChanged?.Invoke(false);
            PublishStatus(failureCount > 1
                ? $"同步失败 {failureCount} 次，继续使用本地缓存"
                : "同步失败，继续使用本地缓存");
            throw new HttpRequestException("同步请求超时。", ex);
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
            PublishStatus("正在全量重同步");
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
            if (changes.FullResyncRequired)
            {
                PublishStatus("需要全量重同步");
                await FullResyncAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            await ApplyChangesAsync(changes, cancellationToken).ConfigureAwait(false);
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

        entries.Add(CreateStateEntry(new SyncState
        {
            LastSequence = latestSequence,
            LastAttemptAtUtc = DateTime.UtcNow,
            LastSuccessfulSyncAtUtc = DateTime.UtcNow,
            LastFullResyncAtUtc = DateTime.UtcNow,
            ConsecutiveFailureCount = 0,
            LastError = null
        }));
        await _cacheStore.ReplaceAllAsync(entries, cancellationToken).ConfigureAwait(false);
        CacheChanged?.Invoke();
    }

    private async Task ApplyChangesAsync(ChangesResponse changes, CancellationToken cancellationToken)
    {
        var affectedLists = new HashSet<SnapshotEntity>();
        long appliedSequence = changes.FromSequence;

        foreach (var change in changes.Changes)
        {
            var entity = ResolveSnapshotEntity(change.EntityType);
            if (entity is null)
            {
                PublishStatus("需要全量重同步");
                await FullResyncAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            affectedLists.Add(entity);
            appliedSequence = Math.Max(appliedSequence, change.Sequence);

            if (change.EntityId is null)
            {
                continue;
            }

            if (IsRemovalAction(change.Action))
            {
                await _cacheStore.RemoveAsync(entity.CacheEntityType, change.EntityId.Value.ToString("N"), cancellationToken).ConfigureAwait(false);
                continue;
            }

            await RefreshEntityAsync(entity, change.EntityId.Value, cancellationToken).ConfigureAwait(false);
        }

        foreach (var entity in affectedLists)
        {
            await RefreshEntityListAsync(entity, cancellationToken).ConfigureAwait(false);
        }

        await SaveStateAsync(Math.Max(appliedSequence, changes.ToSequence), cancellationToken).ConfigureAwait(false);
        CacheChanged?.Invoke();
    }

    private async Task RefreshEntityAsync(SnapshotEntity entity, Guid entityId, CancellationToken cancellationToken)
    {
        var path = BuildEntityPath(entity, entityId);
        if (path is null)
        {
            return;
        }

        try
        {
            var item = await _client.GetAsync<JsonElement>(path, cancellationToken).ConfigureAwait(false);
            if (item.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            {
                await _cacheStore.RemoveAsync(entity.CacheEntityType, entityId.ToString("N"), cancellationToken).ConfigureAwait(false);
                return;
            }

            var revision = TryGetInt64(item, "revision", out var parsedRevision)
                ? parsedRevision
                : 0L;

            await _cacheStore.SetAsync(new CloudCacheEntryDto
            {
                EntityType = entity.CacheEntityType,
                EntityId = entityId.ToString("N"),
                PayloadJson = item.GetRawText(),
                Revision = revision,
                CachedAtUtc = DateTime.UtcNow
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            throw;
        }
    }

    private async Task RefreshEntityListAsync(SnapshotEntity entity, CancellationToken cancellationToken)
    {
        var path = BuildListPath(entity);
        if (path is null)
        {
            return;
        }

        var page = await _client.GetAsync<PagedList<JsonElement>>(path, cancellationToken).ConfigureAwait(false);
        if (page is null)
        {
            return;
        }

        var entries = new List<CloudCacheEntryDto>();
        foreach (var item in page.Items)
        {
            if (!TryGetGuid(item, "id", out var entityId))
            {
                continue;
            }

            entries.Add(new CloudCacheEntryDto
            {
                EntityType = entity.CacheEntityType,
                EntityId = entityId.ToString("N"),
                PayloadJson = item.GetRawText(),
                Revision = TryGetInt64(item, "revision", out var parsedRevision) ? parsedRevision : page.LatestSequence,
                CachedAtUtc = DateTime.UtcNow
            });
        }

        await _cacheStore.SetAsync(new CloudCacheEntryDto
        {
            EntityType = entity.CacheEntityType,
            EntityId = "all",
            PayloadJson = BuildJsonArray(entries),
            Revision = entries.Count > 0 ? entries.Max(static entry => entry.Revision) : page.LatestSequence,
            CachedAtUtc = DateTime.UtcNow
        }, cancellationToken).ConfigureAwait(false);
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
        => UpdateStateAsync(state => state.LastSequence = Math.Max(state.LastSequence, sequence), cancellationToken);

    private Task MarkSyncAttemptAsync(CancellationToken cancellationToken)
        => UpdateStateAsync(state => state.LastAttemptAtUtc = DateTime.UtcNow, cancellationToken);

    private Task MarkSyncSuccessAsync(CancellationToken cancellationToken)
        => UpdateStateAsync(state =>
        {
            state.LastSuccessfulSyncAtUtc = DateTime.UtcNow;
            state.ConsecutiveFailureCount = 0;
            state.LastError = null;
        }, cancellationToken);

    private async Task<int> MarkSyncFailureAsync(string error, CancellationToken cancellationToken)
    {
        var failureCount = 1;
        await UpdateStateAsync(state =>
        {
            state.LastAttemptAtUtc = DateTime.UtcNow;
            state.ConsecutiveFailureCount++;
            state.LastError = error;
            failureCount = state.ConsecutiveFailureCount;
        }, cancellationToken).ConfigureAwait(false);

        return failureCount;
    }

    private async Task UpdateStateAsync(Action<SyncState> mutate, CancellationToken cancellationToken)
    {
        var state = await LoadStateAsync(cancellationToken).ConfigureAwait(false);
        mutate(state);
        await _cacheStore.SetAsync(CreateStateEntry(state), cancellationToken).ConfigureAwait(false);
    }

    private static CloudCacheEntryDto CreateStateEntry(SyncState state) => new()
    {
        EntityType = SyncStateEntityType,
        EntityId = SyncStateEntityId,
        PayloadJson = JsonSerializer.Serialize(state, CacheJsonOptions),
        Revision = state.LastSequence,
        CachedAtUtc = DateTime.UtcNow
    };

    private IReadOnlyList<SnapshotEntity> GetSnapshotEntities()
    {
        var entities = new List<SnapshotEntity>
        {
            new("orders", EntityType.Order, "orders"),
            new("products", EntityType.Product, "products"),
            new("inventory", EntityType.InventoryItem, "inventory/items"),
            new("customers", EntityType.Customer, "customers"),
            new("task", EntityType.BusinessTask, "business-tasks")
        };

        if (string.Equals(_session.Role, CloudRole.Admin, StringComparison.OrdinalIgnoreCase))
        {
            entities.Add(new SnapshotEntity("cashflow", EntityType.CashFlowEntry, "cashflow/entries"));
            entities.Add(new SnapshotEntity("insight", EntityType.BusinessInsight, "insights"));
        }

        return entities;
    }

    private SnapshotEntity? ResolveSnapshotEntity(string entityType)
    {
        return GetSnapshotEntities().FirstOrDefault(entity =>
            string.Equals(entity.CacheEntityType, entityType, StringComparison.OrdinalIgnoreCase)
            || string.Equals(entity.ServerEntityType, entityType, StringComparison.OrdinalIgnoreCase));
    }

    private string? BuildEntityPath(SnapshotEntity entity, Guid entityId)
    {
        if (string.Equals(entity.ServerEntityType, "insight", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"api/workspaces/{_session.WorkspaceId:N}/{entity.RestPath}/{entityId:N}";
    }

    private string? BuildListPath(SnapshotEntity entity)
    {
        if (string.Equals(entity.ServerEntityType, "insight", StringComparison.OrdinalIgnoreCase))
        {
            return $"api/workspaces/{_session.WorkspaceId:N}/insights?pageSize=200";
        }

        return $"api/workspaces/{_session.WorkspaceId:N}/{entity.RestPath}?pageSize=200";
    }

    private static bool IsRemovalAction(string action)
    {
        return action.Contains("archived", StringComparison.OrdinalIgnoreCase)
            || action.Contains("deleted", StringComparison.OrdinalIgnoreCase)
            || action.Contains("removed", StringComparison.OrdinalIgnoreCase);
    }

    private void PublishStatus(string status)
    {
        SyncStatusChanged?.Invoke(status);
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

    private sealed record SnapshotEntity(string ServerEntityType, string CacheEntityType, string RestPath);

    private sealed class SyncState
    {
        public long LastSequence { get; set; }
        public DateTime? LastAttemptAtUtc { get; set; }
        public DateTime? LastSuccessfulSyncAtUtc { get; set; }
        public DateTime? LastFullResyncAtUtc { get; set; }
        public int ConsecutiveFailureCount { get; set; }
        public string? LastError { get; set; }
    }
}
