using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Migration;
using Orderly.Core.Services;
using Orderly.Data.Commerce.Repositories;
using Orderly.Data.Repositories;
using Orderly.Data.Services;
using Orderly.Data.Sqlite;
using Xunit;
using LegacyCustomer = Orderly.Core.Models.Customer;
using CustomerStatus = Orderly.Core.Models.CustomerStatus;
using CustomerPriority = Orderly.Core.Models.CustomerPriority;
using LocalSessionContext = Orderly.Core.Models.LocalSessionContext;
using LocalAccountRole = Orderly.Core.Models.LocalAccountRole;

namespace Orderly.Tests.Commerce;

/// <summary>
/// Tests for <see cref="CommerceStartupMigrationService"/>, the startup wiring that runs the legacy
/// CRM → Commerce migration once, idempotently, and non-destructively (Req 3.4–3.10). The legacy and
/// Commerce tables share one database, so a single connection factory is both source and target.
///
/// Covers: a fresh database starts cleanly (no legacy data), a legacy CRM database is migrated into
/// records the Commerce pages can read, a repeated startup does not re-migrate, a failed migration
/// leaves the legacy data intact, and the outcome is recorded in a traceable migration log.
/// </summary>
public class CommerceStartupMigrationServiceTests
{
    [Fact]
    public void Fresh_database_with_no_legacy_data_completes_without_error()
    {
        WithTempDatabase((path, backup) =>
        {
            var service = new CommerceStartupMigrationService(new SqliteConnectionFactory(path), backup);

            CommerceLegacyMigrationResult result = service.RunAsync().GetAwaiter().GetResult();

            Assert.Equal(CommerceLegacyMigrationOutcome.Completed, result.Outcome);
            Assert.Equal(0, result.MigratedRecordCount);
        });
    }

    [Fact]
    public void Legacy_crm_database_is_migrated_into_commerce_visible_data()
    {
        WithTempDatabase((path, backup) =>
        {
            BuildLegacyCrmDatabase(path);
            var factory = new SqliteConnectionFactory(path);
            var service = new CommerceStartupMigrationService(factory, backup);

            CommerceLegacyMigrationResult result = service.RunAsync().GetAwaiter().GetResult();

            Assert.Equal(CommerceLegacyMigrationOutcome.Completed, result.Outcome);
            Assert.True(result.MigratedRecordCount > 0);

            // The migrated rows are readable through the same Commerce repositories the pages use,
            // and are owned by the stable primary workspace.
            IReadOnlyList<Customer> customers = new CommerceCustomerRepository(factory).GetAllAsync().GetAwaiter().GetResult();
            IReadOnlyList<Order> orders = new CommerceOrderRepository(factory).GetAllAsync().GetAwaiter().GetResult();

            Assert.Contains(customers, c => c.Name == "客户甲");
            Assert.Contains(orders, o => o.OrderNo == "LEGACY-O-1");
            Assert.All(customers, c => Assert.Equal(CommerceStartupMigrationService.PrimaryWorkspaceId, c.WorkspaceId));

            // The primary workspace row exists.
            BusinessWorkspace? workspace = new BusinessWorkspaceRepository(factory)
                .GetByIdAsync(CommerceStartupMigrationService.PrimaryWorkspaceId).GetAwaiter().GetResult();
            Assert.NotNull(workspace);
        });
    }

    [Fact]
    public void Repeated_startup_does_not_migrate_again()
    {
        WithTempDatabase((path, backup) =>
        {
            BuildLegacyCrmDatabase(path);
            var factory = new SqliteConnectionFactory(path);

            CommerceLegacyMigrationResult first = new CommerceStartupMigrationService(factory, backup).RunAsync().GetAwaiter().GetResult();
            Assert.Equal(CommerceLegacyMigrationOutcome.Completed, first.Outcome);
            int customersAfterFirst = new CommerceCustomerRepository(factory).GetAllAsync().GetAwaiter().GetResult().Count;

            // A second startup must short-circuit (already completed): no work, no duplicates.
            CommerceLegacyMigrationResult second = new CommerceStartupMigrationService(factory, backup).RunAsync().GetAwaiter().GetResult();
            Assert.Equal(CommerceLegacyMigrationOutcome.Completed, second.Outcome);
            Assert.Equal(0, second.MigratedRecordCount);

            int customersAfterSecond = new CommerceCustomerRepository(factory).GetAllAsync().GetAwaiter().GetResult().Count;
            Assert.Equal(customersAfterFirst, customersAfterSecond);

            // Exactly one completion is recorded for the whole DB lifetime.
            Assert.Equal(1, CountCompletedMigrationLogEntries(path));
        });
    }

