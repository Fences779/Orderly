using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Migration;
using Orderly.Data.Commerce.Repositories;
using Orderly.Data.Services;
using Orderly.Data.Sqlite;
using Orderly.Tests.Support;
using Xunit;

namespace Orderly.Tests.Commerce;

/// <summary>
/// Property-based tests for the non-destructive, backup-first, idempotent legacy CRM migration
/// (<see cref="CommerceLegacyMigrationService"/>).
///
/// Property 6: Migration is idempotent and non-destructive.
/// For ANY generated legacy source dataset, running the migration two or more times produces a
/// target record set IDENTICAL to running it once with NO duplicated migrated records, and every
/// source record remains present and unchanged after migration (with a source backup created
/// before any change is applied).
///
/// The test, for each generated dataset:
/// <list type="bullet">
///   <item>builds a fresh legacy source SQLite database (the generic CRM tables
///   <c>Customers / Orders / Deals / FollowUps / CustomerNotes</c> the migration reads, plus an
///   <c>ActivityLogs</c> table it must never touch) and a canonical row snapshot of it;</item>
///   <item>runs the migration once against a fresh target database, captures a canonical snapshot
///   of every migrated target record (customers + orders + tasks, ordered by id);</item>
///   <item>runs the migration N−1 additional times (N ∈ [2,4]) against the SAME target database and
///   captures the target snapshot again;</item>
///   <item>asserts the once-snapshot equals the repeated-snapshot (idempotent, no duplicates) and
///   that the migrated record count is stable across runs;</item>
///   <item>asserts the source database snapshot is byte-for-byte unchanged (non-destructive) and
///   that a backup of the source was created before any change.</item>
/// </list>
///
/// Source rows always carry a non-null, parseable <c>CreatedAt</c> (as real legacy CRM rows do) so
/// the migration's deterministic audit-pinning is exercised over a well-defined input space.
/// All temp database/backup files are removed in a <c>finally</c> block.
///
/// **Validates: Requirements 3.6, 3.7**
/// </summary>
public class MigrationIdempotenceNonDestructivePropertyTests
{
    private static readonly Guid WorkspaceId = new("a1b2c3d4-e5f6-4789-abcd-ef0123456789");
    private static readonly DateTime BaseDate = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // Neutral, industry-agnostic text values (no Forbidden_Term); empty strings included on purpose.
    private static readonly Gen<string> TextGen = Gen.OneOfConst("甲", "乙", "丙", "示例", "记录", "需求说明", "");

    // Customer reference 0 means "no customer link"; 1..5 reference a (possibly absent) source id.
    private static readonly Gen<int> CustomerRefGen = Gen.Int[0, 5];
    private static readonly Gen<int?> OrderRefGen = Gen.Int[0, 5].Select(i => i == 0 ? (int?)null : i);

    // Amount in whole cents so the REAL round-trip stays exact (0.00 .. 50000.00).
    private static readonly Gen<int> AmountCentsGen = Gen.Int[0, 5_000_000];

    // CreatedAt is always present (deterministic). UpdatedAt is optional (null falls back to CreatedAt).
    private static readonly Gen<int> CreatedDayGen = Gen.Int[0, 2000];
    private static readonly Gen<int?> UpdatedDayGen = Gen.Int[0, 2200].Select(d => d % 4 == 0 ? (int?)null : d);

    private static readonly Gen<GenCustomer> CustomerGen =
        from name in TextGen
        from phone in TextGen
        from handle in TextGen
        from remark in TextGen
        from created in CreatedDayGen
        from updated in UpdatedDayGen
        select new GenCustomer(name, phone, handle, remark, created, updated);

    private static readonly Gen<GenOrder> OrderGen =
        from cust in CustomerRefGen
        from title in TextGen
        from status in Gen.Int[0, 6]
        from amount in AmountCentsGen
        from req in TextGen
        from created in CreatedDayGen
        from updated in UpdatedDayGen
        select new GenOrder(cust, title, status, amount, req, created, updated);

