namespace Orderly.Core.Models;

public sealed class AppPreferences
{
    public string MainHotkey { get; set; } = "Ctrl+Alt+O";
    public string FloatingHotkey { get; set; } = "Ctrl+Alt+R";
    public string GlobalSearchHotkey { get; set; } = "Ctrl+Alt+F";
    public string TodayWorkbenchHotkey { get; set; } = "Ctrl+Alt+W";
    public string CopyOrderSummaryHotkey { get; set; } = "Ctrl+Shift+C";
    public string OpenProductionSheetHotkey { get; set; } = "Ctrl+Shift+P";
    public string MarkOrderExceptionHotkey { get; set; } = "Ctrl+Shift+E";
    public string AdvanceFulfillmentHotkey { get; set; } = "Ctrl+Shift+N";
    public string OpenCustomerProfileHotkey { get; set; } = "Ctrl+Shift+F";
    public string NewCustomerNoteHotkey { get; set; } = "Ctrl+Shift+M";
    public string CopyCustomerPreferenceSummaryHotkey { get; set; } = "Ctrl+Shift+Y";
    public bool ShowFloatingWindowOnStartup { get; set; }
    public bool StartMinimizedToTray { get; set; }

    public string StartupDefaultSection { get; set; } = "工作台";
    public bool RememberLastSection { get; set; }
    public string LastSection { get; set; } = "工作台";
    public bool StartWithWindows { get; set; }
    public bool RememberWindowBounds { get; set; }
    public string DefaultWindowMode { get; set; } = "普通";
    public bool SidebarDefaultExpanded { get; set; } = true;
    public string FontSizePreset { get; set; } = "标准";
    public bool ShowWindowsScaleHint { get; set; } = true;
    public string ThemeMode { get; set; } = "浅色";
    public string AccentColor { get; set; } = "默认绿";
    public bool EnableLightAnimation { get; set; }

    public string BackupDirectory { get; set; } = string.Empty;
    public bool AutoBackupEnabled { get; set; }
    public string AutoBackupFrequency { get; set; } = "手动";
    public int BackupRetentionCount { get; set; } = 10;

    public bool SnOrderSyncEnabled { get; set; }
    public string SnSyncMode { get; set; } = "手动";
    public string SnSyncFrequency { get; set; } = "每6小时";
    public string SnLastConnectionCheckAt { get; set; } = string.Empty;
    public string SnLastConnectionResult { get; set; } = "未检查";
    public string SnLastSyncAt { get; set; } = string.Empty;
    public string SnLastSyncResult { get; set; } = "未执行";

    public bool MaskPhoneByDefault { get; set; } = true;
    public bool MaskAddressByDefault { get; set; } = true;
    public bool IncludeSensitiveInExport { get; set; }
    public bool MaskOrderSummaryOnCopy { get; set; } = true;
    public bool OperationLogEnabled { get; set; } = true;
    public int OperationLogRetentionDays { get; set; } = 180;

    public bool AiAssistantEnabled { get; set; }
    public bool AiAllowOrderContext { get; set; }
    public bool AiAllowCustomerProfileContext { get; set; }
    public string AiDefaultModel { get; set; } = string.Empty;
    public int AiTimeoutSeconds { get; set; } = 15;
    public bool AiAutoRedactBeforeSend { get; set; } = true;
    public bool AiBlockPhone { get; set; } = true;
    public bool AiBlockFullAddress { get; set; } = true;
    public bool AiBlockPaymentTransactionId { get; set; } = true;
    public string AiReplyTone { get; set; } = "简洁";
    public string AiReplyLength { get; set; } = "标准";
    public bool AiAutoGenerateOrderSummary { get; set; }

    public bool NotifyNewOrder { get; set; } = true;
    public bool NotifyExceptionOrder { get; set; } = true;
    public bool NotifyOverdueUnhandled { get; set; } = true;
    public bool NotifySyncFailed { get; set; } = true;
    public int NotifyPaidUnconfirmedHours { get; set; } = 24;
    public int NotifyPendingProductionHours { get; set; } = 24;
    public int NotifyPendingShipmentHours { get; set; } = 48;
    public bool NotifyMissingAddress { get; set; } = true;
    public string NotifyDoNotDisturbRange { get; set; } = "22:00-08:00";
    public bool NotifyHighPriorityOnly { get; set; }

    public bool DebugModeEnabled { get; set; }
}
