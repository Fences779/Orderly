namespace Orderly.Remote.Auth;

public interface ICloudTokenStorage
{
    Task SaveAsync(string key, string value, CancellationToken cancellationToken = default);
    Task<string?> LoadAsync(string key, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
