using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Core.Commerce.Services;
using Orderly.Data.Commerce.Repositories;
using Orderly.Data.Commerce.Services;
using Orderly.Data.Sqlite;
using Xunit;

namespace Orderly.Tests.Cashflow;

/// <summary>
/// Tests that <see cref="CommercePaymentService"/> writes a <see cref="PaymentRecord"/> and its
/// generated <see cref="CashFlowEntry"/> inside a single <see cref="CoreWriteTransaction"/> so the
/// two are all-or-nothing (Req 18.1, 18.3): if the payment write fails after the entry write, the
/// entry is rolled back and no orphan cash-flow record remains. Idempotency by Business_Key keeps
/// re-runs from producing duplicates.
/// </summary>
public class PaymentCashFlowTransactionTests
{
    [Fact]
    public void Payment_and_generated_cash_flow_entry_are_both_persisted_and_linked()
    {
        WithTempDatabase((factory, payments, cashFlows) =>
        {
            var service = new CommercePaymentService(payments, cashFlows, factory);
            Guid workspaceId = Guid.NewGuid();

            PaymentResult result = service
                .RecordPaymentAsync(NewPayment(workspaceId, "pay-1"), PaymentCashFlowOptions.Generate())
                .GetAwaiter().GetResult();

            Assert.NotNull(result.Payment);
            Assert.NotNull(result.CashFlowEntry);
            Assert.Equal(result.CashFlowEntry!.Id, result.Payment.CashFlowEntryId);
            Assert.Equal(result.Payment.Id, result.CashFlowEntry.PaymentRecordId);

            Assert.Single(payments.GetAllAsync().GetAwaiter().GetResult());
            Assert.Single(cashFlows.GetAllAsync().GetAwaiter().GetResult());
        });
    }

    [Fact]
    public void Cash_flow_entry_is_rolled_back_when_the_payment_write_fails()
    {
        WithTempDatabase((factory, payments, cashFlows) =>
        {
            // The payment repository fails on insert; the cash-flow entry written just before it must
            // be rolled back by the surrounding Core_Write_Transaction, leaving no orphan entry.
            var throwingPayments = new ThrowingOnCreatePaymentRepository(payments);
            var service = new CommercePaymentService(throwingPayments, cashFlows, factory);
            Guid workspaceId = Guid.NewGuid();

            Assert.ThrowsAny<Exception>(() => service
                .RecordPaymentAsync(NewPayment(workspaceId, "pay-x"), PaymentCashFlowOptions.Generate())
                .GetAwaiter().GetResult());

            Assert.Empty(payments.GetAllAsync().GetAwaiter().GetResult());
            Assert.Empty(cashFlows.GetAllAsync().GetAwaiter().GetResult());
        });
    }

    [Fact]
    public void Nothing_is_persisted_when_the_cash_flow_write_fails()
    {
        WithTempDatabase((factory, payments, cashFlows) =>
        {
            // The cash-flow repository fails on insert (the first write in the generated-entry path);
            // the surrounding transaction must roll back so neither a payment nor an entry remains.
            var throwingCashFlows = new ThrowingOnCreateCashFlowRepository(cashFlows);
            var service = new CommercePaymentService(payments, throwingCashFlows, factory);
            Guid workspaceId = Guid.NewGuid();

            Assert.ThrowsAny<Exception>(() => service
                .RecordPaymentAsync(NewPayment(workspaceId, "pay-cf"), PaymentCashFlowOptions.Generate())
                .GetAwaiter().GetResult());

            Assert.Empty(payments.GetAllAsync().GetAwaiter().GetResult());
            Assert.Empty(cashFlows.GetAllAsync().GetAwaiter().GetResult());
        });
    }

    [Fact]
    public void Resubmitting_the_same_business_key_does_not_duplicate_records()
    {
        WithTempDatabase((factory, payments, cashFlows) =>
        {
            var service = new CommercePaymentService(payments, cashFlows, factory);
            Guid workspaceId = Guid.NewGuid();

            service.RecordPaymentAsync(NewPayment(workspaceId, "dup-key"), PaymentCashFlowOptions.Generate()).GetAwaiter().GetResult();
            service.RecordPaymentAsync(NewPayment(workspaceId, "dup-key"), PaymentCashFlowOptions.Generate()).GetAwaiter().GetResult();

            Assert.Single(payments.GetAllAsync().GetAwaiter().GetResult());
            Assert.Single(cashFlows.GetAllAsync().GetAwaiter().GetResult());
        });
    }