    private static readonly Gen<GenDeal> DealGen =
        from cust in CustomerRefGen
        from title in TextGen
        from stage in Gen.Int[0, 6]
        from amount in AmountCentsGen
        from req in TextGen
        from created in CreatedDayGen
        from updated in UpdatedDayGen
        select new GenDeal(cust, title, stage, amount, req, created, updated);

    private static readonly Gen<GenFollowUp> FollowUpGen =
        from cust in CustomerRefGen
        from order in OrderRefGen
        from title in TextGen
        from content in TextGen
        from status in Gen.Int[0, 5]
        from created in CreatedDayGen
        from updated in UpdatedDayGen
        select new GenFollowUp(cust, order, title, content, status, created, updated);

    private static readonly Gen<GenNote> NoteGen =
        from cust in CustomerRefGen
        from order in OrderRefGen
        from type in Gen.Int[0, 3]
        from content in TextGen
        from pinned in Gen.Bool
        from created in CreatedDayGen
        from updated in UpdatedDayGen
        select new GenNote(cust, order, type, content, pinned, created, updated);

    private static readonly Gen<GenDataset> DatasetGen =
        from customers in CustomerGen.List[0, 5]
        from orders in OrderGen.List[0, 5]
        from deals in DealGen.List[0, 6]
        from followUps in FollowUpGen.List[0, 5]
        from notes in NoteGen.List[0, 5]
        from repeatCount in Gen.Int[2, 4]
        select new GenDataset(customers, orders, deals, followUps, notes, repeatCount);

    [Fact]
    public void Property6_migration_is_idempotent_and_non_destructive()
    {
        DatasetGen.Sample(
            dataset =>
            {
                WithTempWorkspace((sourcePath, targetPath, backup) =>
                {
                    BuildSourceDatabase(sourcePath, dataset);
                    string sourceBefore = SnapshotSourceDatabase(sourcePath);

                    int expectedCount =
                        dataset.Customers.Count + dataset.Orders.Count + dataset.Deals.Count +
                        dataset.FollowUps.Count + dataset.Notes.Count;

                    // --- Run once against a fresh target database. ---
                    CommerceLegacyMigrationResult first = RunMigration(sourcePath, targetPath, backup);
                    Assert.Equal(CommerceLegacyMigrationOutcome.Completed, first.Outcome);
                    Assert.Equal(expectedCount, first.MigratedRecordCount);

                    // A complete backup of the source was created before any change (Req 3.7).
                    Assert.NotNull(first.BackupPath);
                    Assert.True(File.Exists(first.BackupPath));

                    string targetAfterOnce = SnapshotTargetDatabase(targetPath);

                    // --- Run N−1 more times against the SAME target database. ---
                    for (int run = 1; run < dataset.RepeatCount; run++)
                    {
                        CommerceLegacyMigrationResult again = RunMigration(sourcePath, targetPath, backup);
                        Assert.Equal(CommerceLegacyMigrationOutcome.Completed, again.Outcome);
                        // Re-running the same migration migrates the same records, not duplicates.
                        Assert.Equal(expectedCount, again.MigratedRecordCount);
                    }

                    string targetAfterRepeated = SnapshotTargetDatabase(targetPath);

                    // Idempotent: running N times leaves a target record set identical to running once.
                    Assert.Equal(targetAfterOnce, targetAfterRepeated);

                    // No duplicated migrated records: the migrated row total still equals the source total.
                    Assert.Equal(expectedCount, CountTargetRows(targetPath));

                    // Non-destructive: every source record remains present and unchanged (Req 3.7).
                    string sourceAfter = SnapshotSourceDatabase(sourcePath);
                    Assert.Equal(sourceBefore, sourceAfter);
                });
            },
            iter: PbtConfig.MinIterations);
    }

    // --- Focused unit examples complementing the property above ---

