using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Core.Commerce.Services;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Commerce.Services;

/// <summary>
/// Commerce Service Layer implementation of <see cref="IPaymentService"/> over the
/// Universal_Domain_Model (Req 4.1, 4.18). Records <see cref="PaymentRecord"/> values and generates
/// or links <b>at most one</b> corresponding <see cref="CashFlowEntry"/> for each payment.
///
/// <para><b>At-most-one cardinality (Req 4.18).</b> A payment holds a single nullable
/// <see cref="PaymentRecord.CashFlowEntryId"/>, which structurally limits it to one linked entry.
/// This service additionally guarantees the behavior: it generates a new entry only when the payment
/// is not already linked, so re-passing an already-linked payment reuses the existing link and never
/// creates a second entry. The generated entry and the payment reference each other
/// (<see cref="CashFlowEntry.PaymentRecordId"/> ↔ <see cref="PaymentRecord.CashFlowEntryId"/>).</para>
///
/// <para>This type is industry-agnostic and free of any Forbidden_Term, and reads/writes only through
/// the Commerce repositories so the P0_Security_System (C-2) is unaffected.</para>
/// </summary>
public sealed class CommercePaymentService : IPaymentService
{
    private readonly IPaymentRecordRepository _paymentRecordRepository;
    private readonly ICashFlowEntryRepository _cashFlowEntryRepository;
    private readonly SqliteConnectionFactory? _connectionFactory;

    /// <summary>
    /// Creates the service over the Commerce payment and cash-flow repositories.
    /// </summary>
    /// <param name="paymentRecordRepository">Repository for <see cref="PaymentRecord"/> persistence.</param>
    /// <param name="cashFlowEntryRepository">Repository for <see cref="CashFlowEntry"/> persistence.</param>
    /// <param name="connectionFactory">
    /// Optional connection factory used to wrap the payment and its generated/linked cash-flow entry
    /// in a single <see cref="CoreWriteTransaction"/> so the two are written all-or-nothing (Req 18.1,
    /// 18.3). When omitted (or when a core write transaction is already active on the current context)
    /// the writes enroll in the ambient transaction instead of opening a nested one.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when either repository is null.</exception>
    public CommercePaymentService(
        IPaymentRecordRepository paymentRecordRepository,
        ICashFlowEntryRepository cashFlowEntryRepository,
        SqliteConnectionFactory? connectionFactory = null)
    {
        _paymentRecordRepository = paymentRecordRepository ?? throw new ArgumentNullException(nameof(paymentRecordRepository));
        _cashFlowEntryRepository = cashFlowEntryRepository ?? throw new ArgumentNullException(nameof(cashFlowEntryRepository));
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<PaymentResult> RecordPaymentAsync(
        PaymentRecord payment,
        PaymentCashFlowOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payment);
        options ??= PaymentCashFlowOptions.None;

        // Idempotency by Business_Key (Req 4.20, 18.6): if a payment carrying the same Business_Key
        // has already been recorded, reuse it together with its single linked cash-flow entry rather
        // than inserting a duplicate. Re-running the same payment therefore produces no duplicate
        // financial records. A payment without a Business_Key opts out and is recorded normally.
        PaymentRecord? duplicate = await BusinessKeyIdempotency
            .FindByBusinessKeyAsync(_paymentRecordRepository, payment.BusinessKey, p => p.BusinessKey, cancellationToken)
            .ConfigureAwait(false);
        if (duplicate is not null)
        {
            CashFlowEntry? linkedEntry = duplicate.CashFlowEntryId is Guid duplicateLinkId
                ? await _cashFlowEntryRepository.GetByIdAsync(duplicateLinkId, cancellationToken).ConfigureAwait(false)
                : null;

            return new PaymentResult { Payment = duplicate, CashFlowEntry = linkedEntry };
        }

        // If the payment is already linked to an entry, reuse that single link rather than creating a
        // second one — a payment links to at most one cash-flow entry (Req 4.18).
        if (payment.CashFlowEntryId is Guid existingLinkId)
        {
            PaymentRecord persistedExisting = await _paymentRecordRepository
                .CreateAsync(payment, cancellationToken)
                .ConfigureAwait(false);

            CashFlowEntry? linked = await _cashFlowEntryRepository
                .GetByIdAsync(existingLinkId, cancellationToken)
                .ConfigureAwait(false);

            return new PaymentResult { Payment = persistedExisting, CashFlowEntry = linked };
        }

        return options.Behavior switch
        {
            PaymentCashFlowBehavior.Generate => await WithAtomicWriteAsync(
                () => RecordWithGeneratedEntryAsync(payment, options, cancellationToken), cancellationToken).ConfigureAwait(false),
            PaymentCashFlowBehavior.LinkExisting => await WithAtomicWriteAsync(
                () => RecordWithLinkedEntryAsync(payment, options, cancellationToken), cancellationToken).ConfigureAwait(false),
            _ => await RecordWithoutEntryAsync(payment, cancellationToken).ConfigureAwait(false),
        };
    }

