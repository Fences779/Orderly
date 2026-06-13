using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orderly.Core.Models;
using Orderly.Core.Services;
using Orderly.Infrastructure.Hotkeys;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    private Func<string, string, bool>? _tryApplyRuntimeHotkeys;
    private Func<string, string, bool>? _trySendDesktopNotification;

    [ObservableProperty]
    private string mainWindowHotkeyInput = "Ctrl+Alt+O";

    [ObservableProperty]
    private string floatingWindowHotkeyInput = "Ctrl+Alt+R";

    [ObservableProperty]
    private string globalSearchHotkeyInput = "Ctrl+Alt+F";

    [ObservableProperty]
    private string todayWorkbenchHotkeyInput = "Ctrl+Alt+W";

    [ObservableProperty]
    private string copyOrderSummaryHotkeyInput = "Ctrl+Shift+C";

    [ObservableProperty]
    private string openProductionSheetHotkeyInput = "Ctrl+Shift+P";

    [ObservableProperty]
    private string markOrderExceptionHotkeyInput = "Ctrl+Shift+E";

    [ObservableProperty]
    private string advanceFulfillmentHotkeyInput = "Ctrl+Shift+N";

    [ObservableProperty]
    private string openCustomerProfileHotkeyInput = "Ctrl+Shift+F";

    [ObservableProperty]
    private string newCustomerNoteHotkeyInput = "Ctrl+Shift+M";

    [ObservableProperty]
    private string copyCustomerPreferenceSummaryHotkeyInput = "Ctrl+Shift+Y";

    [ObservableProperty]
    private bool notifyNewOrderInput = true;

    [ObservableProperty]
    private bool notifyExceptionOrderInput = true;

    [ObservableProperty]
    private bool notifyOverdueUnhandledInput = true;

    [ObservableProperty]
    private bool notifySyncFailedInput = true;

    [ObservableProperty]
    private int notifyPaidUnconfirmedHoursInput = 24;

    [ObservableProperty]
    private int notifyPendingProductionHoursInput = 24;

    [ObservableProperty]
    private int notifyPendingShipmentHoursInput = 48;

    [ObservableProperty]
    private bool notifyMissingAddressInput = true;

    [ObservableProperty]
    private string notifyDoNotDisturbRangeInput = DefaultDoNotDisturbRange;

    [ObservableProperty]
    private bool notifyHighPriorityOnlyInput;

    [ObservableProperty]
    private string hotkeyValidationStatusText = "保存时会自动校验格式与重复绑定。";

    [ObservableProperty]
    private string hotkeyRuntimeStatusText = "仅主窗口/悬浮窗快捷键支持立即重载，其余动作待接入。";

    [ObservableProperty]
    private string notificationServiceStatusText = "通知服务未接入。";

    [ObservableProperty]
    private string notificationStrategyStatusText = "策略已保存，待提醒调度接入。";

    [ObservableProperty]
    private string notificationTestStatusText = "未执行测试通知。";

    public bool IsDesktopNotificationTestAvailable => _trySendDesktopNotification is not null;

    public void ConfigureSettingsRuntimeHooks(
        Func<string, string, bool>? tryApplyRuntimeHotkeys,
        Func<string, string, bool>? trySendDesktopNotification)
    {
        _tryApplyRuntimeHotkeys = tryApplyRuntimeHotkeys;
        _trySendDesktopNotification = trySendDesktopNotification;
        OnPropertyChanged(nameof(IsDesktopNotificationTestAvailable));
        RefreshNotificationSettingsRuntimeStatus();

        // 同步将运行态委托接缝转发给已抽出的设置页 ViewModel（任务 13.4，设计 §8.4.1）：
        // SettingsViewModel 经委托接缝调用 App 壳层能力，无需反向依赖 MainViewModel / App。
        Settings.ConfigureSettingsRuntimeHooks(tryApplyRuntimeHotkeys, trySendDesktopNotification);
    }

    [RelayCommand]
    private void TestDesktopNotification()
    {
        if (IsBusy)
        {
            return;
        }

        if (_trySendDesktopNotification is null)
        {
            NotificationTestStatusText = "通知服务未接入，无法发送测试通知。";
            SettingsStatusMessage = "通知服务未接入，当前仅保存策略。";
            return;
        }

        var sent = _trySendDesktopNotification("Orderly 通知测试", "这是设置页发出的测试通知。");
        NotificationTestStatusText = sent
            ? "测试通知已触发（托盘气泡）。"
            : "通知服务不可用，未发送测试通知。";
        SettingsStatusMessage = sent
            ? "通知测试已触发。"
            : "通知服务不可用，未发送测试通知。";
    }

    private bool TryApplyRuntimeHotkeysBeforeSave(AppPreferences previous, AppPreferences current, out string status)
    {
        var hasMainOrFloatingChanges =
            !string.Equals(previous.MainHotkey, current.MainHotkey, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(previous.FloatingHotkey, current.FloatingHotkey, StringComparison.OrdinalIgnoreCase);
        if (!hasMainOrFloatingChanges)
        {
            HotkeyRuntimeStatusText = "主窗口/悬浮窗快捷键无变更；其余快捷键策略已保存，待接入。";
            status = "主窗口/悬浮窗快捷键无变更；";
            return true;
        }

        if (_tryApplyRuntimeHotkeys is null)
        {
            HotkeyRuntimeStatusText = "主窗口/悬浮窗快捷键已保存，重启后生效；其余快捷键待接入。";
            status = "主窗口/悬浮窗快捷键已保存，重启后生效；";
            return true;
        }

        var applied = _tryApplyRuntimeHotkeys(current.MainHotkey, current.FloatingHotkey);
        if (applied)
        {
            HotkeyRuntimeStatusText = "主窗口/悬浮窗快捷键已立即生效；其余快捷键待接入。";
            status = "主窗口/悬浮窗快捷键已立即生效；";
            return true;
        }

        HotkeyRuntimeStatusText = "主窗口/悬浮窗快捷键被系统或其它应用占用，未保存；其余快捷键待接入。";
        status = "主窗口/悬浮窗快捷键被系统或其它应用占用，未保存。";
        return false;
    }

    private void RollbackRuntimeHotkeys(AppPreferences previous, AppPreferences current)
    {
        var hasMainOrFloatingChanges =
            !string.Equals(previous.MainHotkey, current.MainHotkey, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(previous.FloatingHotkey, current.FloatingHotkey, StringComparison.OrdinalIgnoreCase);
        if (hasMainOrFloatingChanges && _tryApplyRuntimeHotkeys is not null)
        {
            _ = _tryApplyRuntimeHotkeys(previous.MainHotkey, previous.FloatingHotkey);
        }
    }

    private void RefreshNotificationSettingsRuntimeStatus()
    {
        NotificationServiceStatusText = IsDesktopNotificationTestAvailable
            ? "托盘通知服务已接入，可执行测试通知。"
            : "通知服务未接入，当前仅保存提醒策略。";
        NotificationStrategyStatusText = "提醒策略已保存，待提醒调度接入。";
    }

    private IReadOnlyList<HotkeyValidationItem> BuildHotkeyValidationItems()
    {
        return
        [
            new("打开 / 隐藏主窗口", MainWindowHotkeyInput),
            new("显示 / 隐藏悬浮窗", FloatingWindowHotkeyInput),
            new("快速搜索订单", GlobalSearchHotkeyInput),
            new("打开今日工作台", TodayWorkbenchHotkeyInput),
            new("复制当前订单摘要", CopyOrderSummaryHotkeyInput),
            new("打开制作单", OpenProductionSheetHotkeyInput),
            new("标记异常", MarkOrderExceptionHotkeyInput),
            new("推进履约状态", AdvanceFulfillmentHotkeyInput),
            new("打开客户档案", OpenCustomerProfileHotkeyInput),
            new("新建客户备注", NewCustomerNoteHotkeyInput),
            new("复制客户偏好摘要", CopyCustomerPreferenceSummaryHotkeyInput)
        ];
    }

    private static string NormalizeHotkeyInput(string? value, string fallback)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return candidate;
    }

    private static string NormalizeHotkeyForDuplicate(string hotkey)
    {
        return HotkeyTextValidator.TryNormalizeForDuplicate(hotkey, out var normalized)
            ? normalized
            : string.Empty;
    }

    private static bool TryNormalizeDoNotDisturbRange(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var parts = value.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        if (!TryParseHourMinute(parts[0], out var start) || !TryParseHourMinute(parts[1], out var end))
        {
            return false;
        }

        if (start == end)
        {
            return false;
        }

        normalized = $"{start:hh\\:mm}-{end:hh\\:mm}";
        return true;
    }

    private static bool TryParseHourMinute(string value, out TimeSpan result)
    {
        return TimeSpan.TryParseExact(value, "hh\\:mm", CultureInfo.InvariantCulture, out result)
            && result >= TimeSpan.Zero
            && result < TimeSpan.FromDays(1);
    }

    private sealed record HotkeyValidationItem(string Label, string Value);
}