    [Fact]
    public void Migrating_twice_produces_the_same_target_as_migrating_once()
    {
        var dataset = new GenDataset(
            Customers: new List<GenCustomer> { new("客户甲", "1", "wx1", "备注", 1, 2) },
            Orders: new List<GenOrder> { new(1, "订单标题", 5, 12345, "需求", 3, 4) },
            Deals: new List<GenDeal>
            {
                new(1, "成交", 4 /* Won -> Order */, 9999, "需求", 5, null),
                new(1, "进行中", 1 /* open -> BusinessTask */, 5000, "需求", 6, 7),
                new(1, "已丢失", 5 /* Lost -> note */, 0, "需求", 8, 9),
            },
            FollowUps: new List<GenFollowUp> { new(1, 1, "跟进", "内容", 1, 10, 11) },
            Notes: new List<GenNote> { new(1, null, 0, "备注内容", true, 12, 13) },
            RepeatCount: 3);

        WithTempWorkspace((sourcePath, targetPath, backup) =>
        {
            BuildSourceDatabase(sourcePath, dataset);

            RunMigration(sourcePath, targetPath, backup);
            string once = SnapshotTargetDatabase(targetPath);

            RunMigration(sourcePath, targetPath, backup);
            RunMigration(sourcePath, targetPath, backup);
            string thrice = SnapshotTargetDatabase(targetPath);

            Assert.Equal(once, thrice);
            // 7 source rows -> 7 target rows, no duplicates after three runs.
            Assert.Equal(7, CountTargetRows(targetPath));
        });
    }

    [Fact]
    public void Migration_leaves_the_source_database_unchanged_and_creates_a_backup()
    {
        var dataset = new GenDataset(
            Customers: new List<GenCustomer> { new("客户乙", "2", "wx2", "", 1, null) },
            Orders: new List<GenOrder> { new(1, "订单", 2, 4200, "", 2, 3) },
            Deals: new List<GenDeal>(),
            FollowUps: new List<GenFollowUp>(),
            Notes: new List<GenNote> { new(1, 1, 1, "备注", false, 4, 5) },
            RepeatCount: 2);

        WithTempWorkspace((sourcePath, targetPath, backup) =>
        {
            BuildSourceDatabase(sourcePath, dataset);
            string before = SnapshotSourceDatabase(sourcePath);

            CommerceLegacyMigrationResult result = RunMigration(sourcePath, targetPath, backup);

            Assert.Equal(CommerceLegacyMigrationOutcome.Completed, result.Outcome);
            Assert.NotNull(result.BackupPath);
            Assert.True(File.Exists(result.BackupPath));

            string after = SnapshotSourceDatabase(sourcePath);
            Assert.Equal(before, after);
        });
    }

    [Fact]
    public void Empty_source_completes_with_zero_migrated_records()
    {
        var dataset = new GenDataset(
            Customers: new List<GenCustomer>(),
            Orders: new List<GenOrder>(),
            Deals: new List<GenDeal>(),
            FollowUps: new List<GenFollowUp>(),
            Notes: new List<GenNote>(),
            RepeatCount: 3);

        WithTempWorkspace((sourcePath, targetPath, backup) =>
        {
            BuildSourceDatabase(sourcePath, dataset);

            CommerceLegacyMigrationResult first = RunMigration(sourcePath, targetPath, backup);
            string once = SnapshotTargetDatabase(targetPath);
            RunMigration(sourcePath, targetPath, backup);
            string twice = SnapshotTargetDatabase(targetPath);

            Assert.Equal(CommerceLegacyMigrationOutcome.Completed, first.Outcome);
            Assert.Equal(0, first.MigratedRecordCount);
            Assert.Equal(once, twice);
            Assert.Equal(0, CountTargetRows(targetPath));
        });
    }

    // --- Migration execution ---

    private static CommerceLegacyMigrationResult RunMigration(string sourcePath, string targetPath, RecordingFileBackup backup)
    {
        var service = new CommerceLegacyMigrationService(
            new SqliteConnectionFactory(sourcePath),
            new SqliteConnectionFactory(targetPath),
            WorkspaceId,
            backup);
        return service.MigrateAsync().GetAwaiter().GetResult();
    }

