using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Orderly.Infrastructure.Tray;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    public TrayIconService()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开主窗口", null, (_, _) => OpenMainRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("显示/隐藏悬浮窗", null, (_, _) => ToggleFloatingRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _notifyIcon = new NotifyIcon
        {
            Text = "Orderly 商家工作台",
            Icon = LoadTrayIcon(),
            ContextMenuStrip = menu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => OpenMainRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? OpenMainRequested;
    public event EventHandler? ToggleFloatingRequested;
    public event EventHandler? ExitRequested;

    public bool CanShowNotifications => _notifyIcon.Visible;

    public void ShowInfo(string title, string message, int timeoutMilliseconds = 3000)
    {
        if (!_notifyIcon.Visible)
        {
            return;
        }

        var normalizedTitle = string.IsNullOrWhiteSpace(title) ? "Orderly 通知" : title.Trim();
        var normalizedMessage = string.IsNullOrWhiteSpace(message) ? "暂无内容" : message.Trim();
        _notifyIcon.ShowBalloonTip(
            Math.Clamp(timeoutMilliseconds, 1000, 10000),
            normalizedTitle,
            normalizedMessage,
            ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private static Icon LoadTrayIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Brand", "Orderly Logo.ico");
            if (File.Exists(iconPath))
            {
                return new Icon(iconPath);
            }
        }
        catch
        {
            // Fall back to the system icon so tray initialization never blocks startup.
        }

        return SystemIcons.Application;
    }
}
