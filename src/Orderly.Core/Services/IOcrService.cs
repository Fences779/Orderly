namespace Orderly.Core.Services;

public interface IOcrService
{
    Task<string> RecognizeAsync(string filePath, CancellationToken cancellationToken = default);
}
