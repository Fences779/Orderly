using Microsoft.Data.Sqlite;
using Orderly.Core.Models;

namespace Orderly.Data.Sqlite;

public sealed partial class DatabaseInitializer
{
    private static async Task SeedAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (await CountAsync(connection, "Customers", cancellationToken) == 0)
        {
            var now = DateTime.Now;
            await InsertCustomerAsync(connection, "林小姐", "微信", "私域咨询", "lin_photo", "13800000001", "偏好自然风格，预算明确", "wx-customer-001", now, cancellationToken);
            await InsertCustomerAsync(connection, "陈先生", "闲鱼", "二手平台", "chen_store", "13800000002", "关注交付周期", "xy-customer-002", now, cancellationToken);
            await InsertCustomerAsync(connection, "周老板", "微信", "老客复购", "zhou_boss", "13800000003", "企业礼品需求", "wx-customer-003", now, cancellationToken);
            await InsertCustomerAsync(connection, "王同学", "小红书", "内容咨询", "wang_note", "", "需要多版本报价", "xhs-customer-004", now, cancellationToken);
            await InsertCustomerAsync(connection, "赵女士", "线下", "门店咨询", "zhao_local", "13800000005", "倾向加急处理", "offline-customer-005", now, cancellationToken);
        }

        if (await CountAsync(connection, "Orders", cancellationToken) == 0)
        {
            var now = DateTime.Now;
            await InsertOrderAsync(connection, 1, "婚礼伴手礼定制", OrderStatus.PendingCommunication, 0, "需要确认数量、包装和交付日期", "微信", "私域咨询", now.AddHours(4), cancellationToken);
            await InsertOrderAsync(connection, 1, "家庭纪念照相框", OrderStatus.PendingQuote, 0, "客户已发尺寸，待整理报价", "微信", "私域咨询", now.AddDays(1), cancellationToken);
            await InsertOrderAsync(connection, 2, "闲鱼咨询摆件修复", OrderStatus.Quoted, 680, "已发报价，等待客户确认", "闲鱼", "二手平台", now.AddDays(2), cancellationToken);
            await InsertOrderAsync(connection, 2, "旧物翻新加急单", OrderStatus.PendingFollowUp, 1280, "客户担心周期，需要补充说明", "闲鱼", "二手平台", now.AddHours(2), cancellationToken);
            await InsertOrderAsync(connection, 3, "企业年会礼盒", OrderStatus.Won, 9600, "已收定金，准备排产", "微信", "老客复购", now.AddDays(3), cancellationToken);
            await InsertOrderAsync(connection, 3, "复购节日卡片", OrderStatus.PendingFollowUp, 2200, "提醒客户确认文案", "微信", "老客复购", now.AddDays(5), cancellationToken);
            await InsertOrderAsync(connection, 4, "小红书来图定制", OrderStatus.PendingQuote, 0, "需要拆分基础版和升级版", "小红书", "内容咨询", now.AddDays(1), cancellationToken);
            await InsertOrderAsync(connection, 5, "门店加急维修", OrderStatus.Closed, 420, "已完成交付，等待评价", "线下", "门店咨询", null, cancellationToken);
        }

        if (await CountAsync(connection, "ReplyTemplates", cancellationToken) == 0)
        {
            var now = DateTime.Now;
            await InsertTemplateAsync(connection, "首次回应", "新咨询", "你好，我先帮你记录需求。方便的话请发一下尺寸、数量、预算和期望交付时间。", true, "通用", now, cancellationToken);
            await InsertTemplateAsync(connection, "补充信息", "待补信息", "这边还差几个信息才能准确报价：使用场景、具体尺寸、数量和是否需要加急。", true, "通用", now, cancellationToken);
            await InsertTemplateAsync(connection, "报价说明", "待报价", "我会按基础方案和升级方案分别整理报价，方便你对比选择。", true, "通用", now, cancellationToken);
            await InsertTemplateAsync(connection, "已报价跟进", "已报价", "报价已经发你了，如果有预算范围或细节调整，我可以继续帮你改方案。", true, "通用", now, cancellationToken);
            await InsertTemplateAsync(connection, "定金确认", "待确认", "确认方案后可以先付定金锁定排期，我这边同步开始准备材料。", false, "微信", now, cancellationToken);
            await InsertTemplateAsync(connection, "排期说明", "已成交", "目前排期已确认，我会在关键节点同步进度。", false, "通用", now, cancellationToken);
            await InsertTemplateAsync(connection, "加急说明", "加急", "加急可以安排，但需要确认当前排期和加急费用，我先帮你核一下。", false, "通用", now, cancellationToken);
            await InsertTemplateAsync(connection, "闲鱼回复", "闲鱼", "你好，详情我看到了，可以做。你把具体尺寸和期望效果再发我一下。", true, "闲鱼", now, cancellationToken);
            await InsertTemplateAsync(connection, "复购提醒", "复购", "之前的订单已经过了一段时间，如果近期还需要补单或升级，我可以直接按旧方案给你处理。", false, "微信", now, cancellationToken);
            await InsertTemplateAsync(connection, "结束语", "通用", "收到，我这边先记录，确认后会第一时间同步给你。", false, "通用", now, cancellationToken);
        }

