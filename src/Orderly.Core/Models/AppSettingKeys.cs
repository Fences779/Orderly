namespace Orderly.Core.Models;

public static class AppSettingKeys
{
    public const string MainHotkey = "MainHotkey";
    public const string FloatingHotkey = "FloatingHotkey";
    public const string GlobalSearchHotkey = "GlobalSearchHotkey";
    public const string TodayWorkbenchHotkey = "TodayWorkbenchHotkey";
    public const string CopyOrderSummaryHotkey = "CopyOrderSummaryHotkey";
    public const string OpenProductionSheetHotkey = "OpenProductionSheetHotkey";
    public const string MarkOrderExceptionHotkey = "MarkOrderExceptionHotkey";
    public const string AdvanceFulfillmentHotkey = "AdvanceFulfillmentHotkey";
    public const string OpenCustomerProfileHotkey = "OpenCustomerProfileHotkey";
    public const string NewCustomerNoteHotkey = "NewCustomerNoteHotkey";
    public const string CopyCustomerPreferenceSummaryHotkey = "CopyCustomerPreferenceSummaryHotkey";
    public const string ShowFloatingWindowOnStartup = "ShowFloatingWindowOnStartup";
    public const string StartMinimizedToTray = "StartMinimizedToTray";

    public const string StartupDefaultSection = "StartupDefaultSection";
    public const string RememberLastSection = "RememberLastSection";
    public const string LastSection = "LastSection";
    public const string StartWithWindows = "StartWithWindows";
    public const string RememberWindowBounds = "RememberWindowBounds";
    public const string DefaultWindowMode = "DefaultWindowMode";
    public const string SidebarDefaultExpanded = "SidebarDefaultExpanded";
    public const string FontSizePreset = "FontSizePreset";
    public const string ShowWindowsScaleHint = "ShowWindowsScaleHint";
    public const string ThemeMode = "ThemeMode";
    public const string AccentColor = "AccentColor";
    public const string EnableLightAnimation = "EnableLightAnimation";

    public const string BackupDirectory = "BackupDirectory";
    public const string AutoBackupEnabled = "AutoBackupEnabled";
    public const string AutoBackupFrequency = "AutoBackupFrequency";
    public const string BackupRetentionCount = "BackupRetentionCount";

    public const string SnOrderSyncEnabled = "SnOrderSyncEnabled";
    public const string SnSyncMode = "SnSyncMode";
    public const string SnSyncFrequency = "SnSyncFrequency";
    public const string SnLastConnectionCheckAt = "SnLastConnectionCheckAt";
    public const string SnLastConnectionResult = "SnLastConnectionResult";
    public const string SnLastSyncAt = "SnLastSyncAt";
    public const string SnLastSyncResult = "SnLastSyncResult";

    public const string MaskPhoneByDefault = "MaskPhoneByDefault";
    public const string MaskAddressByDefault = "MaskAddressByDefault";
    public const string IncludeSensitiveInExport = "IncludeSensitiveInExport";
    public const string MaskOrderSummaryOnCopy = "MaskOrderSummaryOnCopy";
    public const string OperationLogEnabled = "OperationLogEnabled";
    public const string OperationLogRetentionDays = "OperationLogRetentionDays";

    public const string AiAssistantEnabled = "AiAssistantEnabled";
    public const string AiAllowOrderContext = "AiAllowOrderContext";
    public const string AiAllowCustomerProfileContext = "AiAllowCustomerProfileContext";
    public const string AiDefaultModel = "AiDefaultModel";
    public const string AiTimeoutSeconds = "AiTimeoutSeconds";
    public const string AiAutoRedactBeforeSend = "AiAutoRedactBeforeSend";
    public const string AiBlockPhone = "AiBlockPhone";
    public const string AiBlockFullAddress = "AiBlockFullAddress";
    public const string AiBlockPaymentTransactionId = "AiBlockPaymentTransactionId";
    public const string AiReplyTone = "AiReplyTone";
    public const string AiReplyLength = "AiReplyLength";
    public const string AiAutoGenerateOrderSummary = "AiAutoGenerateOrderSummary";

    public const string NotifyNewOrder = "NotifyNewOrder";
    public const string NotifyExceptionOrder = "NotifyExceptionOrder";
    public const string NotifyOverdueUnhandled = "NotifyOverdueUnhandled";
    public const string NotifySyncFailed = "NotifySyncFailed";
    public const string NotifyPaidUnconfirmedHours = "NotifyPaidUnconfirmedHours";
    public const string NotifyPendingProductionHours = "NotifyPendingProductionHours";
    public const string NotifyPendingShipmentHours = "NotifyPendingShipmentHours";
    public const string NotifyMissingAddress = "NotifyMissingAddress";
    public const string NotifyDoNotDisturbRange = "NotifyDoNotDisturbRange";
    public const string NotifyHighPriorityOnly = "NotifyHighPriorityOnly";

    public const string DebugModeEnabled = "DebugModeEnabled";
}