    [Fact]
    public void Retry_after_a_failed_write_can_succeed()
    {
        WithTempDatabase((factory, payments, cashFlows) =>
        {
            var failing = new ThrowingOnCreatePaymentRepository(payments);
            var failingService = new CommercePaymentService(failing, cashFlows, factory);
            Guid workspaceId = Guid.NewGuid();

            Assert.ThrowsAny<Exception>(() => failingService
                .RecordPaymentAsync(NewPayment(workspaceId, "retry-key"), PaymentCashFlowOptions.Generate())
                .GetAwaiter().GetResult());

            // Nothing persisted yet.
            Assert.Empty(cashFlows.GetAllAsync().GetAwaiter().GetResult());

            // A subsequent attempt through the healthy service succeeds and leaves one of each.
            var healthyService = new CommercePaymentService(payments, cashFlows, factory);
            PaymentResult ok = healthyService
                .RecordPaymentAsync(NewPayment(workspaceId, "retry-key"), PaymentCashFlowOptions.Generate())
                .GetAwaiter().GetResult();

            Assert.NotNull(ok.CashFlowEntry);
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
        Amount = CommerceMoney.From(25m),
        PaidAt = DateTime.UtcNow,
        BusinessKey = businessKey,
    };

    private static void WithTempDatabase(Action<SqliteConnectionFactory, PaymentRecordRepository, CashFlowEntryRepository> action)
    {
        string path = Path.Combine(Path.GetTempPath(), $"orderly-pay-tx-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new SqliteConnectionFactory(path);
            new CommerceSchemaInitializer(factory).InitializeAsync().GetAwaiter().GetResult();
            action(factory, new PaymentRecordRepository(factory), new CashFlowEntryRepository(factory));
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

    /// <summary>
    /// Delegates reads to a real payment repository but always throws on insert, to simulate a
    /// failure that occurs <i>after</i> the cash-flow entry has been written within the same
    /// transaction.
    /// </summary>
    private sealed class ThrowingOnCreatePaymentRepository : IPaymentRecordRepository
    {
        private readonly IPaymentRecordRepository _inner;

        public ThrowingOnCreatePaymentRepository(IPaymentRecordRepository inner) => _inner = inner;

        public Task<PaymentRecord> CreateAsync(PaymentRecord entity, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Injected payment write failure.");

        public Task<PaymentRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => _inner.GetByIdAsync(id, cancellationToken);

        public Task<IReadOnlyList<PaymentRecord>> GetAllAsync(CancellationToken cancellationToken = default)
            => _inner.GetAllAsync(cancellationToken);

        public Task<PaymentRecord?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default)
            => _inner.GetByIdIncludingDeletedAsync(id, cancellationToken);

        public Task<IReadOnlyList<PaymentRecord>> GetAllIncludingDeletedAsync(CancellationToken cancellationToken = default)
            => _inner.GetAllIncludingDeletedAsync(cancellationToken);

        public Task UpdateAsync(PaymentRecord entity, CancellationToken cancellationToken = default)
            => _inner.UpdateAsync(entity, cancellationToken);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
            => _inner.DeleteAsync(id, cancellationToken);
    }

    /// <summary>
    /// Delegates reads to a real cash-flow repository but always throws on insert, to simulate a
    /// failure on the first write of the generated-entry path.
    /// </summary>
    private sealed class ThrowingOnCreateCashFlowRepository : ICashFlowEntryRepository
    {
        private readonly ICashFlowEntryRepository _inner;

        public ThrowingOnCreateCashFlowRepository(ICashFlowEntryRepository inner) => _inner = inner;

        public Task<CashFlowEntry> CreateAsync(CashFlowEntry entity, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Injected cash-flow write failure.");

        public Task<CashFlowEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => _inner.GetByIdAsync(id, cancellationToken);

        public Task<IReadOnlyList<CashFlowEntry>> GetAllAsync(CancellationToken cancellationToken = default)
            => _inner.GetAllAsync(cancellationToken);

        public Task<CashFlowEntry?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default)
            => _inner.GetByIdIncludingDeletedAsync(id, cancellationToken);

        public Task<IReadOnlyList<CashFlowEntry>> GetAllIncludingDeletedAsync(CancellationToken cancellationToken = default)
            => _inner.GetAllIncludingDeletedAsync(cancellationToken);

        public Task UpdateAsync(CashFlowEntry entity, CancellationToken cancellationToken = default)
            => _inner.UpdateAsync(entity, cancellationToken);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
            => _inner.DeleteAsync(id, cancellationToken);
    }
}
