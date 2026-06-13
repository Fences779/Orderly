using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Migration;
using Orderly.Data.Commerce.Repositories;
using Orderly.Data.Services;
using Orderly.Data.Sqlite;
using Xunit;

namespace Orderly.Tests.Commerce;

/// <summary>
/// End-to-end integration tests for the legacy CRM migration routine
/// (<see cref="CommerceLegacyMigrationService"/>), exercised against real, unencrypted temp SQLite
/// databases — a representative legacy source database and a fresh target Commerce database.
///
/// These tests verify the criterion-4 mappings and the non-destructive / idempotent guarantees of
/// the migration end-to-end (the testable surface called out by Requirement 3.10):
/// <list type="bullet">
///   <item><description>
///   <b>Mappings (Req 3.4):</b> legacy <c>Customer→Customer</c>, <c>Order→Order</c>,
///   <c>Deal→Order|BusinessTask|note</c> by the documented stage rule, <c>FollowUp→BusinessTask</c>,
///   <c>CustomerNote→note</c>; the legacy <c>ActivityLog</c> table is retained unchanged.
///   </description></item>
///   <item><description>
///   <b>Remote data is not migrated (Req 3.5):</b> a legacy industry-specific remote table is left
///   unread and unmodified, and contributes nothing to the migrated record set.
///   </description></item>
///   <item><description>
///   <b>Non-destructive (Req 3.7 / 3.10):</b> every source record remains present and byte-for-byte
///   unchanged after migration, and a complete source backup is created before any change.
///   </description></item>
///   <item><description>
///   <b>Idempotent (Req 3.6 / 3.10):</b> running the migration twice yields a target record set
///   identical to running it once, with no duplicated migrated records.
///   </description></item>
/// </list>
///
/// **Validates: Requirements 3.4, 3.10**
/// </summary>
public class LegacyMigrationMappingIntegrationTests
{
    private static readonly Guid WorkspaceId = new("0f5d6f1a-2b3c-4d5e-8f90-112233445566");

    // Legacy deal stage discriminants (mirror the documented mapping in MapDealStage).
    private const int DealStageQualifiedOpen = 1; // → BusinessTask
    private const int DealStageWon = 4;            // → Order
    private const int DealStageLost = 5;           // → note

    // Legacy follow-up / customer-note discriminants used only to populate representative rows.
    private const int FollowUpStatusPending = 0;
    private const int CustomerNoteTypeGeneral = 0;