    [Fact]
    public void Failed_backup_aborts_and_preserves_legacy_data_then_retry_succeeds()
    {
        WithTempDatabase((path, _) =>
        {
            BuildLegacyCrmDatabase(path);
            var factory = new SqliteConnectionFactory(path);
            string legacyBefore = SnapshotLegacyCustomers(path);

            // First attempt: the pre-migration backup fails, so the run aborts without changing data.
            CommerceLegacyMigrationResult aborted = new CommerceStartupMigrationService(factory, new FailingBackup())
                .RunAsync().GetAwaiter().GetResult();
            Assert.Equal(CommerceLegacyMigrationOutcome.BackupFailedMigrationAborted, aborted.Outcome);

            // Legacy data is untouched and nothing was migrated.
            Assert.Equal(legacyBefore, SnapshotLegacyCustomers(path));
            Assert.Empty(new CommerceCustomerRepository(factory).GetAllAsync().GetAwaiter().GetResult());
            Assert.Equal(0, CountCompletedMigrationLogEntries(path));

            // The next startup with a working backup retries and completes.
            using var temp = new TempFileBackup();
            CommerceLegacyMigrationResult retried = new CommerceStartupMigrationService(factory, temp)
                .RunAsync().GetAwaiter().GetResult();
            Assert.Equal(CommerceLegacyMigrationOutcome.Completed, retried.Outcome);
            Assert.True(retried.MigratedRecordCount > 0);
        });
    }

    [Fact]
    public void Migration_outcome_is_recorded_in_a_traceable_log()
    {
        WithTempDatabase((path, backup) =>
        {
            BuildLegacyCrmDatabase(path);
            new CommerceStartupMigrationService(new SqliteConnectionFactory(path), backup).RunAsync().GetAwaiter().GetResult();

            Assert.Equal(1, CountCompletedMigrationLogEntries(path));
        });
    }

    [Fact]
    public void Encrypted_legacy_customer_is_migrated_with_decrypted_values()
    {
        WithTempDatabase((path, backup) =>
        {
            // Build the full legacy schema (including the P0 *Ciphertext columns) and write a customer
            // through the production repository, which encrypts the sensitive fields and clears the
            // plaintext columns — exactly the on-disk state of a database upgraded past P0 encryption.
            var factory = new SqliteConnectionFactory(path);
            new DatabaseInitializer(factory).InitializeAsync().GetAwaiter().GetResult();

            // DatabaseInitializer seeds demo rows; clear the legacy business tables so this test
            // asserts on exactly the one encrypted customer it writes below.
            ClearLegacyBusinessTables(path);

            FieldEncryptionService field = NewFieldEncryption(path);
            var customerRepository = new CustomerRepository(factory, field);
            customerRepository.CreateAsync(new LegacyCustomer
            {
                Name = "张三",
                Phone = "13900000009",
                ContactHandle = "wx-zhangsan",
                Status = CustomerStatus.Active,
                Priority = CustomerPriority.High,
            }).GetAwaiter().GetResult();

            // Migrate using the SAME field-encryption service (same data key) so the encrypted legacy
            // values are decrypted into the Commerce records instead of read as cleared blanks.
            CommerceLegacyMigrationResult result = new CommerceStartupMigrationService(factory, backup, field)
                .RunAsync().GetAwaiter().GetResult();
            Assert.Equal(CommerceLegacyMigrationOutcome.Completed, result.Outcome);

            Customer migrated = Assert.Single(new CommerceCustomerRepository(factory).GetAllAsync().GetAwaiter().GetResult());
            Assert.Equal("张三", migrated.Name);
            Assert.Equal("13900000009", migrated.Phone);
            Assert.Equal("wx-zhangsan", migrated.WeChat);
        });
    }

