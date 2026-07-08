using Aliyun.OSS;
using Orderly.Server.Models;

namespace Orderly.Server.Services;

public sealed class AliyunOssBlobStorage : IBlobStorage
{
    private readonly ServerOptions _options;
    private readonly OssClient? _client;

    public AliyunOssBlobStorage(ServerOptions options)
    {
        _options = options;
        if (options.OssEnabled)
        {
            _client = new OssClient(options.OssEndpoint, options.OssAccessKeyId, options.OssAccessKeySecret);
        }
    }

    public bool IsEnabled => _options.OssEnabled;

    public Task UploadAsync(string key, Stream stream, CancellationToken cancellationToken = default)
    {
        var client = GetClient();
        client.PutObject(_options.OssBucketName, key, stream);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var client = GetClient();
        client.DeleteObject(_options.OssBucketName, key);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var client = GetClient();
        var result = client.ListObjects(_options.OssBucketName, prefix);
        var keys = result.ObjectSummaries.Select(o => o.Key).ToList();
        return Task.FromResult<IReadOnlyList<string>>(keys);
    }

    public Task<Stream?> DownloadAsync(string key, CancellationToken cancellationToken = default)
    {
        var client = GetClient();
        var result = client.GetObject(_options.OssBucketName, key);
        return Task.FromResult<Stream?>(result.Content);
    }

    private OssClient GetClient()
    {
        if (!IsEnabled || _client is null)
        {
            throw new InvalidOperationException("OSS is not configured.");
        }

        return _client;
    }
}
