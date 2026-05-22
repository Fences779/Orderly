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

        if (string.Equals(value, SectionFulfillment, StringComparison.Ordinal))
        {
            EnsureStringNarrationDetailSelection();
        }

        if (_isApplyingSettingsInputs || !RememberLastSectionInput)
        {
            if (string.Equals(value, SectionFulfillment, StringComparison.Ordinal) && StringNarrationOrders.Count == 0 && !IsStringNarrationBusy)
            {
                _ = LoadStringNarrationOrdersAsync();
            }
            else if (string.Equals(value, SectionException, StringComparison.Ordinal) && ExceptionOrders.Count == 0 && !IsExceptionOrdersBusy)
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
            else if (string.Equals(value, SectionException, StringComparison.Ordinal) && ExceptionOrders.Count == 0 && !IsExceptionOrdersBusy)
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
        else if (string.Equals(value, SectionException, StringComparison.Ordinal) && ExceptionOrders.Count == 0 && !IsExceptionOrdersBusy)
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

    [RelayCommand]
    private void OpenDataDirectory()
    {
        try
        {
            var directory = DataDirectoryPath;
            Directory.CreateDirectory(directory);
            OpenDirectory(directory);
            SettingsStatusMessage = $"已打开数据目录：{directory}";
        }
        catch (Exception ex)
        {
            SettingsStatusMessage = $"打开数据目录失败：{ex.Message}";
            ShowErrorMessage("打开数据目录失败", ex);
        }
    }

    [RelayCommand]
    private async Task RunDatabaseHealthCheckAsync()
    {
        await ExecuteSaveActionAsync(
            busyMessage: "正在检查数据库健康状态...",
            successMessage: "数据库健康检查已完成",
            errorTitle: "数据库健康检查失败",
            errorStatusPrefix: "数据库健康检查失败",
            action: async () =>
            {
                var (status, detail) = await CheckDatabaseHealthAsync();
                DatabaseHealthStatusText = status;
                DatabaseHealthDetailText = detail;
            });
    }

    [RelayCommand]
    private void ClearCacheFiles()
    {
        try
        {
            var cachePath = Path.Combine(DatabasePaths.GetAppRootPath(), "cache");
            if (!Directory.Exists(cachePath))
            {
                SettingsStatusMessage = "未发现缓存目录，无需清理。";
                return;
            }

            var removedFiles = 0;
            var removedDirectories = 0;
            foreach (var file in Directory.GetFiles(cachePath, "*", SearchOption.AllDirectories))
            {
                File.Delete(file);
                removedFiles++;
            }

            foreach (var directory in Directory.GetDirectories(cachePath, "*", SearchOption.AllDirectories).OrderByDescending(path => path.Length))
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                    removedDirectories++;
                }
            }

            SettingsStatusMessage = $"缓存清理完成：删除 {removedFiles} 个文件，清理 {removedDirectories} 个空目录。";
        }
        catch (Exception ex)
        {
            SettingsStatusMessage = $"清理缓存失败：{ex.Message}";
            ShowErrorMessage("清理缓存失败", ex);
        }
    }

    [RelayCommand]
    private async Task ClearExpiredOperationLogsAsync()
    {
        await ExecuteSaveActionAsync(
            busyMessage: "正在清理过期操作日志...",
            successMessage: "过期操作日志清理完成",
            errorTitle: "清理过期日志失败",
            errorStatusPrefix: "清理过期日志失败",
            action: async () =>
            {
                var retentionDays = Math.Clamp(OperationLogRetentionDaysInput, 7, 3650);
                var deletedCount = await _activityLogService.CleanupExpiredActivitiesAsync(retentionDays);
                SettingsStatusMessage = $"已清理 {deletedCount} 条超过 {retentionDays} 天的操作日志。";
            });
    }

    [RelayCommand]
    private void CopyDiagnosticInfo()
    {
        try
        {
            var diagnostics = BuildDiagnosticSummary();
            _clipboardService.SetText(diagnostics);
            SettingsStatusMessage = "诊断信息已复制（已脱敏，不包含 token/key 明文）。";
        }
        catch (Exception ex)
        {
            SettingsStatusMessage = $"复制诊断信息失败：{ex.Message}";
            ShowErrorMessage("复制诊断信息失败", ex);
        }
    }

    [RelayCommand]
    private async Task ExportFailureLogsAsync()
    {
        var logs = await GetFailureActivityLogsAsync();
        if (logs.Count == 0)
        {
            SettingsStatusMessage = "暂无可导出的失败类日志。";
            return;
        }

        var logDirectory = GetLogDirectoryPath();
        var dialog = new SaveFileDialog
        {
            Title = "导出失败类操作日志",
            Filter = "JSON 文件|*.json",
            AddExtension = true,
            DefaultExt = ".json",
            OverwritePrompt = true,
            InitialDirectory = logDirectory,
            FileName = $"orderly-failure-logs-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };

        if (dialog.ShowDialog(GetDialogOwner()) != true)
        {
            return;
        }

        var payload = logs.Select(log => new
        {
            log.Id,
            Type = log.Type.ToString(),
            log.TypeLabel,
            log.Title,
            log.Description,
            log.Operator,
            CreatedAt = log.CreatedAt.ToString("O"),
            log.CustomerId,
            log.OrderId,
            log.DealId
        }).ToArray();

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(dialog.FileName, json);
        SettingsStatusMessage = $"失败类日志已导出：{dialog.FileName}";
    }

    [RelayCommand]
    private void OpenLogDirectory()
    {
        try
        {
            var path = GetLogDirectoryPath();
            OpenDirectory(path);
            SettingsStatusMessage = $"已打开日志目录：{path}";
        }
        catch (Exception ex)
        {
            SettingsStatusMessage = $"打开日志目录失败：{ex.Message}";
            ShowErrorMessage("打开日志目录失败", ex);
        }
    }

    [RelayCommand]
    private void RestartApplication()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                throw new InvalidOperationException("未找到当前应用可执行文件路径。");
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true
            });

            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            SettingsStatusMessage = $"重启应用失败：{ex.Message}";
            ShowErrorMessage("重启应用失败", ex);
        }
    }

    [RelayCommand]
    private void OpenQaToolsDirectory()
    {
        try
        {
            var qaPath = ResolveQaToolsPath();
            if (!Directory.Exists(qaPath))
            {
                throw new InvalidOperationException($"未检测到 QA 脚本目录：{qaPath}");
            }

            OpenDirectory(qaPath);
            QaToolsStatusText = $"已打开 QA 工具目录：{qaPath}";
        }
        catch (Exception ex)
        {
            QaToolsStatusText = $"QA 数据入口不可用：{ex.Message}";
            ShowErrorMessage("QA 数据入口不可用", ex);
        }
    }

    [RelayCommand(CanExecute = nameof(CanResetLocalTestState))]
    private void ResetLocalTestState()
    {
        SettingsStatusMessage = "重置本地测试状态未接入。";
    }

    [RelayCommand]
    private async Task CheckSnConnectionAsync()
    {
        await ExecuteSaveActionAsync(
            busyMessage: "正在检查 SN 网关连接...",
            successMessage: "SN 网关检查完成",
            errorTitle: "SN 网关检查失败",
            errorStatusPrefix: "SN 网关检查失败",
            action: async () =>
            {
                var now = DateTime.Now;
                SnLastConnectionTimeText = now.ToString("yyyy-MM-dd HH:mm:ss");

                if (!IsStringNarrationGatewayEndpointConfigured || !IsStringNarrationGatewayTokenConfigured)
                {
                    SnConnectionStatusText = "配置不完整（仅做配置检查，未发起真实连接）";
                    SnLastConnectionResultText = "配置不完整";
                    await SaveSnConnectionResultAsync(now, SnConnectionStatusText);
                    return;
                }

                await TestStringNarrationGatewayAsync();
                if (string.IsNullOrWhiteSpace(StringNarrationError))
                {
                    SnConnectionStatusText = "连接检查成功";
                    SnLastConnectionResultText = StringNarrationStatusMessage;
                }
                else
                {
                    SnConnectionStatusText = "连接检查失败";
                    SnLastConnectionResultText = StringNarrationError;
                }

                await SaveSnConnectionResultAsync(now, SnLastConnectionResultText);
            });
    }

    [RelayCommand]
    private async Task RunSnManualSyncAsync()
    {
        await ExecuteSaveActionAsync(
            busyMessage: "正在手动同步 SN 订单（只读拉取）...",
            successMessage: "SN 手动同步流程完成",
            errorTitle: "SN 手动同步失败",
            errorStatusPrefix: "SN 手动同步失败",
            action: async () =>
            {
                var now = DateTime.Now;
                await _syncService.MarkPendingAsync(
                    SnSyncEntityType,
                    SnSyncEntityId,
                    JsonSerializer.Serialize(new
                    {
                        mode = "manual",
                        at = now.ToString("O")
                    }));

                if (!IsStringNarrationGatewayEndpointConfigured || !IsStringNarrationGatewayTokenConfigured)
                {
                    var reason = "同步服务未接入：网关 endpoint/token 未配置。";
                    await _syncService.MarkFailedAsync(SnSyncEntityType, SnSyncEntityId, reason);
                    SnLastSyncTimeText = now.ToString("yyyy-MM-dd HH:mm:ss");
                    SnLastSyncResultText = reason;
                    await SaveSnSyncResultAsync(now, reason);
                    await RefreshSnSyncStatusAsync();
                    return;
                }

                await LoadStringNarrationOrdersAsync();
                if (string.IsNullOrWhiteSpace(StringNarrationError))
                {
                    var message = $"同步完成：已拉取 {StringNarrationOrders.Count} 单。";
                    await _syncService.MarkSyncedAsync(
                        SnSyncEntityType,
                        SnSyncEntityId,
                        metadataJson: JsonSerializer.Serialize(new
                        {
                            mode = "manual",
                            result = message,
                            orders = StringNarrationOrders.Count,
                            at = now.ToString("O")
                        }));
                    SnLastSyncTimeText = now.ToString("yyyy-MM-dd HH:mm:ss");
                    SnLastSyncResultText = message;
                    await SaveSnSyncResultAsync(now, message);
                }
                else
                {
                    await _syncService.MarkFailedAsync(
                        SnSyncEntityType,
                        SnSyncEntityId,
                        StringNarrationError,
                        JsonSerializer.Serialize(new
                        {
                            mode = "manual",
                            result = "failed",
                            error = StringNarrationError,
                            at = now.ToString("O")
                        }));
                    SnLastSyncTimeText = now.ToString("yyyy-MM-dd HH:mm:ss");
                    SnLastSyncResultText = $"同步失败：{StringNarrationError}";
                    await SaveSnSyncResultAsync(now, SnLastSyncResultText);
                }

                await RefreshSnSyncStatusAsync();
            });
    }

    [RelayCommand]
    private async Task RefreshSnSyncLogsAsync()
    {
        await RefreshSnSyncStatusAsync();
        SettingsStatusMessage = "SN 同步状态已刷新。";
    }

    [RelayCommand]
    private async Task ExportSnSyncLogsAsync()
    {
        var latest = await _syncRecordRepository.GetLatestByEntityTypeAsync(SnSyncEntityType);
        var failures = await GetSnSyncFailureLogsAsync();

        if (latest is null && failures.Count == 0)
        {
            SettingsStatusMessage = "暂无可导出的 SN 同步日志。";
            return;
        }

        var directory = GetLogDirectoryPath();
        var dialog = new SaveFileDialog
        {
            Title = "导出 SN 同步日志",
            Filter = "JSON 文件|*.json",
            AddExtension = true,
            DefaultExt = ".json",
            OverwritePrompt = true,
            InitialDirectory = directory,
            FileName = $"sn-sync-logs-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };

        if (dialog.ShowDialog(GetDialogOwner()) != true)
        {
            return;
        }

        var payload = new
        {
            exportedAt = DateTimeOffset.Now.ToString("O"),
            latestSyncRecord = latest is null ? null : new
            {
                latest.Id,
                latest.EntityType,
                latest.EntityId,
                syncStatus = latest.SyncStatus.ToString(),
                lastSyncedAt = latest.LastSyncedAt?.ToString("O"),
                latest.ErrorMessage,
                latest.UpdatedAt,
                latest.CreatedAt
            },
            failedActivities = failures.Select(item => new
            {
                item.Id,
                type = item.Type.ToString(),
                item.Title,
                item.Description,
                item.Operator,
                createdAt = item.CreatedAt.ToString("O")
            })
        };

        await File.WriteAllTextAsync(dialog.FileName, JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        }));

        SettingsStatusMessage = $"SN 同步日志已导出：{dialog.FileName}";
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

    private AppPreferences BuildPreferencesFromInputs()
    {
        var startupDefaultSection = NormalizeOption(StartupDefaultSectionInput, StartupSectionOptions, SectionWorkbench);
        var lastSection = NormalizeOption(LastSectionInput, StartupSectionOptions, startupDefaultSection);
        var backupRetention = Math.Clamp(BackupRetentionCountInput, 1, 100);
        var retentionDays = Math.Clamp(OperationLogRetentionDaysInput, 7, 3650);
        var autoBackupFrequency = NormalizeOption(AutoBackupFrequencyInput, AutoBackupFrequencyOptions, "手动");
        var snSyncMode = NormalizeOption(SnSyncModeInput, SnSyncModeOptions, "手动");
        var snSyncFrequency = NormalizeOption(SnSyncFrequencyInput, SnSyncFrequencyOptions, "每6小时");
        var windowMode = NormalizeOption(DefaultWindowModeInput, WindowModeOptions, "普通");
        var fontPreset = NormalizeOption(FontSizePresetInput, FontPresetOptions, "标准");
        var themeMode = NormalizeOption(ThemeModeInput, ThemeModeOptions, "浅色");
        var accentColor = NormalizeOption(AccentColorInput, AccentColorOptions, "默认绿");

        var preferences = new AppPreferences
        {
            MainHotkey = Preferences.MainHotkey,
            FloatingHotkey = Preferences.FloatingHotkey,
            ShowFloatingWindowOnStartup = ShowFloatingWindowOnStartupInput,
            StartMinimizedToTray = StartMinimizedToTrayInput,
            StartupDefaultSection = startupDefaultSection,
            RememberLastSection = RememberLastSectionInput,
            LastSection = lastSection,
            StartWithWindows = StartWithWindowsInput,
            RememberWindowBounds = RememberWindowBoundsInput,
            DefaultWindowMode = windowMode,
            SidebarDefaultExpanded = SidebarDefaultExpandedInput,
            FontSizePreset = fontPreset,
            ShowWindowsScaleHint = ShowWindowsScaleHintInput,
            ThemeMode = themeMode,
            AccentColor = accentColor,
            EnableLightAnimation = EnableLightAnimationInput,
            BackupDirectory = ResolveBackupDirectory(BackupDirectoryInput),
            AutoBackupEnabled = AutoBackupEnabledInput,
            AutoBackupFrequency = autoBackupFrequency,
            BackupRetentionCount = backupRetention,
            SnOrderSyncEnabled = SnOrderSyncEnabledInput,
            SnSyncMode = snSyncMode,
            SnSyncFrequency = snSyncFrequency,
            SnLastConnectionCheckAt = Preferences.SnLastConnectionCheckAt,
            SnLastConnectionResult = Preferences.SnLastConnectionResult,
            SnLastSyncAt = Preferences.SnLastSyncAt,
            SnLastSyncResult = Preferences.SnLastSyncResult,
            MaskPhoneByDefault = MaskPhoneByDefaultInput,
            MaskAddressByDefault = MaskAddressByDefaultInput,
            IncludeSensitiveInExport = IncludeSensitiveInExportInput,
            MaskOrderSummaryOnCopy = MaskOrderSummaryOnCopyInput,
            OperationLogEnabled = OperationLogEnabledInput,
            OperationLogRetentionDays = retentionDays,
            DebugModeEnabled = DebugModeEnabledInput
        };

        return ApplyP1InputsToPreferences(preferences);
    }

    private void ApplySettingsInputsFromPreferences(AppPreferences preferences)
    {
        _isApplyingSettingsInputs = true;
        try
        {
            StartupDefaultSectionInput = NormalizeOption(preferences.StartupDefaultSection, StartupSectionOptions, SectionWorkbench);
            RememberLastSectionInput = preferences.RememberLastSection;
            LastSectionInput = NormalizeOption(preferences.LastSection, StartupSectionOptions, StartupDefaultSectionInput);
            StartWithWindowsInput = preferences.StartWithWindows;
            ShowFloatingWindowOnStartupInput = preferences.ShowFloatingWindowOnStartup;
            StartMinimizedToTrayInput = preferences.StartMinimizedToTray;
            RememberWindowBoundsInput = preferences.RememberWindowBounds;
            DefaultWindowModeInput = NormalizeOption(preferences.DefaultWindowMode, WindowModeOptions, "普通");
            SidebarDefaultExpandedInput = preferences.SidebarDefaultExpanded;
            FontSizePresetInput = NormalizeOption(preferences.FontSizePreset, FontPresetOptions, "标准");
            ShowWindowsScaleHintInput = preferences.ShowWindowsScaleHint;
            ThemeModeInput = NormalizeOption(preferences.ThemeMode, ThemeModeOptions, "浅色");
            
            var loadedColor = preferences.AccentColor;
            if (!string.IsNullOrWhiteSpace(loadedColor) && loadedColor.StartsWith('#'))
            {
                for (int i = AccentColorOptions.Count - 1; i >= 0; i--)
                {
                    if (AccentColorOptions[i].StartsWith('#'))
                    {
                        AccentColorOptions.RemoveAt(i);
                    }
                }
                AccentColorOptions.Add(loadedColor);
            }
            AccentColorInput = NormalizeOption(loadedColor, AccentColorOptions, "默认绿");

            EnableLightAnimationInput = preferences.EnableLightAnimation;

            BackupDirectoryInput = ResolveBackupDirectory(preferences.BackupDirectory);
            AutoBackupEnabledInput = preferences.AutoBackupEnabled;
            AutoBackupFrequencyInput = NormalizeOption(preferences.AutoBackupFrequency, AutoBackupFrequencyOptions, "手动");
            BackupRetentionCountInput = Math.Clamp(preferences.BackupRetentionCount, 1, 100);

            SnOrderSyncEnabledInput = preferences.SnOrderSyncEnabled;
            SnSyncModeInput = NormalizeOption(preferences.SnSyncMode, SnSyncModeOptions, "手动");
            SnSyncFrequencyInput = NormalizeOption(preferences.SnSyncFrequency, SnSyncFrequencyOptions, "每6小时");

            MaskPhoneByDefaultInput = preferences.MaskPhoneByDefault;
            MaskAddressByDefaultInput = preferences.MaskAddressByDefault;
            IncludeSensitiveInExportInput = preferences.IncludeSensitiveInExport;
            MaskOrderSummaryOnCopyInput = preferences.MaskOrderSummaryOnCopy;
            OperationLogEnabledInput = preferences.OperationLogEnabled;
            OperationLogRetentionDaysInput = Math.Clamp(preferences.OperationLogRetentionDays, 7, 3650);
            DebugModeEnabledInput = preferences.DebugModeEnabled;
            ApplyP1InputsFromPreferences(preferences);
        }
        finally
        {
            _isApplyingSettingsInputs = false;
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

    private async Task RefreshSettingsRuntimeStatusAsync(CancellationToken cancellationToken = default)
    {
        await RefreshDatabaseRuntimeStatusAsync(cancellationToken);
        await RefreshSnSyncStatusAsync(cancellationToken);
        RefreshSecurityRuntimeStatus();
        RefreshAiSettingsRuntimeStatus();
        RefreshNotificationSettingsRuntimeStatus();
        RefreshAppInfoRuntimeStatus();
    }

    private async Task RefreshDatabaseRuntimeStatusAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        try
        {
            var info = new FileInfo(DatabasePath);
            DatabaseSizeText = info.Exists ? FormatFileSize(info.Length) : "数据库文件不存在";
        }
        catch (Exception ex)
        {
            DatabaseSizeText = $"读取失败：{ex.Message}";
        }

        var (status, detail) = await CheckDatabaseHealthAsync(cancellationToken);
        DatabaseHealthStatusText = status;
        DatabaseHealthDetailText = detail;
    }

    private void RefreshSecurityRuntimeStatus()
    {
        DatabaseEncryptionStatusText = "未启用全库加密（文件层明文）；敏感字段通过会话密钥加密列保护。";
        BackupEncryptionStatusText = "本地备份为 JSON 文件，未加密。";
        LocalAccessProtectionStatusText = _sessionContextService?.IsSignedIn == true
            ? "已启用本地账号登录与 PIN 锁定链路。"
            : "未登录，无法确认本机访问保护状态。";
    }

    private void RefreshAppInfoRuntimeStatus()
    {
        var entryAssembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        AppVersionText = entryAssembly.GetName().Version?.ToString() ?? "未知";

        var location = entryAssembly.Location;
        if (!string.IsNullOrWhiteSpace(location) && File.Exists(location))
        {
            var writeTime = File.GetLastWriteTime(location);
            AppBuildTimeText = $"{writeTime:yyyy-MM-dd HH:mm:ss}（文件时间）";
        }
        else
        {
            AppBuildTimeText = "未记录";
        }

        var lines = new List<string>
        {
            $"OS: {RuntimeInformation.OSDescription}",
            $".NET: {RuntimeInformation.FrameworkDescription}",
            $"进程架构: {RuntimeInformation.ProcessArchitecture}",
            $"数据库: {DatabasePath}",
            $"网关 endpoint: {(IsStringNarrationGatewayEndpointConfigured ? "已配置" : "未配置")}",
            $"网关 token: {(IsStringNarrationGatewayTokenConfigured ? "已配置" : "未配置")}"
        };
        RuntimeEnvironmentText = string.Join(Environment.NewLine, lines);
        SnCloudEnvironmentIdText = ResolveCloudEnvironmentId();
    }

    private async Task<(string Status, string Detail)> CheckDatabaseHealthAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(DatabasePath) || !File.Exists(DatabasePath))
        {
            return ("异常", "数据库文件不存在。");
        }

        var requiredTables = new[] { "AppSettings", "Customers", "Orders", "ActivityLogs", "SyncRecords" };
        var missingTables = new List<string>();
        try
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString());
            await connection.OpenAsync(cancellationToken);

            foreach (var table in requiredTables)
            {
                await using var tableCommand = connection.CreateCommand();
                tableCommand.CommandText = """
                    SELECT COUNT(1)
                    FROM sqlite_master
                    WHERE type = 'table' AND name = $name;
                    """;
                tableCommand.Parameters.AddWithValue("$name", table);
                var exists = Convert.ToInt32(await tableCommand.ExecuteScalarAsync(cancellationToken)) > 0;
                if (!exists)
                {
                    missingTables.Add(table);
                }
            }

            await using var quickCheckCommand = connection.CreateCommand();
            quickCheckCommand.CommandText = "PRAGMA quick_check;";
            var result = Convert.ToString(await quickCheckCommand.ExecuteScalarAsync(cancellationToken)) ?? string.Empty;
            if (!string.Equals(result.Trim(), "ok", StringComparison.OrdinalIgnoreCase))
            {
                return ("异常", $"PRAGMA quick_check 返回：{result}");
            }
        }
        catch (Exception ex)
        {
            return ("异常", $"连接或校验失败：{ex.Message}");
        }

        return missingTables.Count > 0
            ? ("异常", $"缺少关键表：{string.Join(", ", missingTables)}")
            : ("正常", "数据库文件存在、可读、关键表齐全。");
    }

    private async Task RefreshSnSyncStatusAsync(CancellationToken cancellationToken = default)
    {
        var latest = await _syncRecordRepository.GetLatestByEntityTypeAsync(SnSyncEntityType, cancellationToken);
        if (latest is not null)
        {
            SnLastSyncTimeText = latest.LastSyncedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? latest.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss");
            SnLastSyncResultText = latest.SyncStatus switch
            {
                SyncStatus.Synced => "成功",
                SyncStatus.Failed => $"失败：{latest.ErrorMessage}",
                _ => "进行中"
            };
            SnSyncLogSummaryText = $"SyncRecord#{latest.Id} / {latest.SyncStatus} / 更新时间 {latest.UpdatedAt:yyyy-MM-dd HH:mm:ss}";
        }
        else
        {
            SnLastSyncTimeText = TryFormatSavedTime(Preferences.SnLastSyncAt, "未执行");
            SnLastSyncResultText = string.IsNullOrWhiteSpace(Preferences.SnLastSyncResult) ? "未执行" : Preferences.SnLastSyncResult;
            SnSyncLogSummaryText = "未发现 SyncRecords 同步记录。";
        }

        SnLastConnectionTimeText = TryFormatSavedTime(Preferences.SnLastConnectionCheckAt, "未检查");
        SnLastConnectionResultText = string.IsNullOrWhiteSpace(Preferences.SnLastConnectionResult) ? "未检查" : Preferences.SnLastConnectionResult;

        var failures = await GetSnSyncFailureLogsAsync(cancellationToken);
        if (failures.Count == 0)
        {
            SnSyncFailureSummaryText = "暂无失败记录";
            return;
        }

        var top = failures
            .Take(3)
            .Select(item => $"{item.CreatedAt:MM-dd HH:mm} {item.Description}")
            .ToArray();
        SnSyncFailureSummaryText = string.Join(Environment.NewLine, top);
    }

    private async Task<IReadOnlyList<ActivityLog>> GetFailureActivityLogsAsync(CancellationToken cancellationToken = default)
    {
        var logs = await _activityLogService.GetRecentActivitiesAsync(500, cancellationToken);
        return logs
            .Where(item =>
                item.Type is ActivityType.SyncFailed or ActivityType.BackupValidationFailed or ActivityType.BackupRestoreFailed or ActivityType.OcrTaskFailed
                || item.Title.Contains("失败", StringComparison.Ordinal)
                || item.Description.Contains("失败", StringComparison.Ordinal)
                || item.Description.Contains("错误", StringComparison.Ordinal))
            .OrderByDescending(item => item.CreatedAt)
            .ToArray();
    }

    private async Task<IReadOnlyList<ActivityLog>> GetSnSyncFailureLogsAsync(CancellationToken cancellationToken = default)
    {
        var logs = await _activityLogService.GetRecentActivitiesAsync(300, cancellationToken);
        return logs
            .Where(item => item.Type == ActivityType.SyncFailed
                && item.Description.Contains($"{SnSyncEntityType}#", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.CreatedAt)
            .ToArray();
    }

    private async Task SaveSnConnectionResultAsync(DateTime when, string result)
    {
        Preferences.SnLastConnectionCheckAt = when.ToString("O");
        Preferences.SnLastConnectionResult = result;
        await _settingRepository.UpsertAsync(AppSettingKeys.SnLastConnectionCheckAt, Preferences.SnLastConnectionCheckAt);
        await _settingRepository.UpsertAsync(AppSettingKeys.SnLastConnectionResult, Preferences.SnLastConnectionResult);
    }

    private async Task SaveSnSyncResultAsync(DateTime when, string result)
    {
        Preferences.SnLastSyncAt = when.ToString("O");
        Preferences.SnLastSyncResult = result;
        await _settingRepository.UpsertAsync(AppSettingKeys.SnLastSyncAt, Preferences.SnLastSyncAt);
        await _settingRepository.UpsertAsync(AppSettingKeys.SnLastSyncResult, Preferences.SnLastSyncResult);
    }

    private string BuildDiagnosticSummary()
    {
        var lines = new List<string>
        {
            $"应用版本: {AppVersionText}",
            $"构建时间: {AppBuildTimeText}",
            $"数据库路径: {DatabasePath}",
            $"数据库大小: {DatabaseSizeText}",
            $"数据库健康: {DatabaseHealthStatusText}",
            $"数据库详情: {DatabaseHealthDetailText}",
            $"SN Endpoint: {(IsStringNarrationGatewayEndpointConfigured ? "已配置" : "未配置")}",
            $"SN Token: {(IsStringNarrationGatewayTokenConfigured ? "已配置" : "未配置")}",
            $"SN 最近连接: {SnLastConnectionTimeText}",
            $"SN 连接结果: {SnLastConnectionResultText}",
            $"SN 最近同步: {SnLastSyncTimeText}",
            $"SN 同步结果: {SnLastSyncResultText}",
            $"本机访问保护: {LocalAccessProtectionStatusText}",
            $"运行时环境: {RuntimeEnvironmentText.Replace(Environment.NewLine, " | ", StringComparison.Ordinal)}",
            $"当前账号: {CurrentAccountDisplayName}",
            $"当前角色: {(IsCurrentUserOwner ? "Owner" : "Member/Unknown")}"
        };

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        var kb = bytes / 1024d;
        if (kb < 1024)
        {
            return $"{kb:F1} KB";
        }

        var mb = kb / 1024d;
        if (mb < 1024)
        {
            return $"{mb:F2} MB";
        }

        var gb = mb / 1024d;
        return $"{gb:F2} GB";
    }

    private static string NormalizeOption(string? value, IEnumerable<string> options, string fallback)
    {
        var normalized = (value ?? string.Empty).Trim();
        return options.Contains(normalized, StringComparer.Ordinal) ? normalized : fallback;
    }

    private static string ResolveBackupDirectory(string? path)
    {
        var candidate = string.IsNullOrWhiteSpace(path) ? BuildDefaultBackupDirectory() : path.Trim();
        try
        {
            return Path.GetFullPath(candidate);
        }
        catch (Exception)
        {
            return BuildDefaultBackupDirectory();
        }
    }

    private static string BuildDefaultBackupDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Orderly",
            "Backups");
    }

    private static string ResolveCloudEnvironmentId()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("TENCENTCLOUD_ENV_ID"),
            Environment.GetEnvironmentVariable("SN_CLOUD_ENV_ID"),
            Environment.GetEnvironmentVariable("ADMIN_PC_GATEWAY_ENV_ID")
        };

        return candidates.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "未配置";
    }

    private static string TryFormatSavedTime(string rawValue, string fallback)
    {
        if (DateTimeOffset.TryParse(rawValue, out var parsed))
        {
            return parsed.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        }

        return fallback;
    }

    private static void OpenDirectory(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private static string ResolveQaToolsPath()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "tools", "qa"));
    }

    private static string GetLogDirectoryPath()
    {
        var logDirectory = Path.Combine(DatabasePaths.GetAppRootPath(), "logs");
        Directory.CreateDirectory(logDirectory);
        return logDirectory;
    }

    [RelayCommand]
    private void BrowseBackupDirectory()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择备份目录",
            SelectedPath = ResolveBackupDirectory(BackupDirectoryInput),
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            BackupDirectoryInput = dialog.SelectedPath;
        }
    }

    [RelayCommand]
    private void ChooseCustomAccentColor()
    {
        using var dialog = new System.Windows.Forms.ColorDialog();
        dialog.FullOpen = true;
        if (!string.IsNullOrWhiteSpace(AccentColorInput) && AccentColorInput.StartsWith('#') && (AccentColorInput.Length == 7 || AccentColorInput.Length == 9))
        {
            try
            {
                dialog.Color = System.Drawing.ColorTranslator.FromHtml(AccentColorInput);
            }
            catch { }
        }

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var color = dialog.Color;
            var hexColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            
            for (int i = AccentColorOptions.Count - 1; i >= 0; i--)
            {
                if (AccentColorOptions[i].StartsWith('#'))
                {
                    AccentColorOptions.RemoveAt(i);
                }
            }
            
            AccentColorOptions.Add(hexColor);
            AccentColorInput = hexColor;
        }
    }
}