    // --- Helpers ---

    private static void ClearLegacyBusinessTables(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM CustomerNotes; DELETE FROM FollowUps; DELETE FROM Orders; " +
            "DELETE FROM Deals; DELETE FROM Customers;";
        command.ExecuteNonQuery();
    }

    private static FieldEncryptionService NewFieldEncryption(string databasePath)
    {
        var sessionContextService = new SessionContextService();
        sessionContextService.SetCurrent(new LocalSessionContext
        {
            AccountId = "qa-mig-account",
            Username = "qa-mig-user",
            DisplayName = "QA Migration User",
            Role = LocalAccountRole.Owner,
            DatabasePath = databasePath,
            DataKey = RandomNumberGenerator.GetBytes(32),
            SignedInAt = DateTime.Now,
        });
        return new FieldEncryptionService(sessionContextService);
    }

    private static int CountCompletedMigrationLogEntries(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM \"CommerceLegacyMigrationLog\" WHERE OutcomeToken = $token;";
        command.Parameters.AddWithValue("$token", CommerceLegacyMigrationOutcomeTokens.MigrationCompleted);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static string SnapshotLegacyCustomers(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Phone FROM Customers ORDER BY Id;";
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            rows.Add($"{reader.GetInt64(0)}|{reader.GetString(1)}|{reader.GetString(2)}");
        }

        return string.Join(";", rows);
    }

    private static void BuildLegacyCrmDatabase(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();

        Execute(connection,
            "CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT, Phone TEXT, ContactHandle TEXT, Remark TEXT, CreatedAt TEXT, UpdatedAt TEXT, DeletedAt TEXT);" +
            "CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER, Title TEXT, Status INTEGER, Amount REAL, Requirement TEXT, CreatedAt TEXT, UpdatedAt TEXT, DeletedAt TEXT);");

        Execute(connection,
            "INSERT INTO Customers (Id, Name, Phone, ContactHandle, Remark, CreatedAt, UpdatedAt, DeletedAt) " +
            "VALUES (1, '客户甲', '13800000001', 'wx-jia', '备注', '2020-01-01T00:00:00.0000000Z', '2020-01-02T00:00:00.0000000Z', NULL);");
        Execute(connection,
            "INSERT INTO Orders (Id, CustomerId, Title, Status, Amount, Requirement, CreatedAt, UpdatedAt, DeletedAt) " +
            "VALUES (1, 1, '订单标题', 2, 100.00, '需求', '2020-01-03T00:00:00.0000000Z', '2020-01-04T00:00:00.0000000Z', NULL);");
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void WithTempDatabase(Action<string, ICommerceSourceBackup> action)
    {
        string path = Path.Combine(Path.GetTempPath(), $"orderly-startup-mig-{Guid.NewGuid():N}.db");
        using var backup = new TempFileBackup();
        try
        {
            action(path, backup);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
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
                }
            }
        }
    }

    /// <summary>A backup that copies the source to a tracked temp file and reports success.</summary>
    private sealed class TempFileBackup : ICommerceSourceBackup, IDisposable
    {
        private readonly string _backupPath = Path.Combine(Path.GetTempPath(), $"orderly-startup-bak-{Guid.NewGuid():N}.db");

        public Task<CommerceSourceBackupResult> CreateBackupAsync(string sourceDatabasePath, CancellationToken cancellationToken = default)
        {
            File.Copy(sourceDatabasePath, _backupPath, overwrite: true);
            return Task.FromResult(CommerceSourceBackupResult.Success(_backupPath));
        }

        public void Dispose()
        {
            try
            {
                if (File.Exists(_backupPath))
                {
                    File.Delete(_backupPath);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>A backup that always fails, used to assert the backup-first abort path (Req 3.8).</summary>
    private sealed class FailingBackup : ICommerceSourceBackup
    {
        public Task<CommerceSourceBackupResult> CreateBackupAsync(string sourceDatabasePath, CancellationToken cancellationToken = default)
            => Task.FromResult(CommerceSourceBackupResult.Failure("测试注入的备份失败。"));
    }
}
