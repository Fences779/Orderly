using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Repositories;

public sealed class AppSettingRepository : IAppSettingRepository
{
    private const string DefaultDoNotDisturbRange = "22:00-08:00";

    private static readonly HashSet<string> AllowedStartupSections = new(StringComparer.Ordinal)
    {
        "工作台",
        "订单",
        "商品",
        "库存管理",
        "客户",
        "现金流",
        "经营建议",
        "设置",
        "我的"
    };

    // Saved startup/last-section values that point at removed pages are remapped to the closest
    // current page (订单履约 → 订单, 异常处理 → 经营建议) so an upgraded user never starts on a blank page.
    private static readonly Dictionary<string, string> LegacyStartupSectionMap = new(StringComparer.Ordinal)
    {
        ["订单履约"] = "订单",
        ["异常处理"] = "经营建议"
    };

    private static readonly HashSet<string> AllowedWindowModes = new(StringComparer.Ordinal)
    {
        "普通",
        "最大化"
    };

    private static readonly HashSet<string> AllowedFontPresets = new(StringComparer.Ordinal)
    {
        "小",
        "标准",
        "大"
    };

    private static readonly HashSet<string> AllowedThemeModes = new(StringComparer.Ordinal)
    {
        "浅色",
        "深色",
        "跟随系统"
    };

    private static readonly HashSet<string> AllowedAccentColors = new(StringComparer.Ordinal)
    {
        "默认绿",
        "茶金",
        "雾蓝"
    };

    private static readonly HashSet<string> AllowedBackupFrequencies = new(StringComparer.Ordinal)
    {
        "手动",
        "每日",
        "每周"
    };

    private static readonly HashSet<string> AllowedAiReplyTones = new(StringComparer.Ordinal)
    {
        "简洁",
        "温和",
        "专业"
    };

    private static readonly HashSet<string> AllowedAiReplyLengths = new(StringComparer.Ordinal)
    {
        "短",
        "标准",
        "详细"
    };

    private readonly SqliteConnectionFactory _connectionFactory;

