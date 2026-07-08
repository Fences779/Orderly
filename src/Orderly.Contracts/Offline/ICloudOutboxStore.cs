namespace Orderly.Contracts.Offline;

public interface ICloudOutboxStore
{
    Task AddAsync(CloudOutboxEntryDto entry, CancellationToken cancellationToken = default);
    Task<CloudOutboxEntryDto?> GetAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CloudOutboxEntryDto>> ListReadyAsync(DateTime utcNow, int limit = 100, CancellationToken cancellationToken = default);
    Task MarkFailedAsync(string id, string error, DateTime nextAttemptAtUtc, CancellationToken cancellationToken = default);
    Task MarkSubmittedAsync(string id, CancellationToken cancellationToken = default);
    Task RemoveAsync(string id, CancellationToken cancellationToken = default);
}
