using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orderly.Core.Models;
using Orderly.Core.Services;

namespace Orderly.App.ViewModels;

/// <summary>
/// 「设置页」快捷键与通知提醒分部（任务 13.4，设计 §8.4 / §8.4.2）。自
/// <c>MainViewModel.SettingsP1.HotkeysNotifications.cs</c> 迁入的等价实现：快捷键绑定 <c>*Input</c>（六大分类之
/// 「快捷键」）、通知提醒 <c>*Input</c>（六大分类之「通知提醒」）、运行态热键应用
/// （<c>TryApplyRuntimeHotkeysBeforeSave</c> / <c>RollbackRuntimeHotkeys</c>）与测试通知命令由
/// <see cref="SettingsViewModel"/> 承载。
///
/// <para><b>接缝（设计 §8.4.1 / §8.4.3）</b>：运行态热键应用与桌面通知发送需调用 App 壳层能力，经可注入的
/// <see cref="Func{T1,T2,TResult}"/> 委托接缝注入（<see cref="ConfigureSettingsRuntimeHooks"/>），
/// <b>不反向依赖 <see cref="MainViewModel"/> / App</b>（消除循环引用）。</para>
///
/// <para><b>共存说明（设计 §8.4.3）</b>：当前阶段 <see cref="MainViewModel"/> 仍承载 <c>SettingsView</c> 绑定与
/// 自身的快捷键/通知实现；本实现为 <see cref="SettingsViewModel"/> 建立的等价副本，待 DataContext 切换
/// （任务 21.1）后接管。</para>
/// </summary>
public partial class SettingsViewModel
{
    // App 壳层能力的委托接缝（设计 §8.4.1）：为 null 时退化为「已保存，重启后生效 / 服务未接入」语义。
    private Func<AppPreferences, bool>? _tryApplyRuntimeHotkeys;
    private Func<string, string, bool>? _trySendDesktopNotification;

    // ── 快捷键绑定 *Input（六大分类之「快捷键」，§8.4.2）──────────────────────────────

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

    // ── 通知提醒 *Input（六大分类之「通知提醒」，§8.4.2）──────────────────────────────

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

    // ── 运行态状态文案 ──────────────────────────────────────────────────────────────

    [ObservableProperty]
    private string hotkeyValidationStatusText = "保存时会自动校验格式与重复绑定。";

    [ObservableProperty]
    private string hotkeyRuntimeStatusText = "全部快捷键保存后会尝试立即重载。";

    [ObservableProperty]
    private string notificationServiceStatusText = "通知服务未接入。";

    [ObservableProperty]
    private string notificationStrategyStatusText = "提醒策略保存后会立即更新本地调度。";

    [ObservableProperty]
    private string notificationTestStatusText = "未执行测试通知。";

    /// <summary>桌面通知测试是否可用（依赖注入的通知发送接缝）。</summary>
    public bool IsDesktopNotificationTestAvailable => _trySendDesktopNotification is not null;

    /// <summary>
    /// 注入 App 壳层运行态能力的委托接缝（设计 §8.4.1）：运行态热键应用与桌面通知发送。
    /// 由集成接线（任务 21.1）经 <c>MainViewModel.ConfigureSettingsRuntimeHooks</c> 转发调用，
    /// 使 <see cref="SettingsViewModel"/> 无需反向依赖 App / <see cref="MainViewModel"/>。
    /// </summary>
    public void ConfigureSettingsRuntimeHooks(
        Func<AppPreferences, bool>? tryApplyRuntimeHotkeys,
        Func<string, string, bool>? trySendDesktopNotification)
    {
        _tryApplyRuntimeHotkeys = tryApplyRuntimeHotkeys;
        _trySendDesktopNotification = trySendDesktopNotification;
        OnPropertyChanged(nameof(IsDesktopNotificationTestAvailable));
        RefreshNotificationSettingsRuntimeStatus();
    }

    [RelayCommand]
    private void TestDesktopNotification()
    {
        if (_isSettingsActionRunning)
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

    /// <summary>
    /// 保存前的运行态热键应用（设计 §9.5 / Req 3.7）：仅当主窗口/悬浮窗快捷键发生变更且接缝可用时尝试立即生效。
    /// 应用失败时返回 <c>false</c> 并写入运行态文案，由保存流程归类为 <c>SET-1003</c>（见 <c>SaveP0SettingsAsync</c>）。
    /// </summary>
    private bool TryApplyRuntimeHotkeysBeforeSave(AppPreferences previous, AppPreferences current, out string status)
    {
        var hasHotkeyChanges = BuildHotkeyValues(previous)
            .Zip(BuildHotkeyValues(current), (oldValue, newValue) => !string.Equals(oldValue, newValue, StringComparison.OrdinalIgnoreCase))
            .Any(changed => changed);
        if (!hasHotkeyChanges)
        {
            HotkeyRuntimeStatusText = "快捷键无变更。";
            status = "快捷键无变更。";
            return true;
        }

        if (_tryApplyRuntimeHotkeys is null)
        {
            HotkeyRuntimeStatusText = "快捷键已保存，重启后生效。";
            status = "快捷键已保存，重启后生效。";
            return true;
        }

        var applied = _tryApplyRuntimeHotkeys(current);
        if (applied)
        {
            HotkeyRuntimeStatusText = "全部快捷键已立即生效。";
            status = "全部快捷键已立即生效。";
            return true;
        }

        HotkeyRuntimeStatusText = "快捷键被系统或其它应用占用，未保存。";
        status = "快捷键被系统或其它应用占用，未保存。";
        return false;
    }

    private void RollbackRuntimeHotkeys(AppPreferences previous, AppPreferences current)
    {
        var hasHotkeyChanges = BuildHotkeyValues(previous)
            .Zip(BuildHotkeyValues(current), (oldValue, newValue) => !string.Equals(oldValue, newValue, StringComparison.OrdinalIgnoreCase))
            .Any(changed => changed);
        if (hasHotkeyChanges && _tryApplyRuntimeHotkeys is not null)
        {
            _ = _tryApplyRuntimeHotkeys(previous);
        }
    }

    private void RefreshNotificationSettingsRuntimeStatus()
    {
        NotificationServiceStatusText = IsDesktopNotificationTestAvailable
            ? "托盘通知服务已接入，可执行测试通知。"
            : "通知服务未接入，当前仅保存提醒策略。";
        NotificationStrategyStatusText = IsDesktopNotificationTestAvailable
            ? "提醒策略已接入本地调度。"
            : "提醒策略已保存，通知服务可用后自动生效。";
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

    private static IReadOnlyList<string> BuildHotkeyValues(AppPreferences preferences)
    {
        return
        [
            preferences.MainHotkey,
            preferences.FloatingHotkey,
            preferences.GlobalSearchHotkey,
            preferences.TodayWorkbenchHotkey,
            preferences.CopyOrderSummaryHotkey,
            preferences.OpenProductionSheetHotkey,
            preferences.MarkOrderExceptionHotkey,
            preferences.AdvanceFulfillmentHotkey,
            preferences.OpenCustomerProfileHotkey,
            preferences.NewCustomerNoteHotkey,
            preferences.CopyCustomerPreferenceSummaryHotkey
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
