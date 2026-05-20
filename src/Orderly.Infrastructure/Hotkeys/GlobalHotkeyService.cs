using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Orderly.Infrastructure.Hotkeys;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    private readonly Dictionary<int, string> _registered = new();
    private HwndSource? _source;
    private IntPtr _handle;

    public event EventHandler<string>? HotkeyPressed;

    public void Attach(Window window)
    {
        _handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WndProc);
    }

    public static bool IsValidHotkey(string? hotkey)
    {
        return TryParse(hotkey ?? string.Empty, out _, out _);
    }

    public bool Register(int id, string hotkey)
    {
        if (_handle == IntPtr.Zero || !TryParse(hotkey, out var modifiers, out var key))
        {
            return false;
        }

        Unregister(id);
        var ok = RegisterHotKey(_handle, id, modifiers, key);
        if (ok)
        {
            _registered[id] = hotkey;
        }

        return ok;
    }

    public void Unregister(int id)
    {
        if (_registered.Remove(id) && _handle != IntPtr.Zero)
        {
            UnregisterHotKey(_handle, id);
        }
    }

    public void Dispose()
    {
        foreach (var id in _registered.Keys.ToArray())
        {
            Unregister(id);
        }

        _source?.RemoveHook(WndProc);
        _source = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            var id = wParam.ToInt32();
            if (_registered.TryGetValue(id, out var hotkey))
            {
                HotkeyPressed?.Invoke(this, hotkey);
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    private static bool TryParse(string value, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;

        var parts = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        foreach (var part in parts.Take(parts.Length - 1))
        {
            var current = part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => ModControl,
                "ALT" => ModAlt,
                "SHIFT" => ModShift,
                "WIN" or "WINDOWS" => ModWin,
                _ => 0u
            };

            if (current == 0)
            {
                return false;
            }

            modifiers |= current;
        }

        if (!Enum.TryParse<Key>(parts[^1], true, out var key))
        {
            return false;
        }

        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        return modifiers != 0 && virtualKey != 0;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
