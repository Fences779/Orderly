using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Services;
using Orderly.Data.Commerce.Repositories;
using Orderly.Data.Commerce.Services;
using Orderly.Data.Sqlite;
using Xunit;

namespace Orderly.Tests.Commerce;

/// <summary>
/// Tests for the storage-layer Business_Key uniqueness guarantee (Req 4.20 / 18.6). A partial UNIQUE
/// index over <c>(WorkspaceId, BusinessKey)</c> on each idempotency-bearing table closes the
/// "find-then-insert" race so a duplicate keyed record can never be persisted, while leaving null /
/// empty keys unconstrained and allowing the same key across different workspaces. The shared
/// <see cref="BusinessKeyIdempotency"/> helper turns a unique-constraint conflict into an idempotent
/// success.
/// </summary>
public class BusinessKeyUniqueIndexTests
{
    [Fact]
    public void Repeated_schema_init_creates_the_unique_index_without_error()
    {
        WithTempDatabase(path =>
        {
            var factory = new SqliteConnectionFactory(path);
            var initializer = new CommerceSchemaInitializer(factory);

            // Running init several times must not throw (idempotent index creation).
            for (int i = 0; i < 3; i++)
            {
                initializer.InitializeAsync().GetAwaiter().GetResult();
            }

            Assert.True(IndexExists(path, "UX_CommercePaymentRecords_WorkspaceId_BusinessKey"));
            Assert.True(IndexExists(path, "UX_CommerceCashFlowEntries_WorkspaceId_BusinessKey"));
            Assert.True(IndexExists(path, "UX_CommerceInventoryMovements_WorkspaceId_BusinessKey"));
            Assert.True(IndexExists(path, "UX_CommerceBusinessInsights_WorkspaceId_BusinessKey"));
            Assert.True(IndexExists(path, "UX_CommerceBusinessMetricSnapshots_WorkspaceId_BusinessKey"));
        });
    }

    [Fact]
    public void Duplicate_keyed_record_in_same_workspace_is_rejected_by_the_index()
    {
        WithTempDatabase(path =>
        {
            var factory = new SqliteConnectionFactory(path);
            InitSchema(factory);
            var repo = new PaymentRecordRepository(factory);
            Guid workspaceId = Guid.NewGuid();

            repo.CreateAsync(NewPayment(workspaceId, "key-1")).GetAwaiter().GetResult();

            // A second active record with the same (WorkspaceId, BusinessKey) must violate the index.
            SqliteException ex = Assert.Throws<SqliteException>(() =>
                repo.CreateAsync(NewPayment(workspaceId, "key-1")).GetAwaiter().GetResult());
            Assert.True(ex.SqliteErrorCode == 19 || ex.SqliteExtendedErrorCode == 2067);
        });
    }

    [Fact]
    public void Same_business_key_in_different_workspaces_can_coexist()
    {
        WithTempDatabase(path =>
        {
            var factory = new SqliteConnectionFactory(path);
            InitSchema(factory);
            var repo = new PaymentRecordRepository(factory);

            repo.CreateAsync(NewPayment(Guid.NewGuid(), "shared-key")).GetAwaiter().GetResult();
            repo.CreateAsync(NewPayment(Guid.NewGuid(), "shared-key")).GetAwaiter().GetResult();

            Assert.Equal(2, repo.GetAllAsync().GetAwaiter().GetResult().Count);
        });
    }

    [Fact]
    public void Null_or_empty_business_keys_are_not_constrained()
    {
        WithTempDatabase(path =>
        {
            var factory = new SqliteConnectionFactory(path);
            InitSchema(factory);
            var repo = new PaymentRecordRepository(factory);
            Guid workspaceId = Guid.NewGuid();

            // Multiple null-keyed and multiple empty-keyed records in the same workspace are allowed.
            repo.CreateAsync(NewPayment(workspaceId, null)).GetAwaiter().GetResult();
            repo.CreateAsync(NewPayment(workspaceId, null)).GetAwaiter().GetResult();
            repo.CreateAsync(NewPayment(workspaceId, string.Empty)).GetAwaiter().GetResult();
            repo.CreateAsync(NewPayment(workspaceId, string.Empty)).GetAwaiter().GetResult();

            Assert.Equal(4, repo.GetAllAsync().GetAwaiter().GetResult().Count);
        });
    }

    [Fact]
    public void Idempotent_service_write_converts_a_unique_conflict_into_the_existing_record()
    {
        WithTempDatabase(path =>
        {
            var factory = new SqliteConnectionFactory(path);
            InitSchema(factory);
            var payments = new PaymentRecordRepository(factory);
            var cashFlows = new CashFlowEntryRepository(factory);
            var service = new CommercePaymentService(payments, cashFlows, factory);
            Guid workspaceId = Guid.NewGuid();

            service.RecordPaymentAsync(NewPayment(workspaceId, "idem-key"), PaymentCashFlowOptions.Generate()).GetAwaiter().GetResult();
            // A second create for the same key reuses the existing records, never a duplicate.
            service.RecordPaymentAsync(NewPayment(workspaceId, "idem-key"), PaymentCashFlowOptions.Generate()).GetAwaiter().GetResult();

            Assert.Single(payments.GetAllAsync().GetAwaiter().GetResult());
            Assert.Single(cashFlows.GetAllAsync().GetAwaiter().GetResult());
        });
    }

    [Fact]
    public void Concurrent_idempotent_writes_for_the_same_key_yield_one_record()
    {
        WithTempDatabase(path =>
        {
            var factory = new SqliteConnectionFactory(path);
            InitSchema(factory);
            var payments = new PaymentRecordRepository(factory);
            var cashFlows = new CashFlowEntryRepository(factory);
            var service = new CommercePaymentService(payments, cashFlows, factory);
            Guid workspaceId = Guid.NewGuid();

            // Fire several idempotent writes for the same key in parallel; the partial UNIQUE index
            // plus the conflict-to-idempotent-success handling must leave exactly one persisted record.
            var tasks = new List<Task<PaymentResult>>();
            for (int i = 0; i < 6; i++)
            {
                tasks.Add(Task.Run(() =>
                    service.RecordPaymentAsync(NewPayment(workspaceId, "race-key"), PaymentCashFlowOptions.Generate())));
            }

            Task.WhenAll(tasks).GetAwaiter().GetResult();

            Assert.Single(payments.GetAllAsync().GetAwaiter().GetResult());
            Assert.Single(cashFlows.GetAllAsync().GetAwaiter().GetResult());
        });
    }

    // --- Helpers ---

    private static PaymentRecord NewPayment(Guid workspaceId, string? businessKey) => new()
    {
        Id = Guid.NewGuid(),
        CreatedAt = DateTime.UtcNow,
        WorkspaceId = workspaceId,
        Amount = CommerceMoney.From(10m),
        PaidAt = DateTime.UtcNow,
        BusinessKey = businessKey,
    };

    private static void InitSchema(SqliteConnectionFactory factory)
        => new CommerceSchemaInitializer(factory).InitializeAsync().GetAwaiter().GetResult();

    private static bool IndexExists(string path, string indexName)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'index' AND name = $name LIMIT 1;";
        command.Parameters.AddWithValue("$name", indexName);
        return command.ExecuteScalar() is not null;
    }

    private static void WithTempDatabase(Action<string> action)
    {
        string path = Path.Combine(Path.GetTempPath(), $"orderly-bk-index-{Guid.NewGuid():N}.db");
        try
        {
            action(path);
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
}