    [Fact]
    public void Migration_maps_every_legacy_entity_per_criterion_four()
    {
        WithTempWorkspace((sourcePath, targetPath, backupDir) =>
        {
            CreateLegacySourceDatabase(sourcePath);
            CommerceLegacyMigrationService service = CreateService(sourcePath, targetPath, backupDir);

            CommerceLegacyMigrationResult result = service.MigrateAsync().GetAwaiter().GetResult();

            Assert.Equal(CommerceLegacyMigrationOutcome.Completed, result.Outcome);
            Assert.Equal(CommerceLegacyMigrationOutcomeTokens.MigrationCompleted, result.OutcomeToken);

            // --- Per-target breakdown (Req 3.4): ---
            // Customer→Customer        : 2 legacy customers              => 2
            // Order→Order              : 1 legacy order                  => 1
            // Deal→Order               : 1 Won deal                      => +1 Order  (Order total = 2)
            // Deal→BusinessTask        : 1 open deal                     => +1 task
            // FollowUp→BusinessTask    : 1 follow-up                     => +1 task   (BusinessTask total = 2)
            // Deal→note                : 1 Lost deal                     => +1 note
            // CustomerNote→note        : 1 customer note                 => +1 note   (note total = 2)
            Assert.Equal(2, result.CountsByTarget["Customer"]);
            Assert.Equal(2, result.CountsByTarget["Order"]);
            Assert.Equal(2, result.CountsByTarget["BusinessTask"]);
            Assert.Equal(2, result.CountsByTarget["note"]);
            Assert.Equal(8, result.MigratedRecordCount);

            // --- Customer→Customer (Req 3.4): contact fields carried across. ---
            IReadOnlyList<Customer> customers = ListCustomers(targetPath);
            Assert.Equal(2, customers.Count);
            Customer? customerA = customers.SingleOrDefault(c => c.Name == "客户 A");
            Assert.NotNull(customerA);
            Assert.Equal("13800000001", customerA!.Phone);
            Assert.Equal("wechat-a", customerA.WeChat);
            Assert.All(customers, c => Assert.Equal(WorkspaceId, c.WorkspaceId));
            Assert.All(customers, c => Assert.Equal("customer", RecordKind(c.CustomFieldsJson)));

            // --- Order→Order and Won-Deal→Order (Req 3.4). ---
            IReadOnlyList<Order> orders = ListOrders(targetPath);
            Assert.Equal(2, orders.Count);
            Order? fromOrder = orders.SingleOrDefault(o => o.OrderNo == "LEGACY-O-1");
            Order? fromWonDeal = orders.SingleOrDefault(o => o.OrderNo == "LEGACY-D-1");
            Assert.NotNull(fromOrder);
            Assert.NotNull(fromWonDeal);
            Assert.Equal("order", RecordKind(fromOrder!.CustomFieldsJson));
            Assert.Equal("order", RecordKind(fromWonDeal!.CustomFieldsJson));
            Assert.Equal("Won", LegacyDealStageMarker(fromWonDeal.CustomFieldsJson));
            // Won deal links back to its migrated customer.
            Assert.Equal(customerA.Id, fromWonDeal.CustomerId);

            // --- FollowUp→BusinessTask, open-Deal→BusinessTask, and note targets (Req 3.4). ---
            // Notes are represented as BusinessTask rows tagged recordKind=note, so the
            // CommerceBusinessTasks table holds both genuine tasks and notes.
            IReadOnlyList<BusinessTask> tasks = ListTasks(targetPath);
            Assert.Equal(4, tasks.Count);

            List<BusinessTask> genuineTasks = tasks.Where(t => RecordKind(t.CustomFieldsJson) == "task").ToList();
            List<BusinessTask> notes = tasks.Where(t => RecordKind(t.CustomFieldsJson) == "note").ToList();
            Assert.Equal(2, genuineTasks.Count);
            Assert.Equal(2, notes.Count);

            // The follow-up maps to a genuine BusinessTask linked to its customer.
            Assert.Contains(genuineTasks, t => t.CustomerId == customerA.Id && t.Title == "首次跟进");

            // The two notes are sourced from the Lost deal and the customer note respectively.
            Assert.Contains(notes, t => LegacySourceMarker(t.CustomFieldsJson) == "Deal");
            Assert.Contains(notes, t => LegacySourceMarker(t.CustomFieldsJson) == "CustomerNote");

            // --- ActivityLog retained unchanged (Req 3.4): never read, never transformed. ---
            // No ActivityLog content leaks into any migrated target table.
            Assert.DoesNotContain(orders, o => (o.Note ?? string.Empty).Contains("活动日志", StringComparison.Ordinal));
            Assert.DoesNotContain(tasks, t => (t.Title + (t.Description ?? string.Empty)).Contains("活动日志", StringComparison.Ordinal));

            return true;
        });
    }

    [Fact]
    public void Migration_is_non_destructive_and_creates_a_source_backup()
    {
        WithTempWorkspace((sourcePath, targetPath, backupDir) =>
        {
            CreateLegacySourceDatabase(sourcePath);

            // Snapshot every source table (including ActivityLogs and the remote table) before migrating.
            Dictionary<string, string> before = SnapshotAllSourceTables(sourcePath);

            CommerceLegacyMigrationService service = CreateService(sourcePath, targetPath, backupDir);
            CommerceLegacyMigrationResult result = service.MigrateAsync().GetAwaiter().GetResult();

            Assert.Equal(CommerceLegacyMigrationOutcome.Completed, result.Outcome);

            // A complete backup of the source was created before any change (Req 3.7).
            Assert.False(string.IsNullOrWhiteSpace(result.BackupPath));
            Assert.True(File.Exists(result.BackupPath!));

            // Every source record remains present and byte-for-byte unchanged (Req 3.7 / 3.10).
            Dictionary<string, string> after = SnapshotAllSourceTables(sourcePath);
            Assert.Equal(before.Keys.OrderBy(k => k), after.Keys.OrderBy(k => k));
            foreach (string table in before.Keys)
            {
                Assert.Equal(before[table], after[table]);
            }

            // ActivityLog and the legacy remote table are retained unchanged (Req 3.4 / 3.5).
            Assert.Equal(before["ActivityLogs"], after["ActivityLogs"]);
            Assert.Equal(before["RemoteSyncStates"], after["RemoteSyncStates"]);

            return true;
        });
    }

