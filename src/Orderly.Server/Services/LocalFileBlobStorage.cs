using Orderly.Server.Models;

namespace Orderly.Server.Services;

public sealed class LocalFileBlobStorage : IBlobStorage
{
    private readonly string _rootDirectory;

    public LocalFileBlobStorage(ServerOptions options)
    {
        _rootDirectory = Path.GetFullPath(options.LocalBlobDirectory);
        Directory.CreateDirectory(_rootDirectory);
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_rootDirectory);

    public async Task UploadAsync(string key, Stream stream, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(key);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var file = File.Create(path);
        await stream.CopyToAsync(file, cancellationToken);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var normalizedPrefix = NormalizeKey(prefix);
        if (!Directory.Exists(_rootDirectory))
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        var keys = Directory
            .EnumerateFiles(_rootDirectory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(_rootDirectory, path).Replace('\\', '/'))
            .Where(key => key.StartsWith(normalizedPrefix, StringComparison.Ordinal))
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(keys);
    }

    public Task<Stream?> DownloadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(key);
        Stream? stream = File.Exists(path) ? File.OpenRead(path) : null;
        return Task.FromResult(stream);
    }

    private string ResolvePath(string key)
    {
        var normalizedKey = NormalizeKey(key);
        var path = Path.GetFullPath(Path.Combine(_rootDirectory, normalizedKey.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(_rootDirectory, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Blob key is outside the local blob directory.");
        }

        return path;
    }

    private static string NormalizeKey(string key) =>
        key.Replace('\\', '/').TrimStart('/');
}
