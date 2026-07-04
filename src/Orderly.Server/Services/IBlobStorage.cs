namespace Orderly.Server.Services;

public interface IBlobStorage
{
    Task UploadAsync(string key, Stream stream, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken cancellationToken = default);
    Task<Stream?> DownloadAsync(string key, CancellationToken cancellationToken = default);
    bool IsEnabled { get; }
}
