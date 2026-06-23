using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Orderly.App.ViewModels.Helpers;
using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

/// <summary>
/// 「设置页」自动保存引擎（任务 13.2，设计 §8.4 / §9.5 / Req 3.1、3.7）。自 <c>MainViewModel.SettingsP0.cs</c>
/// 迁入的等价引擎：P0 <c>*Input</c> 变更（非 <see cref="IsApplyingSettingsInputs"/> 期间）触发入队防抖保存，
/// 保持「即改即存」语义不变。
///
/// <para><b>渐进迁移边界（设计 §8.4.4）</b>：本步仅迁入 P0 自动保存链路（入队防抖 →
/// <c>ProcessQueuedSettingsAutoSaveAsync</c> → <c>SaveP0SettingsAsync</c> → 记录 <see cref="LastSaveOutcome"/>）。
/// P1 校验（<c>ValidateP1SettingsInputs</c>）与运行态热键应用（<c>TryApplyRuntimeHotkeysBeforeSave</c>）随
/// 任务 13.4 迁入；状态文案 <c>SettingsStatusMessage</c> 随任务 13.3 迁入；离开页结果聚合与导航闸门
/// （<c>FlushPendingAutoSaveAsync</c> / <c>ShowSaveResultToastOnLeave</c>）随任务 13.6 迁入。</para>
///
/// <para><b>共存说明（设计 §8.4.3）</b>：当前阶段 <see cref="MainViewModel"/> 仍承载 <c>SettingsView</c> 绑定与
/// 自身的自动保存引擎；本引擎为 <see cref="SettingsViewModel"/> 建立的等价实现，待 DataContext 切换（任务 21.1）
/// 后接管。</para>
/// </summary>
public partial class SettingsViewModel
{
    /// <summary>
    /// 触发即时自动保存的 P0 <c>*Input</c> 属性集合（自 SettingsP0.cs 迁入，仅含已迁移的 P0 字段）。
    /// 数值类字段（如 <c>BackupRetentionCountInput</c> / <c>OperationLogRetentionDaysInput</c>）不在即时集合内，
    /// 由失焦后经 <see cref="CommitDeferredSettingsAutoSave"/> 提交，保持与原引擎一致的防抖时序。
    /// </summary>
    private static readonly HashSet<string> ImmediateAutoSaveSettingsInputs = new(StringComparer.Ordinal)
    {
        nameof(StartupDefaultSectionInput),
        nameof(StartWithWindowsInput),
        nameof(FloatingBallEnabledInput),
        nameof(ShowFloatingWindowOnStartupInput),
        nameof(FloatingBallOpacityInput),
        nameof(StartMinimizedToTrayInput),
        nameof(RememberLastSectionInput),
        nameof(RememberWindowBoundsInput),
        nameof(DefaultWindowModeInput),
        nameof(SidebarDefaultExpandedInput),
        nameof(FontSizePresetInput),
        nameof(ShowWindowsScaleHintInput),
        nameof(ThemeModeInput),
        nameof(AccentColorInput),
        nameof(EnableLightAnimationInput),
        nameof(BackupDirectoryInput),
        nameof(AutoBackupEnabledInput),
        nameof(AutoBackupFrequencyInput),
        nameof(MaskPhoneByDefaultInput),
        nameof(MaskAddressByDefaultInput),
        nameof(IncludeSensitiveInExportInput),
        nameof(MaskOrderSummaryOnCopyInput),
        nameof(OperationLogEnabledInput),
        nameof(DebugModeEnabledInput),

        // ── P1 即时保存字段（AI 助手 / 通知提醒的开关与选项，任务 13.4 迁入）──────────
        // 数值/字符串类字段（AI 超时、提醒时限、默认模型、各快捷键、免打扰时段）不在即时集合内，
        // 由失焦后经 CommitDeferredSettingsAutoSave 提交，保持与原引擎一致的防抖时序。
        nameof(EnableAiAssistantInput),
        nameof(AllowAiOrderContextInput),
        nameof(AllowAiCustomerProfileContextInput),
        nameof(AiAutoRedactBeforeSendInput),
        nameof(AiBlockPhoneInput),
        nameof(AiBlockFullAddressInput),
        nameof(AiBlockPaymentTransactionIdInput),
        nameof(AiReplyToneInput),
        nameof(AiReplyLengthInput),
        nameof(AiAutoGenerateOrderSummaryInput),
        nameof(NotifyNewOrderInput),
        nameof(NotifyExceptionOrderInput),
        nameof(NotifyOverdueUnhandledInput),
        nameof(NotifySyncFailedInput),
        nameof(NotifyMissingAddressInput),
        nameof(NotifyHighPriorityOnlyInput)
    };

    private bool _isRunningSettingsAutoSave;
    private bool _hasQueuedSettingsAutoSave;

    /// <summary>
    /// 当前自动保存消费循环的句柄（设计 §9.5）。由 <see cref="QueueSettingsAutoSave"/> 启动时捕获，
    /// 供 <see cref="FlushPendingAutoSaveAsync"/> 在离开设置页时 await 至挂起/进行中的保存落盘。
    /// 为 <c>null</c> 表示当前无进行中的自动保存。
    /// </summary>
    private Task? _settingsAutoSaveLoop;

    /// <summary>
    /// 未迁移字段（全部快捷键、AI 助手、通知提醒、头像引用等）的基线来源，供
    /// <see cref="BuildPreferencesFromInputs"/> 在规范化时原样保留。由 <see cref="ApplySettingsInputsFromPreferences"/>
    /// 在回填时刷新，保存成功后以规范化结果回写，保证 P0 自动保存不丢失尚未迁移的偏好字段。
    /// </summary>
    private AppPreferences _baselinePreferences = new();

