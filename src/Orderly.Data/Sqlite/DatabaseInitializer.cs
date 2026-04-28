using Microsoft.Data.Sqlite;
using Orderly.Core.Models;

namespace Orderly.Data.Sqlite;

public sealed class DatabaseInitializer
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public DatabaseInitializer(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_connectionFactory.DatabasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS Customers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Status INTEGER NOT NULL DEFAULT 0,
                Priority INTEGER NOT NULL DEFAULT 1,
                SourcePlatform TEXT NOT NULL DEFAULT '',
                Channel TEXT NOT NULL DEFAULT '',
                ContactHandle TEXT NOT NULL DEFAULT '',
                Phone TEXT NOT NULL DEFAULT '',
                Remark TEXT NOT NULL DEFAULT '',
                ExternalId TEXT NOT NULL DEFAULT '',
                RawPayload TEXT NOT NULL DEFAULT '',
                LastContactAt TEXT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                DeletedAt TEXT NULL,
                RemoteId TEXT NOT NULL DEFAULT '',
                IsSynced INTEGER NOT NULL DEFAULT 0,
                Version INTEGER NOT NULL DEFAULT 1
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS Deals (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerId INTEGER NOT NULL,
                Title TEXT NOT NULL,
                Stage INTEGER NOT NULL,
                EstimatedAmount REAL NOT NULL DEFAULT 0,
                Requirement TEXT NOT NULL DEFAULT '',
                SourcePlatform TEXT NOT NULL DEFAULT '',
                Channel TEXT NOT NULL DEFAULT '',
                ExpectedCloseAt TEXT NULL,
                ClosedAt TEXT NULL,
                LostReason TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                DeletedAt TEXT NULL,
                RemoteId TEXT NOT NULL DEFAULT '',
                IsSynced INTEGER NOT NULL DEFAULT 0,
                Version INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (CustomerId) REFERENCES Customers(Id) ON DELETE CASCADE
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS Orders (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerId INTEGER NOT NULL,
                DealId INTEGER NULL,
                Title TEXT NOT NULL,
                Status INTEGER NOT NULL,
                Amount REAL NOT NULL DEFAULT 0,
                Requirement TEXT NOT NULL DEFAULT '',
                SourcePlatform TEXT NOT NULL DEFAULT '',
                Channel TEXT NOT NULL DEFAULT '',
                ExternalId TEXT NOT NULL DEFAULT '',
                RawPayload TEXT NOT NULL DEFAULT '',
                NextFollowUpAt TEXT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                DeletedAt TEXT NULL,
                RemoteId TEXT NOT NULL DEFAULT '',
                IsSynced INTEGER NOT NULL DEFAULT 0,
                Version INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (CustomerId) REFERENCES Customers(Id) ON DELETE CASCADE,
                FOREIGN KEY (DealId) REFERENCES Deals(Id) ON DELETE SET NULL
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS FollowUps (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerId INTEGER NOT NULL,
                DealId INTEGER NULL,
                OrderId INTEGER NULL,
                Title TEXT NOT NULL,
                Content TEXT NOT NULL DEFAULT '',
                Status INTEGER NOT NULL,
                ScheduledAt TEXT NOT NULL,
                CompletedAt TEXT NULL,
                ReminderAt TEXT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                DeletedAt TEXT NULL,
                RemoteId TEXT NOT NULL DEFAULT '',
                IsSynced INTEGER NOT NULL DEFAULT 0,
                Version INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (CustomerId) REFERENCES Customers(Id) ON DELETE CASCADE,
                FOREIGN KEY (DealId) REFERENCES Deals(Id) ON DELETE SET NULL,
                FOREIGN KEY (OrderId) REFERENCES Orders(Id) ON DELETE SET NULL
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS CustomerNotes (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerId INTEGER NOT NULL,
                DealId INTEGER NULL,
                OrderId INTEGER NULL,
                Type INTEGER NOT NULL,
                Content TEXT NOT NULL DEFAULT '',
                IsPinned INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                DeletedAt TEXT NULL,
                RemoteId TEXT NOT NULL DEFAULT '',
                IsSynced INTEGER NOT NULL DEFAULT 0,
                Version INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (CustomerId) REFERENCES Customers(Id) ON DELETE CASCADE,
                FOREIGN KEY (DealId) REFERENCES Deals(Id) ON DELETE SET NULL,
                FOREIGN KEY (OrderId) REFERENCES Orders(Id) ON DELETE SET NULL
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS ActivityLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Type INTEGER NOT NULL,
                CustomerId INTEGER NULL,
                DealId INTEGER NULL,
                OrderId INTEGER NULL,
                Title TEXT NOT NULL DEFAULT '',
                Description TEXT NOT NULL DEFAULT '',
                Operator TEXT NOT NULL DEFAULT '',
                MetadataJson TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                DeletedAt TEXT NULL,
                RemoteId TEXT NOT NULL DEFAULT '',
                IsSynced INTEGER NOT NULL DEFAULT 0,
                Version INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (CustomerId) REFERENCES Customers(Id) ON DELETE SET NULL,
                FOREIGN KEY (DealId) REFERENCES Deals(Id) ON DELETE SET NULL,
                FOREIGN KEY (OrderId) REFERENCES Orders(Id) ON DELETE SET NULL
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS ConversationMessages (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerId INTEGER NOT NULL,
                OrderId INTEGER NULL,
                DealId INTEGER NULL,
                Direction INTEGER NOT NULL,
                Channel INTEGER NOT NULL,
                SenderName TEXT NOT NULL DEFAULT '',
                Content TEXT NOT NULL DEFAULT '',
                MessageTime TEXT NOT NULL,
                SourceMessageId TEXT NOT NULL DEFAULT '',
                MetadataJson TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                DeletedAt TEXT NULL,
                RemoteId TEXT NOT NULL DEFAULT '',
                IsSynced INTEGER NOT NULL DEFAULT 0,
                Version INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (CustomerId) REFERENCES Customers(Id) ON DELETE CASCADE,
                FOREIGN KEY (OrderId) REFERENCES Orders(Id) ON DELETE SET NULL,
                FOREIGN KEY (DealId) REFERENCES Deals(Id) ON DELETE SET NULL
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS AiSuggestions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerId INTEGER NOT NULL,
                OrderId INTEGER NULL,
                MessageId INTEGER NULL,
                SuggestionText TEXT NOT NULL DEFAULT '',
                Reason TEXT NOT NULL DEFAULT '',
                Confidence REAL NULL,
                Status INTEGER NOT NULL,
                MetadataJson TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                DeletedAt TEXT NULL,
                RemoteId TEXT NOT NULL DEFAULT '',
                IsSynced INTEGER NOT NULL DEFAULT 0,
                Version INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (CustomerId) REFERENCES Customers(Id) ON DELETE CASCADE,
                FOREIGN KEY (OrderId) REFERENCES Orders(Id) ON DELETE SET NULL,
                FOREIGN KEY (MessageId) REFERENCES ConversationMessages(Id) ON DELETE SET NULL
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS OcrResults (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerId INTEGER NULL,
                OrderId INTEGER NULL,
                SourcePath TEXT NOT NULL DEFAULT '',
                SourceName TEXT NOT NULL DEFAULT '',
                ExtractedText TEXT NOT NULL DEFAULT '',
                Status INTEGER NOT NULL,
                ErrorMessage TEXT NOT NULL DEFAULT '',
                MetadataJson TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                DeletedAt TEXT NULL,
                RemoteId TEXT NOT NULL DEFAULT '',
                IsSynced INTEGER NOT NULL DEFAULT 0,
                Version INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (CustomerId) REFERENCES Customers(Id) ON DELETE SET NULL,
                FOREIGN KEY (OrderId) REFERENCES Orders(Id) ON DELETE SET NULL
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS SyncRecords (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                EntityType TEXT NOT NULL DEFAULT '',
                EntityId INTEGER NOT NULL,
                RemoteId TEXT NOT NULL DEFAULT '',
                SyncStatus INTEGER NOT NULL,
                LastSyncedAt TEXT NULL,
                ErrorMessage TEXT NOT NULL DEFAULT '',
                MetadataJson TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                DeletedAt TEXT NULL,
                IsSynced INTEGER NOT NULL DEFAULT 0,
                Version INTEGER NOT NULL DEFAULT 1
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS PriceAdjustments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerId INTEGER NOT NULL,
                DealId INTEGER NULL,
                OrderId INTEGER NULL,
                OriginalAmount REAL NOT NULL DEFAULT 0,
                AdjustedAmount REAL NOT NULL DEFAULT 0,
                Reason TEXT NOT NULL DEFAULT '',
                Status INTEGER NOT NULL,
                RequestedBy TEXT NOT NULL DEFAULT '',
                ApprovedBy TEXT NOT NULL DEFAULT '',
                ApprovedAt TEXT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                DeletedAt TEXT NULL,
                RemoteId TEXT NOT NULL DEFAULT '',
                IsSynced INTEGER NOT NULL DEFAULT 0,
                Version INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (CustomerId) REFERENCES Customers(Id) ON DELETE CASCADE,
                FOREIGN KEY (DealId) REFERENCES Deals(Id) ON DELETE SET NULL,
                FOREIGN KEY (OrderId) REFERENCES Orders(Id) ON DELETE SET NULL
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS ReplyTemplates (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                Scene TEXT NOT NULL DEFAULT '',
                Content TEXT NOT NULL,
                IsFavorite INTEGER NOT NULL DEFAULT 0,
                SourcePlatform TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS AppSettings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
            """, cancellationToken);

        await EnsureSchemaAsync(connection, cancellationToken);
        await SeedAsync(connection, cancellationToken);
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(connection, "Customers", "Status", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "Customers", "Priority", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
        await EnsureColumnAsync(connection, "Customers", "LastContactAt", "TEXT NULL", cancellationToken);
        await EnsureEntityColumnsAsync(connection, "Customers", cancellationToken);

        await EnsureColumnAsync(connection, "Orders", "DealId", "INTEGER NULL", cancellationToken);
        await EnsureEntityColumnsAsync(connection, "Orders", cancellationToken);
    }

    private static async Task EnsureEntityColumnsAsync(SqliteConnection connection, string table, CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(connection, table, "DeletedAt", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(connection, table, "RemoteId", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, table, "IsSynced", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, table, "Version", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
    }

    private static async Task EnsureColumnAsync(SqliteConnection connection, string table, string column, string definition, CancellationToken cancellationToken)
    {
        if (await ColumnExistsAsync(connection, table, column, cancellationToken))
        {
            return;
        }

        await ExecuteAsync(connection, $"ALTER TABLE {table} ADD COLUMN {column} {definition};", cancellationToken);
    }

    private static async Task<bool> ColumnExistsAsync(SqliteConnection connection, string table, string column, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

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

        if (await CountAsync(connection, "AppSettings", cancellationToken) == 0)
        {
            await UpsertSettingAsync(connection, "MainHotkey", "Ctrl+Alt+O", cancellationToken);
            await UpsertSettingAsync(connection, "FloatingHotkey", "Ctrl+Alt+R", cancellationToken);
            await UpsertSettingAsync(connection, "ShowFloatingWindowOnStartup", "false", cancellationToken);
            await UpsertSettingAsync(connection, "StartMinimizedToTray", "false", cancellationToken);
        }
    }

    private static async Task<int> CountAsync(SqliteConnection connection, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(1) FROM {table};";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string commandText, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
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

    private static async Task UpsertSettingAsync(SqliteConnection connection, string key, string value, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AppSettings (Key, Value)
            VALUES ($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