        await EnsureDefaultAppSettingsAsync(connection, cancellationToken);
    }

    private static async Task<int> CountAsync(SqliteConnection connection, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(1) FROM {table};";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    private static async Task InsertCustomerAsync(SqliteConnection connection, string name, string platform, string channel, string handle, string phone, string remark, string externalId, DateTime now, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Customers (Name, SourcePlatform, Channel, ContactHandle, Phone, Remark, ExternalId, RawPayload, CreatedAt, UpdatedAt)
            VALUES ($name, $platform, $channel, $handle, $phone, $remark, $externalId, '', $createdAt, $updatedAt);
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$platform", platform);
        command.Parameters.AddWithValue("$channel", channel);
        command.Parameters.AddWithValue("$handle", handle);
        command.Parameters.AddWithValue("$phone", phone);
        command.Parameters.AddWithValue("$remark", remark);
        command.Parameters.AddWithValue("$externalId", externalId);
        command.Parameters.AddWithValue("$createdAt", now.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertOrderAsync(SqliteConnection connection, int customerId, string title, OrderStatus status, decimal amount, string requirement, string platform, string channel, DateTime? nextFollowUpAt, CancellationToken cancellationToken)
    {
        var now = DateTime.Now;
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Orders (CustomerId, Title, Status, Amount, Requirement, SourcePlatform, Channel, ExternalId, RawPayload, NextFollowUpAt, CreatedAt, UpdatedAt)
            VALUES ($customerId, $title, $status, $amount, $requirement, $platform, $channel, '', '', $nextFollowUpAt, $createdAt, $updatedAt);
            """;
        command.Parameters.AddWithValue("$customerId", customerId);
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$status", (int)status);
        command.Parameters.AddWithValue("$amount", amount);
        command.Parameters.AddWithValue("$requirement", requirement);
        command.Parameters.AddWithValue("$platform", platform);
        command.Parameters.AddWithValue("$channel", channel);
        command.Parameters.AddWithValue("$nextFollowUpAt", nextFollowUpAt is null ? DBNull.Value : nextFollowUpAt.Value.ToString("O"));
        command.Parameters.AddWithValue("$createdAt", now.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertTemplateAsync(SqliteConnection connection, string title, string scene, string content, bool favorite, string platform, DateTime now, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ReplyTemplates (Title, Scene, Content, IsFavorite, SourcePlatform, CreatedAt, UpdatedAt)
            VALUES ($title, $scene, $content, $favorite, $platform, $createdAt, $updatedAt);
            """;
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$scene", scene);
        command.Parameters.AddWithValue("$content", content);
        command.Parameters.AddWithValue("$favorite", favorite ? 1 : 0);
        command.Parameters.AddWithValue("$platform", platform);
        command.Parameters.AddWithValue("$createdAt", now.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureDefaultAppSettingsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var defaults = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AppSettingKeys.MainHotkey] = "Ctrl+Alt+O",
            [AppSettingKeys.FloatingHotkey] = "Ctrl+Alt+R",
            [AppSettingKeys.GlobalSearchHotkey] = "Ctrl+Alt+F",
            [AppSettingKeys.TodayWorkbenchHotkey] = "Ctrl+Alt+W",
            [AppSettingKeys.CopyOrderSummaryHotkey] = "Ctrl+Shift+C",
            [AppSettingKeys.OpenProductionSheetHotkey] = "Ctrl+Shift+P",
            [AppSettingKeys.MarkOrderExceptionHotkey] = "Ctrl+Shift+E",
            [AppSettingKeys.AdvanceFulfillmentHotkey] = "Ctrl+Shift+N",
            [AppSettingKeys.OpenCustomerProfileHotkey] = "Ctrl+Shift+F",
            [AppSettingKeys.NewCustomerNoteHotkey] = "Ctrl+Shift+M",
            [AppSettingKeys.CopyCustomerPreferenceSummaryHotkey] = "Ctrl+Shift+Y",
            [AppSettingKeys.FloatingBallEnabled] = "false",
            [AppSettingKeys.ShowFloatingWindowOnStartup] = "false",
            [AppSettingKeys.FloatingBallOpacity] = "0.82",
            [AppSettingKeys.StartMinimizedToTray] = "false",
            [AppSettingKeys.StartupDefaultSection] = "工作台",
            [AppSettingKeys.RememberLastSection] = "false",
            [AppSettingKeys.LastSection] = "工作台",
            [AppSettingKeys.StartWithWindows] = "false",
            [AppSettingKeys.RememberWindowBounds] = "false",
            [AppSettingKeys.DefaultWindowMode] = "普通",
            [AppSettingKeys.SidebarDefaultExpanded] = "true",
            [AppSettingKeys.FontSizePreset] = "标准",
            [AppSettingKeys.ShowWindowsScaleHint] = "true",
            [AppSettingKeys.ThemeMode] = "浅色",
            [AppSettingKeys.AccentColor] = "默认绿",
            [AppSettingKeys.EnableLightAnimation] = "false",
            [AppSettingKeys.BackupDirectory] = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Orderly",
                "Backups"),
            [AppSettingKeys.AutoBackupEnabled] = "false",
            [AppSettingKeys.AutoBackupFrequency] = "手动",
            [AppSettingKeys.BackupRetentionCount] = "10",
            [AppSettingKeys.MaskPhoneByDefault] = "true",
            [AppSettingKeys.MaskAddressByDefault] = "true",
            [AppSettingKeys.IncludeSensitiveInExport] = "false",
            [AppSettingKeys.MaskOrderSummaryOnCopy] = "true",
            [AppSettingKeys.OperationLogEnabled] = "true",
            [AppSettingKeys.OperationLogRetentionDays] = "180",
            [AppSettingKeys.AiAssistantEnabled] = "false",
            [AppSettingKeys.AiAllowOrderContext] = "false",
            [AppSettingKeys.AiAllowCustomerProfileContext] = "false",
            [AppSettingKeys.AiDefaultModel] = string.Empty,
            [AppSettingKeys.AiTimeoutSeconds] = "15",
            [AppSettingKeys.AiAutoRedactBeforeSend] = "true",
            [AppSettingKeys.AiBlockPhone] = "true",
            [AppSettingKeys.AiBlockFullAddress] = "true",
            [AppSettingKeys.AiBlockPaymentTransactionId] = "true",
            [AppSettingKeys.AiReplyTone] = "简洁",
            [AppSettingKeys.AiReplyLength] = "标准",
            [AppSettingKeys.AiAutoGenerateOrderSummary] = "false",
            [AppSettingKeys.NotifyNewOrder] = "true",
            [AppSettingKeys.NotifyExceptionOrder] = "true",
            [AppSettingKeys.NotifyOverdueUnhandled] = "true",
            [AppSettingKeys.NotifySyncFailed] = "true",
            [AppSettingKeys.NotifyPaidUnconfirmedHours] = "24",
            [AppSettingKeys.NotifyPendingProductionHours] = "24",
            [AppSettingKeys.NotifyPendingShipmentHours] = "48",
            [AppSettingKeys.NotifyMissingAddress] = "true",
            [AppSettingKeys.NotifyDoNotDisturbRange] = "22:00-08:00",
            [AppSettingKeys.NotifyHighPriorityOnly] = "false",
            [AppSettingKeys.DebugModeEnabled] = "false"
        };

        foreach (var setting in defaults)
        {
            await InsertSettingIfMissingAsync(connection, setting.Key, setting.Value, cancellationToken);
        }
    }

    private static async Task InsertSettingIfMissingAsync(SqliteConnection connection, string key, string value, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AppSettings (Key, Value)
            VALUES ($key, $value)
            ON CONFLICT(Key) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
