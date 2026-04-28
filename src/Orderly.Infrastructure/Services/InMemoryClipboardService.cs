using Orderly.Core.Services;

namespace Orderly.Infrastructure.Services;

public sealed class InMemoryClipboardService : IClipboardService
{
    public string LastText { get; private set; } = string.Empty;

    public void SetText(string text)
    {
        LastText = text ?? string.Empty;
    }
}