    public AppSettingRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<AppPreferences> GetPreferencesAsync(CancellationToken cancellationToken = default)
    {
        var settings = await ReadAllAsync(cancellationToken);
        var fallbackBackupDirectory = BuildDefaultBackupDirectory();

        return new AppPreferences
        {
            MainHotkey = GetHotkey(settings, AppSettingKeys.MainHotkey, "Ctrl+Alt+O"),
            FloatingHotkey = GetHotkey(settings, AppSettingKeys.FloatingHotkey, "Ctrl+Alt+R"),
            GlobalSearchHotkey = GetHotkey(settings, AppSettingKeys.GlobalSearchHotkey, "Ctrl+Alt+F"),
            TodayWorkbenchHotkey = GetHotkey(settings, AppSettingKeys.TodayWorkbenchHotkey, "Ctrl+Alt+W"),
            CopyOrderSummaryHotkey = GetHotkey(settings, AppSettingKeys.CopyOrderSummaryHotkey, "Ctrl+Shift+C"),
            OpenProductionSheetHotkey = GetHotkey(settings, AppSettingKeys.OpenProductionSheetHotkey, "Ctrl+Shift+P"),
            MarkOrderExceptionHotkey = GetHotkey(settings, AppSettingKeys.MarkOrderExceptionHotkey, "Ctrl+Shift+E"),
            AdvanceFulfillmentHotkey = GetHotkey(settings, AppSettingKeys.AdvanceFulfillmentHotkey, "Ctrl+Shift+N"),
            OpenCustomerProfileHotkey = GetHotkey(settings, AppSettingKeys.OpenCustomerProfileHotkey, "Ctrl+Shift+F"),
            NewCustomerNoteHotkey = GetHotkey(settings, AppSettingKeys.NewCustomerNoteHotkey, "Ctrl+Shift+M"),
            CopyCustomerPreferenceSummaryHotkey = GetHotkey(settings, AppSettingKeys.CopyCustomerPreferenceSummaryHotkey, "Ctrl+Shift+Y"),
            ShowFloatingWindowOnStartup = GetBool(settings, AppSettingKeys.ShowFloatingWindowOnStartup, false),
            StartMinimizedToTray = GetBool(settings, AppSettingKeys.StartMinimizedToTray, false),
            StartupDefaultSection = GetStartupSection(settings, AppSettingKeys.StartupDefaultSection, "工作台"),
            RememberLastSection = GetBool(settings, AppSettingKeys.RememberLastSection, false),
            LastSection = GetStartupSection(settings, AppSettingKeys.LastSection, "工作台"),
            StartWithWindows = GetBool(settings, AppSettingKeys.StartWithWindows, false),
            RememberWindowBounds = GetBool(settings, AppSettingKeys.RememberWindowBounds, false),
            DefaultWindowMode = GetEnum(settings, AppSettingKeys.DefaultWindowMode, "普通", AllowedWindowModes),
            SidebarDefaultExpanded = GetBool(settings, AppSettingKeys.SidebarDefaultExpanded, true),
            FontSizePreset = GetEnum(settings, AppSettingKeys.FontSizePreset, "标准", AllowedFontPresets),
            ShowWindowsScaleHint = GetBool(settings, AppSettingKeys.ShowWindowsScaleHint, true),
            ThemeMode = GetEnum(settings, AppSettingKeys.ThemeMode, "浅色", AllowedThemeModes),
            AccentColor = GetAccentColor(settings, AppSettingKeys.AccentColor, "默认绿"),
            EnableLightAnimation = GetBool(settings, AppSettingKeys.EnableLightAnimation, false),
            BackupDirectory = NormalizePath(Get(settings, AppSettingKeys.BackupDirectory, fallbackBackupDirectory), fallbackBackupDirectory),
            AutoBackupEnabled = GetBool(settings, AppSettingKeys.AutoBackupEnabled, false),
            AutoBackupFrequency = GetEnum(settings, AppSettingKeys.AutoBackupFrequency, "手动", AllowedBackupFrequencies),
            BackupRetentionCount = GetInt(settings, AppSettingKeys.BackupRetentionCount, 10, 1, 100),
            MaskPhoneByDefault = GetBool(settings, AppSettingKeys.MaskPhoneByDefault, true),
            MaskAddressByDefault = GetBool(settings, AppSettingKeys.MaskAddressByDefault, true),
            IncludeSensitiveInExport = GetBool(settings, AppSettingKeys.IncludeSensitiveInExport, false),
            MaskOrderSummaryOnCopy = GetBool(settings, AppSettingKeys.MaskOrderSummaryOnCopy, true),
            OperationLogEnabled = GetBool(settings, AppSettingKeys.OperationLogEnabled, true),
            OperationLogRetentionDays = GetInt(settings, AppSettingKeys.OperationLogRetentionDays, 180, 7, 3650),
            AiAssistantEnabled = GetBool(settings, AppSettingKeys.AiAssistantEnabled, false),
            AiAllowOrderContext = GetBool(settings, AppSettingKeys.AiAllowOrderContext, false),
            AiAllowCustomerProfileContext = GetBool(settings, AppSettingKeys.AiAllowCustomerProfileContext, false),
            AiDefaultModel = Get(settings, AppSettingKeys.AiDefaultModel, string.Empty).Trim(),
            AiTimeoutSeconds = GetInt(settings, AppSettingKeys.AiTimeoutSeconds, 15, 5, 120),
            AiAutoRedactBeforeSend = GetBool(settings, AppSettingKeys.AiAutoRedactBeforeSend, true),
            AiBlockPhone = GetBool(settings, AppSettingKeys.AiBlockPhone, true),
            AiBlockFullAddress = GetBool(settings, AppSettingKeys.AiBlockFullAddress, true),
            AiBlockPaymentTransactionId = GetBool(settings, AppSettingKeys.AiBlockPaymentTransactionId, true),
            AiReplyTone = GetEnum(settings, AppSettingKeys.AiReplyTone, "简洁", AllowedAiReplyTones),
            AiReplyLength = GetEnum(settings, AppSettingKeys.AiReplyLength, "标准", AllowedAiReplyLengths),
            AiAutoGenerateOrderSummary = GetBool(settings, AppSettingKeys.AiAutoGenerateOrderSummary, false),
            NotifyNewOrder = GetBool(settings, AppSettingKeys.NotifyNewOrder, true),
            NotifyExceptionOrder = GetBool(settings, AppSettingKeys.NotifyExceptionOrder, true),
            NotifyOverdueUnhandled = GetBool(settings, AppSettingKeys.NotifyOverdueUnhandled, true),
            NotifySyncFailed = GetBool(settings, AppSettingKeys.NotifySyncFailed, true),
            NotifyPaidUnconfirmedHours = GetInt(settings, AppSettingKeys.NotifyPaidUnconfirmedHours, 24, 1, 168),
            NotifyPendingProductionHours = GetInt(settings, AppSettingKeys.NotifyPendingProductionHours, 24, 1, 168),
            NotifyPendingShipmentHours = GetInt(settings, AppSettingKeys.NotifyPendingShipmentHours, 48, 1, 168),
            NotifyMissingAddress = GetBool(settings, AppSettingKeys.NotifyMissingAddress, true),
            NotifyDoNotDisturbRange = NormalizeDoNotDisturbRange(Get(settings, AppSettingKeys.NotifyDoNotDisturbRange, DefaultDoNotDisturbRange), DefaultDoNotDisturbRange),
            NotifyHighPriorityOnly = GetBool(settings, AppSettingKeys.NotifyHighPriorityOnly, false),
            DebugModeEnabled = GetBool(settings, AppSettingKeys.DebugModeEnabled, false)
        };
    }

