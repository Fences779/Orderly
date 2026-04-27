using Orderly.Core.Services;

namespace Orderly.Infrastructure.Services;

public sealed class DesktopClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        System.Windows.Forms.Clipboard.SetText(text ?? string.Empty);
    }
}