    [Fact]
    public void Migration_does_not_read_or_migrate_legacy_remote_data()
    {
        WithTempWorkspace((sourcePath, targetPath, backupDir) =>
        {
            CreateLegacySourceDatabase(sourcePath);
            CommerceLegacyMigrationService service = CreateService(sourcePath, targetPath, backupDir);

            CommerceLegacyMigrationResult result = service.MigrateAsync().GetAwaiter().GetResult();

            // The migrated count equals exactly the mapped CRM records; the remote table's rows
            // (RemoteSyncStates) never contribute to the target set (Req 3.5).
            Assert.Equal(8, result.MigratedRecordCount);

            // None of the migrated records carries the remote payload sentinel.
            IReadOnlyList<Order> orders = ListOrders(targetPath);
            IReadOnlyList<BusinessTask> tasks = ListTasks(targetPath);
            IReadOnlyList<Customer> customers = ListCustomers(targetPath);

            const string remoteSentinel = "REMOTE_PAYLOAD_SENTINEL";
            Assert.DoesNotContain(customers, c => (c.CustomFieldsJson ?? string.Empty).Contains(remoteSentinel, StringComparison.Ordinal));
            Assert.DoesNotContain(orders, o => (o.CustomFieldsJson ?? string.Empty).Contains(remoteSentinel, StringComparison.Ordinal));
            Assert.DoesNotContain(tasks, t => (t.CustomFieldsJson ?? string.Empty).Contains(remoteSentinel, StringComparison.Ordinal));

            return true;
        });
    }

    [Fact]
    public void Migration_is_idempotent_across_repeated_runs()
    {
        WithTempWorkspace((sourcePath, targetPath, backupDir) =>
        {
            CreateLegacySourceDatabase(sourcePath);
            CommerceLegacyMigrationService service = CreateService(sourcePath, targetPath, backupDir);

            CommerceLegacyMigrationResult first = service.MigrateAsync().GetAwaiter().GetResult();
            HashSet<Guid> customersAfterFirst = ListCustomers(targetPath).Select(c => c.Id).ToHashSet();
            HashSet<Guid> ordersAfterFirst = ListOrders(targetPath).Select(o => o.Id).ToHashSet();
            HashSet<Guid> tasksAfterFirst = ListTasks(targetPath).Select(t => t.Id).ToHashSet();

            // Second run against the same source and target must produce no duplicates (Req 3.6).
            CommerceLegacyMigrationResult second = service.MigrateAsync().GetAwaiter().GetResult();

            Assert.Equal(CommerceLegacyMigrationOutcome.Completed, second.Outcome);
            Assert.Equal(first.MigratedRecordCount, second.MigratedRecordCount);

            HashSet<Guid> customersAfterSecond = ListCustomers(targetPath).Select(c => c.Id).ToHashSet();
            HashSet<Guid> ordersAfterSecond = ListOrders(targetPath).Select(o => o.Id).ToHashSet();
            HashSet<Guid> tasksAfterSecond = ListTasks(targetPath).Select(t => t.Id).ToHashSet();

            // The target record set is identical to running once: same ids, same counts, no dupes.
            Assert.Equal(customersAfterFirst, customersAfterSecond);
            Assert.Equal(ordersAfterFirst, ordersAfterSecond);
            Assert.Equal(tasksAfterFirst, tasksAfterSecond);

            Assert.Equal(2, customersAfterSecond.Count);
            Assert.Equal(2, ordersAfterSecond.Count);
            Assert.Equal(4, tasksAfterSecond.Count);

            return true;
        });
    }

    [Fact]
    public void Migration_aborts_without_change_when_the_source_backup_fails()
    {
        WithTempWorkspace((sourcePath, targetPath, _) =>
        {
            CreateLegacySourceDatabase(sourcePath);
            Dictionary<string, string> before = SnapshotAllSourceTables(sourcePath);

            var service = new CommerceLegacyMigrationService(
                new SqliteConnectionFactory(sourcePath),
                new SqliteConnectionFactory(targetPath),
                WorkspaceId,
                new FailingBackup());

            CommerceLegacyMigrationResult result = service.MigrateAsync().GetAwaiter().GetResult();

            // Backup-first guarantee: a failed backup aborts before any change (Req 3.8 / 3.10).
            Assert.Equal(CommerceLegacyMigrationOutcome.BackupFailedMigrationAborted, result.Outcome);
            Assert.Equal(CommerceLegacyMigrationOutcomeTokens.BackupFailedMigrationAborted, result.OutcomeToken);
            Assert.Equal(0, result.MigratedRecordCount);
            Assert.Null(result.BackupPath);

            // Source data is left entirely unmodified.
            Dictionary<string, string> after = SnapshotAllSourceTables(sourcePath);
            foreach (string table in before.Keys)
            {
                Assert.Equal(before[table], after[table]);
            }

            // No Commerce entity tables were created because the run aborted before schema init.
            // (An abort still records a migration-log entry per Req 3.8/3.9, so we assert on the
            // entity tables specifically rather than every Commerce-prefixed object.)
            Assert.False(CommerceEntityTablesExist(targetPath));

            return true;
        });
    }