    public async Task SavePreferencesAsync(AppPreferences preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        var items = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AppSettingKeys.MainHotkey] = NormalizeHotkey(preferences.MainHotkey, "Ctrl+Alt+O"),
            [AppSettingKeys.FloatingHotkey] = NormalizeHotkey(preferences.FloatingHotkey, "Ctrl+Alt+R"),
            [AppSettingKeys.GlobalSearchHotkey] = NormalizeHotkey(preferences.GlobalSearchHotkey, "Ctrl+Alt+F"),
            [AppSettingKeys.TodayWorkbenchHotkey] = NormalizeHotkey(preferences.TodayWorkbenchHotkey, "Ctrl+Alt+W"),
            [AppSettingKeys.CopyOrderSummaryHotkey] = NormalizeHotkey(preferences.CopyOrderSummaryHotkey, "Ctrl+Shift+C"),
            [AppSettingKeys.OpenProductionSheetHotkey] = NormalizeHotkey(preferences.OpenProductionSheetHotkey, "Ctrl+Shift+P"),
            [AppSettingKeys.MarkOrderExceptionHotkey] = NormalizeHotkey(preferences.MarkOrderExceptionHotkey, "Ctrl+Shift+E"),
            [AppSettingKeys.AdvanceFulfillmentHotkey] = NormalizeHotkey(preferences.AdvanceFulfillmentHotkey, "Ctrl+Shift+N"),
            [AppSettingKeys.OpenCustomerProfileHotkey] = NormalizeHotkey(preferences.OpenCustomerProfileHotkey, "Ctrl+Shift+F"),
            [AppSettingKeys.NewCustomerNoteHotkey] = NormalizeHotkey(preferences.NewCustomerNoteHotkey, "Ctrl+Shift+M"),
            [AppSettingKeys.CopyCustomerPreferenceSummaryHotkey] = NormalizeHotkey(preferences.CopyCustomerPreferenceSummaryHotkey, "Ctrl+Shift+Y"),
            [AppSettingKeys.ShowFloatingWindowOnStartup] = ToBoolValue(preferences.ShowFloatingWindowOnStartup),
            [AppSettingKeys.StartMinimizedToTray] = ToBoolValue(preferences.StartMinimizedToTray),
            [AppSettingKeys.StartupDefaultSection] = NormalizeStartupSectionValue(preferences.StartupDefaultSection, "工作台"),
            [AppSettingKeys.RememberLastSection] = ToBoolValue(preferences.RememberLastSection),
            [AppSettingKeys.LastSection] = NormalizeStartupSectionValue(preferences.LastSection, "工作台"),
            [AppSettingKeys.StartWithWindows] = ToBoolValue(preferences.StartWithWindows),
            [AppSettingKeys.RememberWindowBounds] = ToBoolValue(preferences.RememberWindowBounds),
            [AppSettingKeys.DefaultWindowMode] = NormalizeEnumValue(preferences.DefaultWindowMode, "普通", AllowedWindowModes),
            [AppSettingKeys.SidebarDefaultExpanded] = ToBoolValue(preferences.SidebarDefaultExpanded),
            [AppSettingKeys.FontSizePreset] = NormalizeEnumValue(preferences.FontSizePreset, "标准", AllowedFontPresets),
            [AppSettingKeys.ShowWindowsScaleHint] = ToBoolValue(preferences.ShowWindowsScaleHint),
            [AppSettingKeys.ThemeMode] = NormalizeEnumValue(preferences.ThemeMode, "浅色", AllowedThemeModes),
            [AppSettingKeys.AccentColor] = NormalizeAccentColor(preferences.AccentColor, "默认绿"),
            [AppSettingKeys.EnableLightAnimation] = ToBoolValue(preferences.EnableLightAnimation),
            [AppSettingKeys.BackupDirectory] = NormalizePath(preferences.BackupDirectory, BuildDefaultBackupDirectory()),
            [AppSettingKeys.AutoBackupEnabled] = ToBoolValue(preferences.AutoBackupEnabled),
            [AppSettingKeys.AutoBackupFrequency] = NormalizeEnumValue(preferences.AutoBackupFrequency, "手动", AllowedBackupFrequencies),
            [AppSettingKeys.BackupRetentionCount] = Math.Clamp(preferences.BackupRetentionCount, 1, 100).ToString(),
            [AppSettingKeys.MaskPhoneByDefault] = ToBoolValue(preferences.MaskPhoneByDefault),
            [AppSettingKeys.MaskAddressByDefault] = ToBoolValue(preferences.MaskAddressByDefault),
            [AppSettingKeys.IncludeSensitiveInExport] = ToBoolValue(preferences.IncludeSensitiveInExport),
            [AppSettingKeys.MaskOrderSummaryOnCopy] = ToBoolValue(preferences.MaskOrderSummaryOnCopy),
            [AppSettingKeys.OperationLogEnabled] = ToBoolValue(preferences.OperationLogEnabled),
            [AppSettingKeys.OperationLogRetentionDays] = Math.Clamp(preferences.OperationLogRetentionDays, 7, 3650).ToString(),
            [AppSettingKeys.AiAssistantEnabled] = ToBoolValue(preferences.AiAssistantEnabled),
            [AppSettingKeys.AiAllowOrderContext] = ToBoolValue(preferences.AiAllowOrderContext),
            [AppSettingKeys.AiAllowCustomerProfileContext] = ToBoolValue(preferences.AiAllowCustomerProfileContext),
            [AppSettingKeys.AiDefaultModel] = preferences.AiDefaultModel?.Trim() ?? string.Empty,
            [AppSettingKeys.AiTimeoutSeconds] = Math.Clamp(preferences.AiTimeoutSeconds, 5, 120).ToString(),
            [AppSettingKeys.AiAutoRedactBeforeSend] = ToBoolValue(preferences.AiAutoRedactBeforeSend),
            [AppSettingKeys.AiBlockPhone] = ToBoolValue(preferences.AiBlockPhone),
            [AppSettingKeys.AiBlockFullAddress] = ToBoolValue(preferences.AiBlockFullAddress),
            [AppSettingKeys.AiBlockPaymentTransactionId] = ToBoolValue(preferences.AiBlockPaymentTransactionId),
            [AppSettingKeys.AiReplyTone] = NormalizeEnumValue(preferences.AiReplyTone, "简洁", AllowedAiReplyTones),
            [AppSettingKeys.AiReplyLength] = NormalizeEnumValue(preferences.AiReplyLength, "标准", AllowedAiReplyLengths),
            [AppSettingKeys.AiAutoGenerateOrderSummary] = ToBoolValue(preferences.AiAutoGenerateOrderSummary),
            [AppSettingKeys.NotifyNewOrder] = ToBoolValue(preferences.NotifyNewOrder),
            [AppSettingKeys.NotifyExceptionOrder] = ToBoolValue(preferences.NotifyExceptionOrder),
            [AppSettingKeys.NotifyOverdueUnhandled] = ToBoolValue(preferences.NotifyOverdueUnhandled),
            [AppSettingKeys.NotifySyncFailed] = ToBoolValue(preferences.NotifySyncFailed),
            [AppSettingKeys.NotifyPaidUnconfirmedHours] = Math.Clamp(preferences.NotifyPaidUnconfirmedHours, 1, 168).ToString(),
            [AppSettingKeys.NotifyPendingProductionHours] = Math.Clamp(preferences.NotifyPendingProductionHours, 1, 168).ToString(),
            [AppSettingKeys.NotifyPendingShipmentHours] = Math.Clamp(preferences.NotifyPendingShipmentHours, 1, 168).ToString(),
            [AppSettingKeys.NotifyMissingAddress] = ToBoolValue(preferences.NotifyMissingAddress),
            [AppSettingKeys.NotifyDoNotDisturbRange] = NormalizeDoNotDisturbRange(preferences.NotifyDoNotDisturbRange, DefaultDoNotDisturbRange),
            [AppSettingKeys.NotifyHighPriorityOnly] = ToBoolValue(preferences.NotifyHighPriorityOnly),
            [AppSettingKeys.DebugModeEnabled] = ToBoolValue(preferences.DebugModeEnabled)
        };

        await UpsertManyAsync(items, cancellationToken);
    }

    public async Task UpsertAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("设置 key 不能为空。", nameof(key));
        }

        await UpsertManyAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [key.Trim()] = value ?? string.Empty
            },
            cancellationToken);
    }

    private async Task<IDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Key, Value FROM AppSettings;";

        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            settings[reader.GetString(0)] = reader.GetString(1);
        }

        return settings;
    }

    private async Task UpsertManyAsync(IReadOnlyDictionary<string, string> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return;
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        foreach (var item in items)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO AppSettings (Key, Value)
                VALUES ($key, $value)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
                """;
            command.Parameters.AddWithValue("$key", item.Key);
            command.Parameters.AddWithValue("$value", item.Value ?? string.Empty);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static string Get(IDictionary<string, string> settings, string key, string fallback)
    {
        return settings.TryGetValue(key, out var value) ? value : fallback;
    }

    private static bool GetBool(IDictionary<string, string> settings, string key, bool fallback)
    {
        var raw = Get(settings, key, fallback ? "true" : "false");
        return bool.TryParse(raw, out var parsed) ? parsed : fallback;
    }

    private static int GetInt(IDictionary<string, string> settings, string key, int fallback, int min, int max)
    {
        var raw = Get(settings, key, fallback.ToString());
        if (!int.TryParse(raw, out var parsed))
        {
            return fallback;
        }

        return Math.Clamp(parsed, min, max);
    }

    private static string GetEnum(IDictionary<string, string> settings, string key, string fallback, HashSet<string> allowedValues)
    {
        var raw = Get(settings, key, fallback).Trim();
        return allowedValues.Contains(raw) ? raw : fallback;
    }

    private static string GetStartupSection(IDictionary<string, string> settings, string key, string fallback)
    {
        return NormalizeStartupSectionValue(Get(settings, key, fallback), fallback);
    }

    private static string NormalizeStartupSectionValue(string? value, string fallback)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (LegacyStartupSectionMap.TryGetValue(normalized, out var mapped))
        {
            normalized = mapped;
        }

        return AllowedStartupSections.Contains(normalized) ? normalized : fallback;
    }

    private static string GetHotkey(IDictionary<string, string> settings, string key, string fallback)
    {
        return NormalizeHotkey(Get(settings, key, fallback), fallback);
    }

    private static string NormalizeEnumValue(string? value, string fallback, HashSet<string> allowedValues)
    {
        var normalized = (value ?? string.Empty).Trim();
        return allowedValues.Contains(normalized) ? normalized : fallback;
    }

    private static string NormalizePath(string? value, string fallback)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        try
        {
            return Path.GetFullPath(candidate);
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private static string ToBoolValue(bool value)
    {
        return value ? "true" : "false";
    }

    private static string NormalizeHotkey(string? value, string fallback)
    {
        return HotkeyTextValidator.NormalizeOrFallback(value, fallback);
    }

    private static string NormalizeDoNotDisturbRange(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var parts = value.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return fallback;
        }

        if (!TimeSpan.TryParseExact(parts[0], "hh\\:mm", System.Globalization.CultureInfo.InvariantCulture, out var start)
            || !TimeSpan.TryParseExact(parts[1], "hh\\:mm", System.Globalization.CultureInfo.InvariantCulture, out var end))
        {
            return fallback;
        }

        if (start < TimeSpan.Zero || start >= TimeSpan.FromDays(1) || end < TimeSpan.Zero || end >= TimeSpan.FromDays(1))
        {
            return fallback;
        }

        if (start == end)
        {
            return fallback;
        }

        return $"{start:hh\\:mm}-{end:hh\\:mm}";
    }

    private static string BuildDefaultBackupDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Orderly",
            "Backups");
    }

    private static string GetAccentColor(IDictionary<string, string> settings, string key, string fallback)
    {
        var raw = Get(settings, key, fallback).Trim();
        if (raw == "默认绿" || raw == "茶金" || raw == "雾蓝")
        {
            return raw;
        }
        if (raw.StartsWith('#') && (raw.Length == 7 || raw.Length == 9))
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(raw, "^#[0-9a-fA-F]+$"))
            {
                return raw;
            }
        }
        return fallback;
    }

    private static string NormalizeAccentColor(string? value, string fallback)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized == "默认绿" || normalized == "茶金" || normalized == "雾蓝")
        {
            return normalized;
        }
        if (normalized.StartsWith('#') && (normalized.Length == 7 || normalized.Length == 9))
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(normalized, "^#[0-9a-fA-F]+$"))
            {
                return normalized;
            }
        }
        return fallback;
    }
}
