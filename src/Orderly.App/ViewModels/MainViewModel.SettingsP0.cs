using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using Orderly.Core.Models;
using Orderly.Data.Sqlite;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    private const string SnSyncEntityType = "string-narration-orders";
    private const int SnSyncEntityId = 1;
    private static readonly HashSet<string> ImmediateAutoSaveSettingsInputs = new(StringComparer.Ordinal)
    {
        nameof(StartupDefaultSectionInput),
        nameof(StartWithWindowsInput),
        nameof(ShowFloatingWindowOnStartupInput),
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
        nameof(SnOrderSyncEnabledInput),
        nameof(SnSyncModeInput),
        nameof(SnSyncFrequencyInput),
        nameof(MaskPhoneByDefaultInput),
        nameof(MaskAddressByDefaultInput),
        nameof(IncludeSensitiveInExportInput),
        nameof(MaskOrderSummaryOnCopyInput),
        nameof(OperationLogEnabledInput),
        nameof(DebugModeEnabledInput),
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

    private bool _isApplyingSettingsInputs;
    private bool _isRunningSettingsAutoSave;
    private bool _hasQueuedSettingsAutoSave;
    private bool _hasAppliedStartupSection;

    public ObservableCollection<string> StartupSectionOptions { get; } = new([SectionWorkbench, SectionFulfillment, SectionException]);
    public ObservableCollection<string> WindowModeOptions { get; } = new(["普通", "最大化"]);
    public ObservableCollection<string> FontPresetOptions { get; } = new(["小", "标准", "大"]);
    public ObservableCollection<string> ThemeModeOptions { get; } = new(["浅色", "深色", "跟随系统"]);
    public ObservableCollection<string> AccentColorOptions { get; } = new(["默认绿", "茶金", "雾蓝"]);
    public ObservableCollection<string> AutoBackupFrequencyOptions { get; } = new(["手动", "每日", "每周"]);
    public ObservableCollection<string> SnSyncModeOptions { get; } = new(["手动", "定时"]);
    public ObservableCollection<string> SnSyncFrequencyOptions { get; } = new(["每30分钟", "每1小时", "每6小时", "每日"]);

    [ObservableProperty]
    private string startupDefaultSectionInput = SectionWorkbench;

    [ObservableProperty]
    private bool rememberLastSectionInput;

    [ObservableProperty]
    private string lastSectionInput = SectionWorkbench;

    [ObservableProperty]
    private bool startWithWindowsInput;

    [ObservableProperty]
    private bool showFloatingWindowOnStartupInput;

    [ObservableProperty]
    private bool startMinimizedToTrayInput;

    [ObservableProperty]
    private bool rememberWindowBoundsInput;

    [ObservableProperty]
    private string defaultWindowModeInput = "普通";

    [ObservableProperty]
    private bool sidebarDefaultExpandedInput = true;

    [ObservableProperty]
    private string fontSizePresetInput = "标准";

    [ObservableProperty]
    private bool showWindowsScaleHintInput = true;

    [ObservableProperty]
    private string themeModeInput = "浅色";

    [ObservableProperty]
    private string accentColorInput = "默认绿";

    [ObservableProperty]
    private bool enableLightAnimationInput;

    [ObservableProperty]
    private string backupDirectoryInput = BuildDefaultBackupDirectory();

    [ObservableProperty]
    private bool autoBackupEnabledInput;

    [ObservableProperty]
    private string autoBackupFrequencyInput = "手动";

    [ObservableProperty]
    private int backupRetentionCountInput = 10;

    [ObservableProperty]
    private bool snOrderSyncEnabledInput;

    [ObservableProperty]
    private string snSyncModeInput = "手动";

    [ObservableProperty]
    private string snSyncFrequencyInput = "每6小时";

    [ObservableProperty]
    private bool maskPhoneByDefaultInput = true;

    [ObservableProperty]
    private bool maskAddressByDefaultInput = true;

    [ObservableProperty]
    private bool includeSensitiveInExportInput;

    [ObservableProperty]
    private bool maskOrderSummaryOnCopyInput = true;

    [ObservableProperty]
    private bool operationLogEnabledInput = true;

    [ObservableProperty]
    private int operationLogRetentionDaysInput = 180;

    [ObservableProperty]
    private bool debugModeEnabledInput;

    [ObservableProperty]
    private string settingsStatusMessage = "设置未保存";

    [ObservableProperty]
    private string databaseSizeText = "未知";

    [ObservableProperty]
    private string databaseHealthStatusText = "未检查";

    [ObservableProperty]
    private string databaseHealthDetailText = "请点击“数据完整性检查”。";

    [ObservableProperty]
    private string databaseEncryptionStatusText = "未启用全库加密（文件层明文）；敏感字段通过会话密钥加密列保护。";

    [ObservableProperty]
    private string backupEncryptionStatusText = "本地备份为 JSON 文件，未加密。";

    [ObservableProperty]
    private string localAccessProtectionStatusText = "未登录";

    [ObservableProperty]
    private string appVersionText = "未知";

    [ObservableProperty]
    private string appBuildTimeText = "未记录";

    [ObservableProperty]
    private string runtimeEnvironmentText = "未加载";

    [ObservableProperty]
    private string updateCheckStatusText = "未接入更新服务";

    [ObservableProperty]
    private string snCloudEnvironmentIdText = "未配置";

    [ObservableProperty]
    private string snConnectionStatusText = "未检查";

    [ObservableProperty]
    private string snLastConnectionTimeText = "未检查";

    [ObservableProperty]
    private string snLastConnectionResultText = "未检查";

    [ObservableProperty]
    private string snLastSyncTimeText = "未执行";

    [ObservableProperty]
    private string snLastSyncResultText = "未执行";

    [ObservableProperty]
    private string snSyncLogSummaryText = "未加载";

    [ObservableProperty]
    private string snSyncFailureSummaryText = "暂无失败记录";

    [ObservableProperty]
    private string exportCapabilityStatusText = "导出订单/客户/操作日志与历史导入待接入。";

    [ObservableProperty]
    private string qaToolsStatusText = "仅开发/QA 环境可用。";

    public bool IsStartWithWindowsUnavailable => StartWithWindowsInput;
    public bool CanResetLocalTestState => false;

    public string EffectiveBackupDirectory => ResolveBackupDirectory(BackupDirectoryInput);

    public string DataDirectoryPath
    {
        get
        {
            try
            {
                return Path.GetDirectoryName(DatabasePath) ?? DatabasePath;
            }
            catch (Exception)
            {
                return DatabasePath;
            }
        }
    }

    partial void OnStartWithWindowsInputChanged(bool value)
    {
        OnPropertyChanged(nameof(IsStartWithWindowsUnavailable));
    }

    partial void OnBackupDirectoryInputChanged(string value)
    {
        OnPropertyChanged(nameof(EffectiveBackupDirectory));
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        HandleImmediateSettingsAutoSave(e.PropertyName);
    }

    internal void CommitDeferredSettingsAutoSave()
    {
        QueueSettingsAutoSave();
    }

    internal void ReportDeferredSettingsAutoSaveValidationError(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        SettingsStatusMessage = message;
        StatusMessage = message;
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

        _ = ProcessQueuedSettingsAutoSaveAsync();
    }

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

                while (!_isApplyingSettingsInputs && IsBusy)
                {
                    await Task.Delay(50);
                }

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
                _ = ProcessQueuedSettingsAutoSaveAsync();
            }
        }
    }

    partial void OnSelectedSectionChanged(string value)
    {
        var normalizedSection = NormalizeSection(value);
        if (!string.Equals(normalizedSection, value, StringComparison.Ordinal))
        {
            SelectedSection = normalizedSection;
            return;
        }

        if (string.Equals(value, SectionMe, StringComparison.Ordinal))
        {
            _ = RefreshManagedAccountsAsync();
        }

        if (string.Equals(value, SectionFulfillment, StringComparison.Ordinal))
        {
            // 不再自动默认选中第一个卡片
            // EnsureStringNarrationDetailSelection();
        }

        if (_isApplyingSettingsInputs || !RememberLastSectionInput)
        {
            if (string.Equals(value, SectionFulfillment, StringComparison.Ordinal) && StringNarrationOrders.Count == 0 && !IsStringNarrationBusy)
            {
                _ = LoadStringNarrationOrdersAsync();
            }
            else if (string.Equals(value, SectionException, StringComparison.Ordinal) && !IsExceptionOrdersBusy)
            {
                _ = LoadExceptionOrdersAsync();
            }

            return;
        }

        if (!StartupSectionOptions.Contains(value))
        {
            if (string.Equals(value, SectionFulfillment, StringComparison.Ordinal) && StringNarrationOrders.Count == 0 && !IsStringNarrationBusy)
            {
                _ = LoadStringNarrationOrdersAsync();
            }
            else if (string.Equals(value, SectionException, StringComparison.Ordinal) && !IsExceptionOrdersBusy)
            {
                _ = LoadExceptionOrdersAsync();
            }

            return;
        }

        LastSectionInput = value;
        _ = PersistLastSectionAsync(value);

        if (string.Equals(value, SectionFulfillment, StringComparison.Ordinal) && StringNarrationOrders.Count == 0 && !IsStringNarrationBusy)
        {
            _ = LoadStringNarrationOrdersAsync();
        }
        else if (string.Equals(value, SectionException, StringComparison.Ordinal) && !IsExceptionOrdersBusy)
        {
            _ = LoadExceptionOrdersAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanSaveP0Settings))]
    private async Task SaveP0SettingsAsync()
    {
        var validationError = ValidateP1SettingsInputs();
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            SettingsStatusMessage = validationError;
            StatusMessage = validationError;
            return;
        }

        await ExecuteSaveActionAsync(
            busyMessage: "正在保存设置...",
            successMessage: "设置已保存",
            errorTitle: "保存设置失败",
            errorStatusPrefix: "保存设置失败",
            action: async () =>
            {
                var previous = Preferences;
                var normalized = BuildPreferencesFromInputs();
                if (!TryApplyRuntimeHotkeysBeforeSave(previous, normalized, out var hotkeyStatus))
                {
                    SettingsStatusMessage = hotkeyStatus;
                    StatusMessage = hotkeyStatus;
                    throw new InvalidOperationException(hotkeyStatus);
                }

                try
                {
                    await _settingRepository.SavePreferencesAsync(normalized);
                }
                catch
                {
                    RollbackRuntimeHotkeys(previous, normalized);
                    throw;
                }

                Preferences = normalized;
                ApplySettingsInputsFromPreferences(normalized);
                SettingsStatusMessage = $"设置已保存；{hotkeyStatus} AI/通知策略已保存，待链路接入。";
                await RefreshSnSyncStatusAsync();
                RefreshAiSettingsRuntimeStatus();
                RefreshNotificationSettingsRuntimeStatus();
            });
    }

    private bool CanSaveP0Settings()
    {
        return !IsBusy;
    }

    private async Task PersistLastSectionAsync(string section)
    {
        try
        {
            await _settingRepository.UpsertAsync(AppSettingKeys.LastSection, section);
        }
        catch
        {
            // Last section persistence is best-effort and should not break navigation.
        }
    }

    private void ApplyStartupSectionPreferenceIfNeeded()
    {
        if (_hasAppliedStartupSection)
        {
            return;
        }

        _hasAppliedStartupSection = true;
        var targetSection = RememberLastSectionInput
            ? NormalizeOption(LastSectionInput, StartupSectionOptions, StartupDefaultSectionInput)
            : NormalizeOption(StartupDefaultSectionInput, StartupSectionOptions, SectionWorkbench);

        if (!string.Equals(SelectedSection, targetSection, StringComparison.Ordinal))
        {
            SelectedSection = targetSection;
        }
    }
}