    // ----------------------------------------------------------------------------------------------
    // Service construction
    // ----------------------------------------------------------------------------------------------

    private static CommerceLegacyMigrationService CreateService(string sourcePath, string targetPath, string backupDir)
        => new(
            new SqliteConnectionFactory(sourcePath),
            new SqliteConnectionFactory(targetPath),
            WorkspaceId,
            new CommerceSourceFileBackup(backupDir));

    /// <summary>A backup strategy that always fails, used to assert the abort-on-backup-failure path.</summary>
    private sealed class FailingBackup : ICommerceSourceBackup
    {
        public Task<CommerceSourceBackupResult> CreateBackupAsync(string sourceDatabasePath, CancellationToken cancellationToken = default)
            => Task.FromResult(CommerceSourceBackupResult.Failure("测试注入：备份失败。"));
    }

    // ----------------------------------------------------------------------------------------------
    // Target reads
    // ----------------------------------------------------------------------------------------------

    private static IReadOnlyList<Customer> ListCustomers(string targetPath)
        => new CommerceCustomerRepository(new SqliteConnectionFactory(targetPath))
            .GetAllAsync().GetAwaiter().GetResult();

    private static IReadOnlyList<Order> ListOrders(string targetPath)
        => new CommerceOrderRepository(new SqliteConnectionFactory(targetPath))
            .GetAllAsync().GetAwaiter().GetResult();

    private static IReadOnlyList<BusinessTask> ListTasks(string targetPath)
        => new BusinessTaskRepository(new SqliteConnectionFactory(targetPath))
            .GetAllAsync().GetAwaiter().GetResult();

    private static string? RecordKind(string? customFieldsJson)
        => ReadMarkerString(customFieldsJson, "recordKind");

    private static string? LegacySourceMarker(string? customFieldsJson)
        => ReadMarkerString(customFieldsJson, "legacySource");

    private static string? LegacyDealStageMarker(string? customFieldsJson)
        => ReadMarkerString(customFieldsJson, "legacyDealStage");

    private static string? ReadMarkerString(string? customFieldsJson, string property)
    {
        if (string.IsNullOrWhiteSpace(customFieldsJson))
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(customFieldsJson);
        return document.RootElement.TryGetProperty(property, out JsonElement value)
            ? value.GetString()
            : null;
    }

    // ----------------------------------------------------------------------------------------------
    // Legacy source database creation
    // ----------------------------------------------------------------------------------------------

