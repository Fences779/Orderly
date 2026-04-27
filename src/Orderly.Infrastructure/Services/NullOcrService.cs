using Orderly.Core.Services;

namespace Orderly.Infrastructure.Services;

public sealed class NullOcrService : IOcrService
{
    public Task<string> RecognizeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(string.Empty);
    }
}
