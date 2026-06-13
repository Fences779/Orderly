using System;
using System.Collections.Generic;
using System.Linq;
using Orderly.Core.Models;
using Orderly.Data.Services;
using Orderly.Infrastructure.Hotkeys;

namespace Orderly.App.ViewModels;

/// <summary>
/// 「设置页」P1 偏好校验与 <c>*Input</c> ↔ <see cref="AppPreferences"/> 映射分部（任务 13.4，设计 §8.4 / §8.4.2）。
/// 自 <c>MainViewModel.SettingsP1.cs</c> 迁入的等价实现：AI 助手 / 通知提醒 / 快捷键三组 <c>*Input</c> 的边界校验
/// （<see cref="ValidateP1SettingsInputs"/>）与双向规范化映射（<see cref="ApplyP1InputsToPreferences"/> /
/// <see cref="ApplyP1InputsFromPreferences"/>）由 <see cref="SettingsViewModel"/> 承载。
///
/// <para><b>映射接管（设计 §8.4.4）</b>：本步起，<see cref="BuildPreferencesFromInputs"/> 不再从基线偏好原样保留
/// 这些 P1 字段（快捷键 / AI / 通知），改由本分部的 <see cref="ApplyP1InputsToPreferences"/> 以当前 <c>*Input</c>
/// 覆盖；<see cref="ApplySettingsInputsFromPreferences"/> 经 <see cref="ApplyP1InputsFromPreferences"/> 回填这些
/// <c>*Input</c>，完成完整映射，不再依赖基线偏好保留 P1 字段。</para>
/// </summary>
public partial class SettingsViewModel
{
    private const int MinAiTimeoutSeconds = 5;
    private const int MaxAiTimeoutSeconds = 120;
    private const int MinReminderHours = 1;
    private const int MaxReminderHours = 168;
    private const string DefaultDoNotDisturbRange = "22:00-08:00";

    /// <summary>
    /// 校验 P1（AI / 通知提醒 / 快捷键）<c>*Input</c> 的边界与一致性（自 SettingsP1.cs 迁入）。
    /// 通过返回 <c>null</c>，否则返回首个面向用户的中文校验文案。供保存流程在落盘前调用，
    /// 失败时由保存流程归类为 <c>SET-1002</c>（见 <c>SaveP0SettingsAsync</c>）。
    /// </summary>
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

    /// <summary>
    /// 以当前 P1 <c>*Input</c> 规范化覆盖 <paramref name="preferences"/> 的快捷键 / AI / 通知字段（自 SettingsP1.cs 迁入）。
    /// 由 <see cref="BuildPreferencesFromInputs"/> 在写入 P0 字段后调用，完成完整映射。
    /// </summary>
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

    /// <summary>
    /// 将 <paramref name="preferences"/> 的快捷键 / AI / 通知字段回填到 P1 <c>*Input</c>（自 SettingsP1.cs 迁入）。
    /// 由 <see cref="ApplySettingsInputsFromPreferences"/> 在抑制自动保存期间调用，并刷新 AI/通知运行态文案。
    /// </summary>
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
}