    private static int CountTargetRows(string targetPath)
    {
        var factory = new SqliteConnectionFactory(targetPath);
        int customers = new CommerceCustomerRepository(factory).GetAllIncludingDeletedAsync().GetAwaiter().GetResult().Count;
        int orders = new CommerceOrderRepository(factory).GetAllIncludingDeletedAsync().GetAwaiter().GetResult().Count;
        int tasks = new BusinessTaskRepository(factory).GetAllIncludingDeletedAsync().GetAwaiter().GetResult().Count;
        return customers + orders + tasks;
    }

    // --- Target snapshot (canonical, ordered by id) ---

    private static string SnapshotTargetDatabase(string targetPath)
    {
        var factory = new SqliteConnectionFactory(targetPath);
        var builder = new StringBuilder();

        IReadOnlyList<Customer> customers =
            new CommerceCustomerRepository(factory).GetAllIncludingDeletedAsync().GetAwaiter().GetResult();
        foreach (Customer c in customers.OrderBy(c => c.Id))
        {
            builder.Append("customer|")
                .Append(c.Id).Append('|')
                .Append(c.WorkspaceId).Append('|')
                .Append(c.Name).Append('|')
                .Append(c.Phone ?? "∅").Append('|')
                .Append(c.WeChat ?? "∅").Append('|')
                .Append(c.Email ?? "∅").Append('|')
                .Append(Fmt(c.CreatedAt)).Append('|')
                .Append(Fmt(c.UpdatedAt)).Append('|')
                .Append(FmtNullable(c.DeletedAt)).Append('|')
                .Append((int)c.Lifecycle).Append('|')
                .Append(c.CustomFieldsJson ?? "∅").Append('\n');
        }

        IReadOnlyList<Order> orders =
            new CommerceOrderRepository(factory).GetAllIncludingDeletedAsync().GetAwaiter().GetResult();
        foreach (Order o in orders.OrderBy(o => o.Id))
        {
            builder.Append("order|")
                .Append(o.Id).Append('|')
                .Append(o.WorkspaceId).Append('|')
                .Append(o.OrderNo ?? "∅").Append('|')
                .Append(o.CustomerId?.ToString() ?? "∅").Append('|')
                .Append((int)o.SalesStage).Append('|')
                .Append((int)o.PaymentStage).Append('|')
                .Append((int)o.FulfillmentStage).Append('|')
                .Append(o.Total.Amount.ToString("0.00", CultureInfo.InvariantCulture)).Append('|')
                .Append(o.ReceivableAmount.Amount.ToString("0.00", CultureInfo.InvariantCulture)).Append('|')
                .Append(Fmt(o.CreatedAt)).Append('|')
                .Append(Fmt(o.UpdatedAt)).Append('|')
                .Append(FmtNullable(o.DeletedAt)).Append('|')
                .Append((int)o.Lifecycle).Append('|')
                .Append(o.Note ?? "∅").Append('|')
                .Append(o.CustomFieldsJson ?? "∅").Append('\n');
        }

        IReadOnlyList<BusinessTask> tasks =
            new BusinessTaskRepository(factory).GetAllIncludingDeletedAsync().GetAwaiter().GetResult();
        foreach (BusinessTask t in tasks.OrderBy(t => t.Id))
        {
            builder.Append("task|")
                .Append(t.Id).Append('|')
                .Append(t.WorkspaceId).Append('|')
                .Append(t.Title).Append('|')
                .Append(t.Description ?? "∅").Append('|')
                .Append((int)t.Status).Append('|')
                .Append(FmtNullable(t.DueDate)).Append('|')
                .Append(FmtNullable(t.CompletedAt)).Append('|')
                .Append(t.CustomerId?.ToString() ?? "∅").Append('|')
                .Append(t.OrderId?.ToString() ?? "∅").Append('|')
                .Append(Fmt(t.CreatedAt)).Append('|')
                .Append(Fmt(t.UpdatedAt)).Append('|')
                .Append(FmtNullable(t.DeletedAt)).Append('|')
                .Append((int)t.Lifecycle).Append('|')
                .Append(t.CustomFieldsJson ?? "∅").Append('\n');
        }

        return builder.ToString();
    }

