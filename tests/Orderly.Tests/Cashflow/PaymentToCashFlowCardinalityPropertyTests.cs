using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CsCheck;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Services;
using Orderly.Data.Commerce.Repositories;
using Orderly.Data.Commerce.Services;
using Orderly.Data.Sqlite;
using Orderly.Tests.Support;
using Xunit;

namespace Orderly.Tests.Cashflow;

/// <summary>
/// Property-based test for the payment-to-cash-flow cardinality guarantee of
/// <see cref="CommercePaymentService"/> (the <see cref="IPaymentService"/> implementation).
///
/// <para><b>Property 13: Each PaymentRecord links to at most one CashFlowEntry.</b>
/// For ANY sequence of payment-recording operations, every resulting <see cref="PaymentRecord"/> is
/// linked to at most one corresponding <see cref="CashFlowEntry"/>: the payment references at most one
/// entry through <see cref="PaymentRecord.CashFlowEntryId"/>, and at most one entry references the
/// payment back through <see cref="CashFlowEntry.PaymentRecordId"/>. No payment ever produces a second
/// generated or linked entry.</para>
///
/// <para>The property is exercised end-to-end against the real SQLCipher-backed Commerce repositories
/// (an unencrypted temp database, no mocks). Each generated case runs a random sequence of the four
/// mutually exclusive recording behaviors — record-without-entry, generate-one-entry,
/// link-a-pre-existing-entry, and reuse-an-already-linked payment — then asserts the at-most-one
/// cardinality holds across every payment and every entry that was written.</para>
///
/// **Validates: Requirements 4.18**
/// </summary>
public sealed class PaymentToCashFlowCardinalityPropertyTests
{
    private static readonly DateTime PaidAt = new(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>The recording behavior to apply for a single generated operation.</summary>
    private enum OpKind
    {
        /// <summary>Record a payment with no associated cash-flow entry.</summary>
        None = 0,

        /// <summary>Record a payment and generate exactly one new linked cash-flow entry.</summary>
        Generate = 1,

        /// <summary>Record a payment linking it to a freshly created pre-existing cash-flow entry.</summary>
        LinkExisting = 2,

        /// <summary>Record a payment that is already linked, exercising the reuse path (no new entry).</summary>
        ReuseAlreadyLinked = 3,
    }

    // A single operation: its behavior plus the amount (in cents) used for the payment/entry.
    private readonly record struct OpSpec(OpKind Kind, int AmountCents);

    private static readonly Gen<OpKind> OpKindGen =
        Gen.Int[0, 3].Select(i => (OpKind)i);

    // Amounts stay well inside the CommerceMoney range; cents keep the value at scale 2.
    private static readonly Gen<int> AmountCentsGen = Gen.Int[0, 100_000_000];

    private static readonly Gen<OpSpec> OpGen =
        OpKindGen.Select(AmountCentsGen, (kind, cents) => new OpSpec(kind, cents));

    private static readonly Gen<List<OpSpec>> OpsGen =
        OpGen.List[0, 12].Select(list => new List<OpSpec>(list));

    [Fact]
    public void Property13_each_payment_links_to_at_most_one_cash_flow_entry()
    {
        string path = Path.Combine(Path.GetTempPath(), $"orderly-payment-cardinality-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new SqliteConnectionFactory(path);
            new CommerceSchemaInitializer(factory).InitializeAsync().GetAwaiter().GetResult();

            var payments = new PaymentRecordRepository(factory);
            var cashFlows = new CashFlowEntryRepository(factory);
            var service = new CommercePaymentService(payments, cashFlows);

            OpsGen.Sample(
                specs =>
                {
                    Guid workspaceId = Guid.NewGuid();
                    var recordedPaymentIds = new List<Guid>();

                    foreach (OpSpec spec in specs)
                    {
                        CommerceMoney amount = CommerceMoney.From(spec.AmountCents / 100m);
                        PaymentResult result = ApplyOperation(service, cashFlows, workspaceId, spec.Kind, amount);

                        // The result carries at most one entry by construction (a single nullable field),
                        // and when present the payment references exactly that entry.
                        if (result.CashFlowEntry is not null)
                        {
                            Assert.Equal(result.CashFlowEntry.Id, result.Payment.CashFlowEntryId);
                        }

                        recordedPaymentIds.Add(result.Payment.Id);
                    }

                    IReadOnlyList<PaymentRecord> allPayments = payments.GetAllAsync().GetAwaiter().GetResult();
                    IReadOnlyList<CashFlowEntry> allEntries = cashFlows.GetAllAsync().GetAwaiter().GetResult();

                    foreach (Guid paymentId in recordedPaymentIds)
                    {
                        // Each recorded payment was inserted exactly once and is retrievable.
                        PaymentRecord payment = Assert.Single(allPayments.Where(p => p.Id == paymentId));

                        // At most one entry references this payment back via PaymentRecordId.
                        List<CashFlowEntry> backLinks =
                            allEntries.Where(e => e.PaymentRecordId == payment.Id).ToList();
                        Assert.True(
                            backLinks.Count <= 1,
                            $"Payment {payment.Id} is referenced by {backLinks.Count} cash-flow entries (expected at most one).");

                        // The payment references at most one entry, and when set it resolves to exactly one.
                        if (payment.CashFlowEntryId is Guid linkedEntryId)
                        {
                            Assert.Single(allEntries.Where(e => e.Id == linkedEntryId));
                        }
                    }

                    // Globally: no cash-flow entry is the back-link of more than one payment.
                    foreach (IGrouping<Guid, CashFlowEntry> group in allEntries
                        .Where(e => e.PaymentRecordId is not null)
                        .GroupBy(e => e.PaymentRecordId!.Value))
                    {
                        Assert.True(
                            group.Count() <= 1,
                            $"Payment {group.Key} has {group.Count()} generated/linked entries (expected at most one).");
                    }
                },
                iter: PbtConfig.MinIterations);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
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
                    // Best-effort cleanup of temp files.
                }
            }
        }
    }

    /// <summary>Runs one generated operation through the service and returns its result.</summary>
    private static PaymentResult ApplyOperation(
        IPaymentService service,
        CashFlowEntryRepository cashFlows,
        Guid workspaceId,
        OpKind kind,
        CommerceMoney amount)
    {
        switch (kind)
        {
            case OpKind.Generate:
                return service
                    .RecordPaymentAsync(NewPayment(workspaceId, amount), PaymentCashFlowOptions.Generate())
                    .GetAwaiter().GetResult();

            case OpKind.LinkExisting:
            {
                CashFlowEntry existing = CreateStandaloneEntry(cashFlows, workspaceId, amount);
                return service
                    .RecordPaymentAsync(NewPayment(workspaceId, amount), PaymentCashFlowOptions.LinkExisting(existing.Id))
                    .GetAwaiter().GetResult();
            }

            case OpKind.ReuseAlreadyLinked:
            {
                // A payment that already references an entry must reuse that single link and never
                // generate a second entry, even when Generate is requested.
                CashFlowEntry existing = CreateStandaloneEntry(cashFlows, workspaceId, amount);
                PaymentRecord prelinked = NewPayment(workspaceId, amount);
                prelinked.CashFlowEntryId = existing.Id;
                return service
                    .RecordPaymentAsync(prelinked, PaymentCashFlowOptions.Generate())
                    .GetAwaiter().GetResult();
            }

            default:
                return service
                    .RecordPaymentAsync(NewPayment(workspaceId, amount), PaymentCashFlowOptions.None)
                    .GetAwaiter().GetResult();
        }
    }

    private static PaymentRecord NewPayment(Guid workspaceId, CommerceMoney amount) => new()
    {
        Id = Guid.NewGuid(),
        WorkspaceId = workspaceId,
        Amount = amount,
        PaidAt = PaidAt,
    };

    private static CashFlowEntry CreateStandaloneEntry(
        CashFlowEntryRepository cashFlows,
        Guid workspaceId,
        CommerceMoney amount)
    {
        var entry = new CashFlowEntry
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Direction = CashFlowDirection.Income,
            Amount = amount,
            SettledAmount = amount,
            SettlementStatus = CashFlowSettlementStatus.Settled,
            OccurredAt = PaidAt,
        };

        return cashFlows.CreateAsync(entry).GetAwaiter().GetResult();
    }
}