    /// <summary>
    /// Runs <paramref name="write"/> inside a single <see cref="CoreWriteTransaction"/> so the payment
    /// and its generated/linked cash-flow entry commit or roll back together (Req 18.1, 18.3): if the
    /// second write throws, the first is rolled back and no orphan record remains. When a transaction
    /// is already active on the current execution context (for example payment is recorded inside an
    /// order-completion core write) the writes enroll in that ambient transaction rather than nesting,
    /// preserving overall atomicity. When no connection factory was supplied the write runs as-is.
    /// </summary>
    private async Task<PaymentResult> WithAtomicWriteAsync(Func<Task<PaymentResult>> write, CancellationToken cancellationToken)
    {
        if (_connectionFactory is null || CoreWriteTransaction.Current is not null)
        {
            return await write().ConfigureAwait(false);
        }

        using CoreWriteTransaction transaction = CoreWriteTransaction.Begin(_connectionFactory);
        PaymentResult result = await write().ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>Persists the payment with no associated cash-flow entry.</summary>
    private async Task<PaymentResult> RecordWithoutEntryAsync(
        PaymentRecord payment,
        CancellationToken cancellationToken)
    {
        PaymentRecord persisted = await _paymentRecordRepository
            .CreateAsync(payment, cancellationToken)
            .ConfigureAwait(false);

        return new PaymentResult { Payment = persisted, CashFlowEntry = null };
    }

    /// <summary>
    /// Generates exactly one cash-flow entry from the payment, links the two in both directions, and
    /// persists both (Req 4.18). The entry takes its amount, timestamp, and order link from the payment.
    /// </summary>
    private async Task<PaymentResult> RecordWithGeneratedEntryAsync(
        PaymentRecord payment,
        PaymentCashFlowOptions options,
        CancellationToken cancellationToken)
    {
        var entry = new CashFlowEntry
        {
            WorkspaceId = payment.WorkspaceId,
            Direction = options.Direction,
            Amount = payment.Amount,
            SettledAmount = payment.Amount,
            SettlementStatus = CashFlowSettlementStatus.Settled,
            OccurredAt = payment.PaidAt,
            CategoryName = options.CategoryName,
            OrderId = payment.OrderId,
            PaymentRecordId = payment.Id,
            // Derive the entry's Business_Key from the payment's so that re-generating the entry for an
            // idempotent payment looks up and reuses the existing entry rather than inserting a
            // duplicate (Req 4.20, 18.6). A payment with no Business_Key yields an unkeyed entry.
            BusinessKey = GeneratedEntryBusinessKey(payment.BusinessKey),
        };

        // Persist the entry idempotently by Business_Key, then link the payment to whichever entry is
        // now persisted (an existing keyed entry or the newly created one).
        CashFlowEntry persistedEntry = await BusinessKeyIdempotency
            .CreateIdempotentAsync(_cashFlowEntryRepository, entry, e => e.BusinessKey, cancellationToken)
            .ConfigureAwait(false);

        payment.CashFlowEntryId = persistedEntry.Id;

        PaymentRecord persistedPayment = await BusinessKeyIdempotency
            .CreateIdempotentAsync(_paymentRecordRepository, payment, p => p.BusinessKey, cancellationToken)
            .ConfigureAwait(false);

        return new PaymentResult { Payment = persistedPayment, CashFlowEntry = persistedEntry };
    }

    /// <summary>
    /// Links the payment to a single pre-existing cash-flow entry in both directions and persists the
    /// change (Req 4.18). No new entry is generated.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the referenced entry does not exist.</exception>
    private async Task<PaymentResult> RecordWithLinkedEntryAsync(
        PaymentRecord payment,
        PaymentCashFlowOptions options,
        CancellationToken cancellationToken)
    {
        Guid entryId = options.ExistingCashFlowEntryId
            ?? throw new InvalidOperationException("A cash-flow entry id is required to link an existing entry.");

        CashFlowEntry entry = await _cashFlowEntryRepository
            .GetByIdAsync(entryId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Cash-flow entry '{entryId}' was not found.");

        // Establish the single bidirectional link, then persist the payment and the updated entry.
        payment.CashFlowEntryId = entry.Id;
        entry.PaymentRecordId = payment.Id;

        PaymentRecord persistedPayment = await _paymentRecordRepository
            .CreateAsync(payment, cancellationToken)
            .ConfigureAwait(false);
        await _cashFlowEntryRepository
            .UpdateAsync(entry, cancellationToken)
            .ConfigureAwait(false);

        return new PaymentResult { Payment = persistedPayment, CashFlowEntry = entry };
    }

    /// <summary>
    /// Derives the stable Business_Key for the single cash-flow entry generated from a payment, so the
    /// entry can be looked up and reused on a re-run rather than duplicated (Req 4.20, 18.6). Returns
    /// <c>null</c> when the payment itself has no Business_Key, leaving the generated entry unkeyed
    /// (and therefore always inserted) to preserve ad-hoc payment behavior. Contains no Forbidden_Term.
    /// </summary>
    private static string? GeneratedEntryBusinessKey(string? paymentBusinessKey)
        => string.IsNullOrEmpty(paymentBusinessKey) ? null : $"payment-cashflow:{paymentBusinessKey}";
}