    private static string Fmt(DateTime value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static string FmtNullable(DateTime? value)
        => value is null ? "∅" : value.Value.ToString("O", CultureInfo.InvariantCulture);

    // --- Source database construction and snapshot ---

    private static void BuildSourceDatabase(string sourcePath, GenDataset dataset)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourcePath, Pooling = false }.ToString());
        connection.Open();

        ExecuteNonQuery(connection,
            "CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT, Phone TEXT, ContactHandle TEXT, Remark TEXT, CreatedAt TEXT, UpdatedAt TEXT, DeletedAt TEXT);" +
            "CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER, Title TEXT, Status INTEGER, Amount REAL, Requirement TEXT, CreatedAt TEXT, UpdatedAt TEXT, DeletedAt TEXT);" +
            "CREATE TABLE Deals (Id INTEGER PRIMARY KEY, CustomerId INTEGER, Title TEXT, Stage INTEGER, EstimatedAmount REAL, Requirement TEXT, ExpectedCloseAt TEXT, ClosedAt TEXT, CreatedAt TEXT, UpdatedAt TEXT, DeletedAt TEXT);" +
            "CREATE TABLE FollowUps (Id INTEGER PRIMARY KEY, CustomerId INTEGER, OrderId INTEGER, Title TEXT, Content TEXT, Status INTEGER, ScheduledAt TEXT, CompletedAt TEXT, CreatedAt TEXT, UpdatedAt TEXT, DeletedAt TEXT);" +
            "CREATE TABLE CustomerNotes (Id INTEGER PRIMARY KEY, CustomerId INTEGER, OrderId INTEGER, Type INTEGER, Content TEXT, IsPinned INTEGER, CreatedAt TEXT, UpdatedAt TEXT, DeletedAt TEXT);" +
            // ActivityLogs is never read by the migration; present to confirm it is left untouched.
            "CREATE TABLE ActivityLogs (Id INTEGER PRIMARY KEY, Detail TEXT);");

        ExecuteNonQuery(connection, "INSERT INTO ActivityLogs (Id, Detail) VALUES (1, '活动日志');");

        for (int i = 0; i < dataset.Customers.Count; i++)
        {
            GenCustomer row = dataset.Customers[i];
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO Customers (Id, Name, Phone, ContactHandle, Remark, CreatedAt, UpdatedAt, DeletedAt) " +
                "VALUES ($id, $name, $phone, $handle, $remark, $created, $updated, NULL);";
            command.Parameters.AddWithValue("$id", i + 1);
            command.Parameters.AddWithValue("$name", row.Name);
            command.Parameters.AddWithValue("$phone", row.Phone);
            command.Parameters.AddWithValue("$handle", row.ContactHandle);
            command.Parameters.AddWithValue("$remark", row.Remark);
            command.Parameters.AddWithValue("$created", Day(row.CreatedDay));
            command.Parameters.AddWithValue("$updated", DayNullable(row.UpdatedDay));
            command.ExecuteNonQuery();
        }

        for (int i = 0; i < dataset.Orders.Count; i++)
        {
            GenOrder row = dataset.Orders[i];
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO Orders (Id, CustomerId, Title, Status, Amount, Requirement, CreatedAt, UpdatedAt, DeletedAt) " +
                "VALUES ($id, $customerId, $title, $status, $amount, $req, $created, $updated, NULL);";
            command.Parameters.AddWithValue("$id", i + 1);
            command.Parameters.AddWithValue("$customerId", row.CustomerRef);
            command.Parameters.AddWithValue("$title", row.Title);
            command.Parameters.AddWithValue("$status", row.Status);
            command.Parameters.AddWithValue("$amount", row.AmountCents / 100.0);
            command.Parameters.AddWithValue("$req", row.Requirement);
            command.Parameters.AddWithValue("$created", Day(row.CreatedDay));
            command.Parameters.AddWithValue("$updated", DayNullable(row.UpdatedDay));
            command.ExecuteNonQuery();
        }

        for (int i = 0; i < dataset.Deals.Count; i++)
        {
            GenDeal row = dataset.Deals[i];
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO Deals (Id, CustomerId, Title, Stage, EstimatedAmount, Requirement, ExpectedCloseAt, ClosedAt, CreatedAt, UpdatedAt, DeletedAt) " +
                "VALUES ($id, $customerId, $title, $stage, $amount, $req, NULL, NULL, $created, $updated, NULL);";
            command.Parameters.AddWithValue("$id", i + 1);
            command.Parameters.AddWithValue("$customerId", row.CustomerRef);
            command.Parameters.AddWithValue("$title", row.Title);
            command.Parameters.AddWithValue("$stage", row.Stage);
            command.Parameters.AddWithValue("$amount", row.AmountCents / 100.0);
            command.Parameters.AddWithValue("$req", row.Requirement);
            command.Parameters.AddWithValue("$created", Day(row.CreatedDay));
            command.Parameters.AddWithValue("$updated", DayNullable(row.UpdatedDay));
            command.ExecuteNonQuery();
        }

        for (int i = 0; i < dataset.FollowUps.Count; i++)
        {
            GenFollowUp row = dataset.FollowUps[i];
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO FollowUps (Id, CustomerId, OrderId, Title, Content, Status, ScheduledAt, CompletedAt, CreatedAt, UpdatedAt, DeletedAt) " +
                "VALUES ($id, $customerId, $orderId, $title, $content, $status, NULL, NULL, $created, $updated, NULL);";
            command.Parameters.AddWithValue("$id", i + 1);
            command.Parameters.AddWithValue("$customerId", row.CustomerRef);
            command.Parameters.AddWithValue("$orderId", (object?)row.OrderRef ?? DBNull.Value);
            command.Parameters.AddWithValue("$title", row.Title);
            command.Parameters.AddWithValue("$content", row.Content);
            command.Parameters.AddWithValue("$status", row.Status);
            command.Parameters.AddWithValue("$created", Day(row.CreatedDay));
            command.Parameters.AddWithValue("$updated", DayNullable(row.UpdatedDay));
            command.ExecuteNonQuery();
        }

        for (int i = 0; i < dataset.Notes.Count; i++)
        {
            GenNote row = dataset.Notes[i];
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO CustomerNotes (Id, CustomerId, OrderId, Type, Content, IsPinned, CreatedAt, UpdatedAt, DeletedAt) " +
                "VALUES ($id, $customerId, $orderId, $type, $content, $pinned, $created, $updated, NULL);";
            command.Parameters.AddWithValue("$id", i + 1);
            command.Parameters.AddWithValue("$customerId", row.CustomerRef);
            command.Parameters.AddWithValue("$orderId", (object?)row.OrderRef ?? DBNull.Value);
            command.Parameters.AddWithValue("$type", row.Type);
            command.Parameters.AddWithValue("$content", row.Content);
            command.Parameters.AddWithValue("$pinned", row.IsPinned ? 1 : 0);
            command.Parameters.AddWithValue("$created", Day(row.CreatedDay));
            command.Parameters.AddWithValue("$updated", DayNullable(row.UpdatedDay));
            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Builds a deterministic, canonical text snapshot of the entire legacy source database: every
    /// row of every table (including <c>ActivityLogs</c>), ordered by table then row id, so that the
    /// snapshot depends only on stored content. Used to prove the migration never modifies the source.
    /// </summary>
    private static string SnapshotSourceDatabase(string sourcePath)
    {
        var builder = new StringBuilder();
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourcePath, Pooling = false }.ToString());
        connection.Open();

        List<string> tables = new();
        using (SqliteCommand listCommand = connection.CreateCommand())
        {
            listCommand.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;";
            using SqliteDataReader reader = listCommand.ExecuteReader();
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }
        }

        foreach (string table in tables)
        {
            builder.Append("=table=").Append(table).Append('\n');
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM \"{table}\" ORDER BY Id ASC;";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    string columnName = reader.GetName(i);
                    string value = reader.IsDBNull(i)
                        ? "∅"
                        : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture) ?? string.Empty;
                    builder.Append(columnName).Append('=').Append(value).Append('§');
                }

                builder.Append('\n');
            }
        }

        return builder.ToString();
    }

    private static void ExecuteNonQuery(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string Day(int dayOffset)
        => BaseDate.AddDays(dayOffset).ToString("O", CultureInfo.InvariantCulture);

    private static object DayNullable(int? dayOffset)
        => dayOffset is null ? DBNull.Value : BaseDate.AddDays(dayOffset.Value).ToString("O", CultureInfo.InvariantCulture);

    // --- Temp workspace lifecycle ---

    private static void WithTempWorkspace(Action<string, string, RecordingFileBackup> action)
    {
        string id = Guid.NewGuid().ToString("N");
        string sourcePath = Path.Combine(Path.GetTempPath(), $"orderly-mig-src-{id}.db");
        string targetPath = Path.Combine(Path.GetTempPath(), $"orderly-mig-tgt-{id}.db");
        string backupPath = Path.Combine(Path.GetTempPath(), $"orderly-mig-bak-{id}.db");
        var backup = new RecordingFileBackup(backupPath);

        try
        {
            action(sourcePath, targetPath, backup);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDeleteDatabaseFiles(sourcePath);
            TryDeleteDatabaseFiles(targetPath);
            TryDeleteDatabaseFiles(backupPath);
        }
    }

    private static void TryDeleteDatabaseFiles(string path)
    {
        foreach (string file in new[] { path, path + "-wal", path + "-shm", path + "-journal" })
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup; a transiently-locked temp file is not a test failure.
            }
        }
    }

    // --- Generated source-row shapes ---

    private sealed record GenCustomer(string Name, string Phone, string ContactHandle, string Remark, int CreatedDay, int? UpdatedDay);

    private sealed record GenOrder(int CustomerRef, string Title, int Status, int AmountCents, string Requirement, int CreatedDay, int? UpdatedDay);

    private sealed record GenDeal(int CustomerRef, string Title, int Stage, int AmountCents, string Requirement, int CreatedDay, int? UpdatedDay);

    private sealed record GenFollowUp(int CustomerRef, int? OrderRef, string Title, string Content, int Status, int CreatedDay, int? UpdatedDay);

    private sealed record GenNote(int CustomerRef, int? OrderRef, int Type, string Content, bool IsPinned, int CreatedDay, int? UpdatedDay);

    private sealed record GenDataset(
        List<GenCustomer> Customers,
        List<GenOrder> Orders,
        List<GenDeal> Deals,
        List<GenFollowUp> FollowUps,
        List<GenNote> Notes,
        int RepeatCount);

    /// <summary>
    /// A test <see cref="ICommerceSourceBackup"/> that copies the source database (and its sidecar
    /// files) to a tracked temp path and reports success, so the test can assert a backup was
    /// created before any change without depending on the production backup's storage location.
    /// </summary>
    private sealed class RecordingFileBackup : ICommerceSourceBackup
    {
        private readonly string _backupPath;

        public RecordingFileBackup(string backupPath) => _backupPath = backupPath;

        public Task<CommerceSourceBackupResult> CreateBackupAsync(string sourceDatabasePath, CancellationToken cancellationToken = default)
        {
            File.Copy(sourceDatabasePath, _backupPath, overwrite: true);
            return Task.FromResult(CommerceSourceBackupResult.Success(_backupPath));
        }
    }
}
