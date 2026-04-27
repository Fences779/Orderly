namespace Orderly.Core.Models;

public sealed class AppPreferences
{
    public string MainHotkey { get; set; } = "Ctrl+Alt+O";
    public string FloatingHotkey { get; set; } = "Ctrl+Alt+R";
    public bool ShowFloatingWindowOnStartup { get; set; }
    public bool StartMinimizedToTray { get; set; }
}