    /// <summary>
    /// 离开设置页时聚合的「最近一次保存结果」（设计 §9.5 / Req 3.7）。成功路径写入
    /// <see cref="SettingsSaveOutcome.FromSuccess"/>；异常路径写入 <see cref="SettingsSaveOutcome.FromFailure"/>
    /// （经 <see cref="SettingsSaveErrorCode.MapToStableErrorCode"/> 归类为稳定短码）。
    /// 离开页导航闸门与 Toast 消费（任务 13.6）据此决定放行/阻止并清空。
    /// </summary>
    public SettingsSaveOutcome? LastSaveOutcome { get; private set; }

    /// <summary>
    /// 监听 P0 <c>*Input</c> 属性变更并触发即时自动保存（自 SettingsP0.cs 迁入）。
    /// </summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        HandleImmediateSettingsAutoSave(e.PropertyName);
    }

    /// <summary>
    /// 提交延迟自动保存：用于数值/失焦类字段在编辑结束后入队一次保存（自 SettingsP0.cs 迁入）。
    /// </summary>
    internal void CommitDeferredSettingsAutoSave()
    {
        QueueSettingsAutoSave();
    }

    private void HandleImmediateSettingsAutoSave(string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName)
            || _isApplyingSettingsInputs
            || !ImmediateAutoSaveSettingsInputs.Contains(propertyName))
        {
            return;
        }

        QueueSettingsAutoSave();
    }

    private void QueueSettingsAutoSave()
    {
        if (_isApplyingSettingsInputs)
        {
            return;
        }

        _hasQueuedSettingsAutoSave = true;
        if (_isRunningSettingsAutoSave)
        {
            return;
        }

        _settingsAutoSaveLoop = ProcessQueuedSettingsAutoSaveAsync();
    }

    /// <summary>
    /// 防抖入队消费循环（自 SettingsP0.cs 迁入）：在单一执行者内串行消费排队的保存请求，
    /// 合并连续变更为最少次数的落盘，保持即改即存语义。
    /// </summary>
    private async Task ProcessQueuedSettingsAutoSaveAsync()
    {
        if (_isRunningSettingsAutoSave)
        {
            return;
        }

        _isRunningSettingsAutoSave = true;
        try
        {
            while (_hasQueuedSettingsAutoSave)
            {
                _hasQueuedSettingsAutoSave = false;

                if (_isApplyingSettingsInputs)
                {
                    continue;
                }

                await SaveP0SettingsAsync();
            }
        }
        finally
        {
            _isRunningSettingsAutoSave = false;
            if (_hasQueuedSettingsAutoSave && !_isApplyingSettingsInputs)
            {
                _settingsAutoSaveLoop = ProcessQueuedSettingsAutoSaveAsync();
            }
        }
    }

    /// <summary>
    /// 校验 P1 输入 → 规范化当前 <c>*Input</c> → 运行态热键应用 →
    /// <see cref="IAppSettingRepository.SavePreferencesAsync"/> → 记录 <see cref="LastSaveOutcome"/>
    /// （设计 §9.5 / Req 3.1、3.7）。成功路径写入成功结果并以规范化结果刷新基线偏好；失败路径写入失败结果
    /// （错误码经稳定映射归类：校验未过 → SET-1002、热键应用失败 → SET-1003、IO/DB → SET-1001、其它 →
    /// SET-1999），不向 UI 泄露异常类型名/堆栈。
    /// </summary>
    private async Task SaveP0SettingsAsync()
    {
        try
        {
            // P1 输入校验（任务 13.4）：未过则归类为 SET-1002，且不落盘。
            var validationError = ValidateP1SettingsInputs();
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                SettingsStatusMessage = validationError;
                throw new SettingsValidationException(validationError);
            }

            var previous = _baselinePreferences;
            var normalized = BuildPreferencesFromInputs(previous);

            // 运行态热键应用（任务 13.4）：主窗口/悬浮窗快捷键变更失败归类为 SET-1003，且不落盘。
            if (!TryApplyRuntimeHotkeysBeforeSave(previous, normalized, out var hotkeyStatus))
            {
                SettingsStatusMessage = hotkeyStatus;
                throw new SettingsHotkeyException(hotkeyStatus);
            }

            if (_settingRepository is not null)
            {
                try
                {
                    await _settingRepository.SavePreferencesAsync(normalized);
                }
                catch
                {
                    // 落盘失败：回滚已应用的运行态热键，避免运行态与持久态不一致。
                    RollbackRuntimeHotkeys(previous, normalized);
                    throw;
                }
            }

            // 落盘成功：以规范化结果作为新基线，回填以保持 *Input 与持久化值一致。
            _baselinePreferences = normalized;
            ApplySettingsInputsFromPreferences(normalized);
            _applyFloatingWindowRuntime?.Invoke(previous, normalized);
            RefreshAiSettingsRuntimeStatus();
            RefreshNotificationSettingsRuntimeStatus();

            LastSaveOutcome = SettingsSaveOutcome.FromSuccess(DateTime.Now);
        }
        catch (Exception ex)
        {
            // 异常路径：归类为稳定短码并记录失败结果，既有自动保存队列保留后续重试机会。
            LastSaveOutcome = SettingsSaveOutcome.FromFailure(ex, DateTime.Now);
        }
    }
}