    private static void CreateLegacySourceDatabase(string sourcePath)
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = sourcePath }.ToString());
        connection.Open();

        Execute(connection, """
            CREATE TABLE Customers (
                Id INTEGER PRIMARY KEY,
                Name TEXT NOT NULL,
                Phone TEXT NULL,
                ContactHandle TEXT NULL,
                Remark TEXT NULL,
                CreatedAt TEXT NULL,
                UpdatedAt TEXT NULL,
                DeletedAt TEXT NULL
            );
            """);

        Execute(connection, """
            CREATE TABLE Orders (
                Id INTEGER PRIMARY KEY,
                CustomerId INTEGER NOT NULL,
                Title TEXT NULL,
                Status INTEGER NOT NULL,
                Amount REAL NOT NULL,
                Requirement TEXT NULL,
                CreatedAt TEXT NULL,
                UpdatedAt TEXT NULL,
                DeletedAt TEXT NULL
            );
            """);

        Execute(connection, """
            CREATE TABLE Deals (
                Id INTEGER PRIMARY KEY,
                CustomerId INTEGER NOT NULL,
                Title TEXT NULL,
                Stage INTEGER NOT NULL,
                EstimatedAmount REAL NOT NULL,
                Requirement TEXT NULL,
                ExpectedCloseAt TEXT NULL,
                ClosedAt TEXT NULL,
                CreatedAt TEXT NULL,
                UpdatedAt TEXT NULL,
                DeletedAt TEXT NULL
            );
            """);

        Execute(connection, """
            CREATE TABLE FollowUps (
                Id INTEGER PRIMARY KEY,
                CustomerId INTEGER NOT NULL,
                OrderId INTEGER NULL,
                Title TEXT NULL,
                Content TEXT NULL,
                Status INTEGER NOT NULL,
                ScheduledAt TEXT NULL,
                CompletedAt TEXT NULL,
                CreatedAt TEXT NULL,
                UpdatedAt TEXT NULL,
                DeletedAt TEXT NULL
            );
            """);

        Execute(connection, """
            CREATE TABLE CustomerNotes (
                Id INTEGER PRIMARY KEY,
                CustomerId INTEGER NOT NULL,
                OrderId INTEGER NULL,
                Type INTEGER NOT NULL,
                Content TEXT NULL,
                IsPinned INTEGER NOT NULL,
                CreatedAt TEXT NULL,
                UpdatedAt TEXT NULL,
                DeletedAt TEXT NULL
            );
            """);

        // ActivityLogs: retained unchanged (Req 3.4). The migration never reads this table.
        Execute(connection, """
            CREATE TABLE ActivityLogs (
                Id INTEGER PRIMARY KEY,
                Type INTEGER NOT NULL,
                Title TEXT NOT NULL,
                Description TEXT NULL,
                CreatedAt TEXT NULL
            );
            """);

        // Legacy industry-specific remote data: never read or migrated (Req 3.5).
        Execute(connection, """
            CREATE TABLE RemoteSyncStates (
                Id INTEGER PRIMARY KEY,
                ExternalId TEXT NOT NULL,
                RawPayload TEXT NOT NULL
            );
            """);

        string created = new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);
        string updated = new DateTime(2024, 2, 1, 8, 0, 0, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);

        // Customers.
        ExecuteParams(connection,
            "INSERT INTO Customers (Id, Name, Phone, ContactHandle, Remark, CreatedAt, UpdatedAt) " +
            "VALUES (1, '客户 A', '13800000001', 'wechat-a', '重点客户', $c, $u);",
            ("$c", created), ("$u", updated));
        ExecuteParams(connection,
            "INSERT INTO Customers (Id, Name, Phone, ContactHandle, Remark, CreatedAt, UpdatedAt) " +
            "VALUES (2, '客户 B', '13800000002', 'wechat-b', '', $c, $u);",
            ("$c", created), ("$u", updated));

        // Orders → Order.
        ExecuteParams(connection,
            "INSERT INTO Orders (Id, CustomerId, Title, Status, Amount, Requirement, CreatedAt, UpdatedAt) " +
            "VALUES (1, 1, '订单 001', 5, 1280.50, '常规需求', $c, $u);",
            ("$c", created), ("$u", updated));

        // Deals → Order (Won) / BusinessTask (open) / note (Lost).
        ExecuteParams(connection,
            "INSERT INTO Deals (Id, CustomerId, Title, Stage, EstimatedAmount, Requirement, ExpectedCloseAt, ClosedAt, CreatedAt, UpdatedAt) " +
            "VALUES (1, 1, '成交机会-赢单', $stage, 5000.00, '已成交', NULL, $u, $c, $u);",
            ("$stage", DealStageWon), ("$c", created), ("$u", updated));
        ExecuteParams(connection,
            "INSERT INTO Deals (Id, CustomerId, Title, Stage, EstimatedAmount, Requirement, ExpectedCloseAt, ClosedAt, CreatedAt, UpdatedAt) " +
            "VALUES (2, 1, '成交机会-跟进中', $stage, 3000.00, '待跟进', $u, NULL, $c, $u);",
            ("$stage", DealStageQualifiedOpen), ("$c", created), ("$u", updated));
        ExecuteParams(connection,
            "INSERT INTO Deals (Id, CustomerId, Title, Stage, EstimatedAmount, Requirement, ExpectedCloseAt, ClosedAt, CreatedAt, UpdatedAt) " +
            "VALUES (3, 2, '成交机会-丢单', $stage, 0.00, '客户放弃', NULL, $u, $c, $u);",
            ("$stage", DealStageLost), ("$c", created), ("$u", updated));

        // FollowUp → BusinessTask.
        ExecuteParams(connection,
            "INSERT INTO FollowUps (Id, CustomerId, OrderId, Title, Content, Status, ScheduledAt, CompletedAt, CreatedAt, UpdatedAt) " +
            "VALUES (1, 1, 1, '首次跟进', '电话回访', $status, $u, NULL, $c, $u);",
            ("$status", FollowUpStatusPending), ("$c", created), ("$u", updated));

        // CustomerNote → note.
        ExecuteParams(connection,
            "INSERT INTO CustomerNotes (Id, CustomerId, OrderId, Type, Content, IsPinned, CreatedAt, UpdatedAt) " +
            "VALUES (1, 1, NULL, $type, '客户偏好记录', 1, $c, $u);",
            ("$type", CustomerNoteTypeGeneral), ("$c", created), ("$u", updated));

        // ActivityLog row — must remain untouched and must not surface in any migrated record.
        ExecuteParams(connection,
            "INSERT INTO ActivityLogs (Id, Type, Title, Description, CreatedAt) " +
            "VALUES (1, 1, '活动日志标题', '活动日志详情', $c);",
            ("$c", created));

        // Remote data row — must remain unread and unmigrated.
        Execute(connection,
            "INSERT INTO RemoteSyncStates (Id, ExternalId, RawPayload) " +
            "VALUES (1, 'ext-1', 'REMOTE_PAYLOAD_SENTINEL');");
    }

    // ----------------------------------------------------------------------------------------------
    // Source snapshotting / inspection
    // ----------------------------------------------------------------------------------------------

    private static readonly string[] SourceTables =
    {
        "Customers", "Orders", "Deals", "FollowUps", "CustomerNotes", "ActivityLogs", "RemoteSyncStates",
    };

    /// <summary>
    /// Captures a deterministic text snapshot of every source table's rows so a before/after
    /// comparison can prove the migration did not delete, overwrite, or reorder any source data.
    /// </summary>
    private static Dictionary<string, string> SnapshotAllSourceTables(string sourcePath)
    {
        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);

        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = sourcePath }.ToString());
        connection.Open();

        foreach (string table in SourceTables)
        {
            snapshot[table] = SnapshotTable(connection, table);
        }

        return snapshot;
    }

    private static string SnapshotTable(SqliteConnection connection, string table)
    {
        var builder = new StringBuilder();

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM \"{table}\" ORDER BY Id ASC;";
        using SqliteDataReader reader = command.ExecuteReader();

        int fieldCount = reader.FieldCount;
        while (reader.Read())
        {
            for (int i = 0; i < fieldCount; i++)
            {
                builder.Append(reader.GetName(i)).Append('=');
                builder.Append(reader.IsDBNull(i)
                    ? "<null>"
                    : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture));
                builder.Append('|');
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    private static bool CommerceEntityTablesExist(string targetPath)
    {
        if (!File.Exists(targetPath))
        {
            return false;
        }

        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = targetPath }.ToString());
        connection.Open();

        // The 18 Commerce entity tables are only created by the migration's schema-init step, which
        // runs after a successful backup. The CommerceLegacyMigrationLog table is deliberately
        // excluded because an aborted run still records its outcome there (Req 3.8 / 3.9).
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' " +
            "AND name LIKE 'Commerce%' AND name <> 'CommerceLegacyMigrationLog';";
        long count = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        return count > 0;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void ExecuteParams(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }

    // ----------------------------------------------------------------------------------------------
    // Temp workspace lifecycle
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Creates an isolated temp directory containing the legacy source database path, the target
    /// Commerce database path, and a backup directory, runs <paramref name="action"/>, then removes
    /// the whole directory (and clears the SQLite pool) in a finally block so no artifacts leak.
    /// </summary>
    private static void WithTempWorkspace(Func<string, string, string, bool> action)
    {
        string root = Path.Combine(Path.GetTempPath(), $"orderly-legacy-migration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        string sourcePath = Path.Combine(root, "legacy-source.db");
        string targetPath = Path.Combine(root, "commerce-target.db");
        string backupDir = Path.Combine(root, "backups");

        try
        {
            action(sourcePath, targetPath, backupDir);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDeleteDirectory(root);
        }
    }

    private static void TryDeleteDirectory(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; a transiently-locked temp artifact is not a test failure.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup.
        }
    }
}
