using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orderly.Core.Models;
using Orderly.Core.Services;
using Orderly.Data.Services;
using Orderly.Infrastructure.Hotkeys;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    private const int MinAiTimeoutSeconds = 5;
    private const int MaxAiTimeoutSeconds = 120;
    private const int MinReminderHours = 1;
    private const int MaxReminderHours = 168;
    private const string DefaultDoNotDisturbRange = "22:00-08:00";

    private Func<string, string, bool>? _tryApplyRuntimeHotkeys;
    private Func<string, string, bool>? _trySendDesktopNotification;

    public ObservableCollection<string> AiReplyToneOptions { get; } = new(["简洁", "温和", "专业"]);
    public ObservableCollection<string> AiReplyLengthOptions { get; } = new(["短", "标准", "详细"]);

    [ObservableProperty]
    private bool enableAiAssistantInput;

    [ObservableProperty]
    private bool allowAiOrderContextInput;

    [ObservableProperty]
    private bool allowAiCustomerProfileContextInput;

    [ObservableProperty]
    private string defaultAiModelInput = string.Empty;

    [ObservableProperty]
    private int aiTimeoutSecondsInput = 15;

    [ObservableProperty]
    private bool aiAutoRedactBeforeSendInput = true;

    [ObservableProperty]
    private bool aiBlockPhoneInput = true;

    [ObservableProperty]
    private bool aiBlockFullAddressInput = true;

    [ObservableProperty]
    private bool aiBlockPaymentTransactionIdInput = true;

    [ObservableProperty]
    private string aiReplyToneInput = "简洁";

    [ObservableProperty]
    private string aiReplyLengthInput = "标准";

    [ObservableProperty]
    private bool aiAutoGenerateOrderSummaryInput;

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
    private string aiRuntimeProviderText = "local";

    [ObservableProperty]
    private string aiRuntimeModelText = "未配置";

    [ObservableProperty]
    private string aiApiKeyStatusText = "未配置";

    [ObservableProperty]
    private string aiEndpointStatusText = "未配置";

    [ObservableProperty]
    private string aiConnectionCheckStatusText = "未检查";

    [ObservableProperty]
    private string aiModelPreferenceStatusText = "默认模型偏好已保存，待 AI provider 接入。";

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
    }

    [RelayCommand]
    private void CheckAiConfiguration()
    {
        if (IsBusy)
        {
            return;
        }

        RefreshAiSettingsRuntimeStatus();
        var options = AiProviderOptions.FromEnvironment();
        var provider = options.RequestedProvider;
        var errors = new List<string>();

        if (options.TimeoutSeconds < MinAiTimeoutSeconds || options.TimeoutSeconds > MaxAiTimeoutSeconds)
        {
            errors.Add($"ORDERLY_AI_TIMEOUT_SECONDS 超出范围（{MinAiTimeoutSeconds}-{MaxAiTimeoutSeconds} 秒）");
        }

        switch (provider)
        {
            case AiProviderOptions.LocalProviderName:
                AiConnectionCheckStatusText = "配置检查通过：当前 provider=local，离线模式可运行（未发起真实请求）。";
                SettingsStatusMessage = "AI 配置检查已完成（仅配置检查）。";
                return;
            case AiProviderOptions.OpenAiCompatibleProviderName:
                if (string.IsNullOrWhiteSpace(options.BaseUrl))
                {
                    errors.Add("ORDERLY_AI_BASE_URL 未配置");
                }

                if (string.IsNullOrWhiteSpace(options.ApiKey))
                {
                    errors.Add("ORDERLY_AI_API_KEY 未配置");
                }

                break;
            case AiProviderOptions.DeepSeekProviderName:
                if (string.IsNullOrWhiteSpace(options.ApiKey))
                {
                    errors.Add("DEEPSEEK_API_KEY 未配置");
                }

                break;
            default:
                errors.Add($"未知 provider：{provider}");
                break;
        }

        if (string.IsNullOrWhiteSpace(options.Model))
        {
            if (string.IsNullOrWhiteSpace(DefaultAiModelInput))
            {
                errors.Add("ORDERLY_AI_MODEL 未配置");
            }
            else
            {
                errors.Add("运行时 ORDERLY_AI_MODEL 未配置（已保存默认模型偏好，待 provider 接入）");
            }
        }

        if (errors.Count == 0)
        {
            AiConnectionCheckStatusText = "配置检查通过：provider / endpoint / key / model / timeout 已满足最低配置（未发起真实请求）。";
            SettingsStatusMessage = "AI 配置检查已完成（仅配置检查）。";
            return;
        }

        AiConnectionCheckStatusText = $"配置不完整：{string.Join("；", errors)}。";
        SettingsStatusMessage = "AI 配置检查发现缺项。";
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

    private string? ValidateP1SettingsInputs()
    {
        if (AiTimeoutSecondsInput < MinAiTimeoutSeconds || AiTimeoutSecondsInput > MaxAiTimeoutSeconds)
        {
            return $"AI 超时时间需在 {MinAiTimeoutSeconds}-{MaxAiTimeoutSeconds} 秒。";
        }

        if (NotifyPaidUnconfirmedHoursInput < MinReminderHours || NotifyPaidUnconfirmedHoursInput > MaxReminderHours)
        {
            return $"“已支付未确认”阈值需在 {MinReminderHours}-{MaxReminderHours} 小时。";
        }

        if (NotifyPendingProductionHoursInput < MinReminderHours || NotifyPendingProductionHoursInput > MaxReminderHours)
        {
            return $"“待制作未推进”阈值需在 {MinReminderHours}-{MaxReminderHours} 小时。";
        }

        if (NotifyPendingShipmentHoursInput < MinReminderHours || NotifyPendingShipmentHoursInput > MaxReminderHours)
        {
            return $"“待发货未发货”阈值需在 {MinReminderHours}-{MaxReminderHours} 小时。";
        }

        if (!TryNormalizeDoNotDisturbRange(NotifyDoNotDisturbRangeInput, out _))
        {
            return "免打扰时间段格式无效，需为 HH:mm-HH:mm 或留空。";
        }

        var hotkeys = BuildHotkeyValidationItems();
        foreach (var item in hotkeys)
        {
            if (string.IsNullOrWhiteSpace(item.Value))
            {
                return $"{item.Label}不能为空。";
            }

            if (!GlobalHotkeyService.IsValidHotkey(item.Value))
            {
                return $"{item.Label}格式无效，示例：Ctrl+Alt+O。";
            }
        }

        var duplicates = hotkeys
            .GroupBy(item => NormalizeHotkeyForDuplicate(item.Value), StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToList();
        if (duplicates.Count > 0)
        {
            var first = duplicates[0];
            var labels = string.Join("、", first.Select(item => item.Label));
            return $"快捷键重复：{labels} 使用同一绑定 {first.First().Value}。";
        }

        HotkeyValidationStatusText = "快捷键校验通过。";
        return null;
    }

    private AppPreferences ApplyP1InputsToPreferences(AppPreferences preferences)
    {
        preferences.MainHotkey = NormalizeHotkeyInput(MainWindowHotkeyInput, "Ctrl+Alt+O");
        preferences.FloatingHotkey = NormalizeHotkeyInput(FloatingWindowHotkeyInput, "Ctrl+Alt+R");
        preferences.GlobalSearchHotkey = NormalizeHotkeyInput(GlobalSearchHotkeyInput, "Ctrl+Alt+F");
        preferences.TodayWorkbenchHotkey = NormalizeHotkeyInput(TodayWorkbenchHotkeyInput, "Ctrl+Alt+W");
        preferences.CopyOrderSummaryHotkey = NormalizeHotkeyInput(CopyOrderSummaryHotkeyInput, "Ctrl+Shift+C");
        preferences.OpenProductionSheetHotkey = NormalizeHotkeyInput(OpenProductionSheetHotkeyInput, "Ctrl+Shift+P");
        preferences.MarkOrderExceptionHotkey = NormalizeHotkeyInput(MarkOrderExceptionHotkeyInput, "Ctrl+Shift+E");
        preferences.AdvanceFulfillmentHotkey = NormalizeHotkeyInput(AdvanceFulfillmentHotkeyInput, "Ctrl+Shift+N");
        preferences.OpenCustomerProfileHotkey = NormalizeHotkeyInput(OpenCustomerProfileHotkeyInput, "Ctrl+Shift+F");
        preferences.NewCustomerNoteHotkey = NormalizeHotkeyInput(NewCustomerNoteHotkeyInput, "Ctrl+Shift+M");
        preferences.CopyCustomerPreferenceSummaryHotkey = NormalizeHotkeyInput(CopyCustomerPreferenceSummaryHotkeyInput, "Ctrl+Shift+Y");

        preferences.AiAssistantEnabled = EnableAiAssistantInput;
        preferences.AiAllowOrderContext = AllowAiOrderContextInput;
        preferences.AiAllowCustomerProfileContext = AllowAiCustomerProfileContextInput;
        preferences.AiDefaultModel = (DefaultAiModelInput ?? string.Empty).Trim();
        preferences.AiTimeoutSeconds = Math.Clamp(AiTimeoutSecondsInput, MinAiTimeoutSeconds, MaxAiTimeoutSeconds);
        preferences.AiAutoRedactBeforeSend = AiAutoRedactBeforeSendInput;
        preferences.AiBlockPhone = AiBlockPhoneInput;
        preferences.AiBlockFullAddress = AiBlockFullAddressInput;
        preferences.AiBlockPaymentTransactionId = AiBlockPaymentTransactionIdInput;
        preferences.AiReplyTone = NormalizeOption(AiReplyToneInput, AiReplyToneOptions, "简洁");
        preferences.AiReplyLength = NormalizeOption(AiReplyLengthInput, AiReplyLengthOptions, "标准");
        preferences.AiAutoGenerateOrderSummary = AiAutoGenerateOrderSummaryInput;

        preferences.NotifyNewOrder = NotifyNewOrderInput;
        preferences.NotifyExceptionOrder = NotifyExceptionOrderInput;
        preferences.NotifyOverdueUnhandled = NotifyOverdueUnhandledInput;
        preferences.NotifySyncFailed = NotifySyncFailedInput;
        preferences.NotifyPaidUnconfirmedHours = Math.Clamp(NotifyPaidUnconfirmedHoursInput, MinReminderHours, MaxReminderHours);
        preferences.NotifyPendingProductionHours = Math.Clamp(NotifyPendingProductionHoursInput, MinReminderHours, MaxReminderHours);
        preferences.NotifyPendingShipmentHours = Math.Clamp(NotifyPendingShipmentHoursInput, MinReminderHours, MaxReminderHours);
        preferences.NotifyMissingAddress = NotifyMissingAddressInput;
        preferences.NotifyDoNotDisturbRange = TryNormalizeDoNotDisturbRange(NotifyDoNotDisturbRangeInput, out var dndRange)
            ? dndRange
            : DefaultDoNotDisturbRange;
        preferences.NotifyHighPriorityOnly = NotifyHighPriorityOnlyInput;

        return preferences;
    }

    private void ApplyP1InputsFromPreferences(AppPreferences preferences)
    {
        MainWindowHotkeyInput = NormalizeHotkeyInput(preferences.MainHotkey, "Ctrl+Alt+O");
        FloatingWindowHotkeyInput = NormalizeHotkeyInput(preferences.FloatingHotkey, "Ctrl+Alt+R");
        GlobalSearchHotkeyInput = NormalizeHotkeyInput(preferences.GlobalSearchHotkey, "Ctrl+Alt+F");
        TodayWorkbenchHotkeyInput = NormalizeHotkeyInput(preferences.TodayWorkbenchHotkey, "Ctrl+Alt+W");
        CopyOrderSummaryHotkeyInput = NormalizeHotkeyInput(preferences.CopyOrderSummaryHotkey, "Ctrl+Shift+C");
        OpenProductionSheetHotkeyInput = NormalizeHotkeyInput(preferences.OpenProductionSheetHotkey, "Ctrl+Shift+P");
        MarkOrderExceptionHotkeyInput = NormalizeHotkeyInput(preferences.MarkOrderExceptionHotkey, "Ctrl+Shift+E");
        AdvanceFulfillmentHotkeyInput = NormalizeHotkeyInput(preferences.AdvanceFulfillmentHotkey, "Ctrl+Shift+N");
        OpenCustomerProfileHotkeyInput = NormalizeHotkeyInput(preferences.OpenCustomerProfileHotkey, "Ctrl+Shift+F");
        NewCustomerNoteHotkeyInput = NormalizeHotkeyInput(preferences.NewCustomerNoteHotkey, "Ctrl+Shift+M");
        CopyCustomerPreferenceSummaryHotkeyInput = NormalizeHotkeyInput(preferences.CopyCustomerPreferenceSummaryHotkey, "Ctrl+Shift+Y");

        EnableAiAssistantInput = preferences.AiAssistantEnabled;
        AllowAiOrderContextInput = preferences.AiAllowOrderContext;
        AllowAiCustomerProfileContextInput = preferences.AiAllowCustomerProfileContext;

        var runtimeOptions = AiProviderOptions.FromEnvironment();
        DefaultAiModelInput = string.IsNullOrWhiteSpace(preferences.AiDefaultModel)
            ? runtimeOptions.Model
            : preferences.AiDefaultModel;
        AiTimeoutSecondsInput = Math.Clamp(preferences.AiTimeoutSeconds, MinAiTimeoutSeconds, MaxAiTimeoutSeconds);
        AiAutoRedactBeforeSendInput = preferences.AiAutoRedactBeforeSend;
        AiBlockPhoneInput = preferences.AiBlockPhone;
        AiBlockFullAddressInput = preferences.AiBlockFullAddress;
        AiBlockPaymentTransactionIdInput = preferences.AiBlockPaymentTransactionId;
        AiReplyToneInput = NormalizeOption(preferences.AiReplyTone, AiReplyToneOptions, "简洁");
        AiReplyLengthInput = NormalizeOption(preferences.AiReplyLength, AiReplyLengthOptions, "标准");
        AiAutoGenerateOrderSummaryInput = preferences.AiAutoGenerateOrderSummary;

        NotifyNewOrderInput = preferences.NotifyNewOrder;
        NotifyExceptionOrderInput = preferences.NotifyExceptionOrder;
        NotifyOverdueUnhandledInput = preferences.NotifyOverdueUnhandled;
        NotifySyncFailedInput = preferences.NotifySyncFailed;
        NotifyPaidUnconfirmedHoursInput = Math.Clamp(preferences.NotifyPaidUnconfirmedHours, MinReminderHours, MaxReminderHours);
        NotifyPendingProductionHoursInput = Math.Clamp(preferences.NotifyPendingProductionHours, MinReminderHours, MaxReminderHours);
        NotifyPendingShipmentHoursInput = Math.Clamp(preferences.NotifyPendingShipmentHours, MinReminderHours, MaxReminderHours);
        NotifyMissingAddressInput = preferences.NotifyMissingAddress;
        NotifyDoNotDisturbRangeInput = TryNormalizeDoNotDisturbRange(preferences.NotifyDoNotDisturbRange, out var dndRange)
            ? dndRange
            : DefaultDoNotDisturbRange;
        NotifyHighPriorityOnlyInput = preferences.NotifyHighPriorityOnly;

        RefreshAiSettingsRuntimeStatus();
        RefreshNotificationSettingsRuntimeStatus();
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

    private void RefreshAiSettingsRuntimeStatus()
    {
        var options = AiProviderOptions.FromEnvironment();
        AiRuntimeProviderText = string.IsNullOrWhiteSpace(options.RequestedProvider) ? "local" : options.RequestedProvider;
        AiRuntimeModelText = string.IsNullOrWhiteSpace(options.Model) ? "未配置" : options.Model;
        AiApiKeyStatusText = string.IsNullOrWhiteSpace(options.ApiKey) ? "未配置" : "已配置";
        AiEndpointStatusText = string.IsNullOrWhiteSpace(options.BaseUrl) ? "未配置" : "已配置";

        AiModelPreferenceStatusText = string.IsNullOrWhiteSpace(DefaultAiModelInput)
            ? "默认模型偏好未设置；若需固定模型可保存偏好（待 provider 接入）。"
            : string.IsNullOrWhiteSpace(options.Model)
                ? "默认模型偏好已保存，待 AI provider 链路消费。"
                : "运行时模型优先来自环境变量；默认模型偏好已保存，待 provider 接入。";
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
