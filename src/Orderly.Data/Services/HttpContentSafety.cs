using System.Text;

namespace Orderly.Data.Services;

internal static class HttpContentSafety
{
    public static async Task<string> ReadAsStringWithLimitAsync(
        HttpContent content,
        long maxBytes,
        string sourceDisplayName,
        CancellationToken cancellationToken,
        TimeSpan? readTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (content.Headers.ContentLength is long contentLength && contentLength > maxBytes)
        {
            throw new InvalidOperationException($"{sourceDisplayName} 返回内容过大。");
        }

        using var timeoutCancellation = readTimeout is { } timeout && timeout > TimeSpan.Zero
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        timeoutCancellation?.CancelAfter(readTimeout!.Value);
        var readCancellationToken = timeoutCancellation?.Token ?? cancellationToken;

        await using var stream = await content.ReadAsStreamAsync(readCancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        long totalBytes = 0;

        while (true)
        {
            var read = await stream.ReadAsync(chunk, readCancellationToken);
            if (read == 0)
            {
                break;
            }

            totalBytes += read;
            if (totalBytes > maxBytes)
            {
                throw new InvalidOperationException($"{sourceDisplayName} 返回内容过大。");
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
