using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Core.Commerce.Services;

namespace Orderly.Data.Commerce.Services;

/// <summary>
/// Commerce Service Layer implementation of <see cref="ICashFlowService"/> over the
/// Universal_Domain_Model (Req 4.1, 4.12). Records income/expense/receivable/payable entries, settles
/// receivable/payable entries through <see cref="CashFlowSettlementStatus"/>, produces period
/// summaries, and computes a bounded integer cash-flow health score in [0, 100].
///
/// <para><b>Representation.</b> Receivable and payable are not directions: a receivable is an
/// <see cref="CashFlowDirection.Income"/> entry that starts unsettled, and a payable is an
/// <see cref="CashFlowDirection.Expense"/> entry that starts unsettled. Settlement state is carried by
/// <see cref="CashFlowSettlementStatus"/> together with due dates (Req 4.12), and the
/// <see cref="CashFlowDirection.Transfer"/> direction (neutral account-to-account movement) is
/// excluded from income/expense summaries.</para>
///
/// <para>This type is industry-agnostic and free of any Forbidden_Term, and reads/writes only through
/// the Commerce repositories so the P0_Security_System (C-2) is unaffected.</para>
/// </summary>
public sealed class CommerceCashFlowService : ICashFlowService
{
    private readonly ICashFlowEntryRepository _cashFlowEntryRepository;

    /// <summary>
    /// Creates the service over the Commerce cash-flow repository.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when the repository is null.</exception>
    public CommerceCashFlowService(ICashFlowEntryRepository cashFlowEntryRepository)
    {
        _cashFlowEntryRepository = cashFlowEntryRepository ?? throw new ArgumentNullException(nameof(cashFlowEntryRepository));
    }

    /// <inheritdoc />
    public Task<CashFlowEntry> RecordIncomeAsync(CashFlowEntryInput input, CancellationToken cancellationToken = default)
        => RecordSettledAsync(input, CashFlowDirection.Income, cancellationToken);

    /// <inheritdoc />
    public Task<CashFlowEntry> RecordExpenseAsync(CashFlowEntryInput input, CancellationToken cancellationToken = default)
        => RecordSettledAsync(input, CashFlowDirection.Expense, cancellationToken);

    /// <inheritdoc />
    public Task<CashFlowEntry> RecordReceivableAsync(CashFlowEntryInput input, CancellationToken cancellationToken = default)
        => RecordOutstandingAsync(input, CashFlowDirection.Income, cancellationToken);

    /// <inheritdoc />
    public Task<CashFlowEntry> RecordPayableAsync(CashFlowEntryInput input, CancellationToken cancellationToken = default)
        => RecordOutstandingAsync(input, CashFlowDirection.Expense, cancellationToken);

    /// <inheritdoc />
    public async Task<CashFlowEntry> SettleAsync(
        Guid entryId,
        CommerceMoney amount,
        DateTime asOfUtc,
        CancellationToken cancellationToken = default)
    {
        if (amount.Amount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount.Amount, "A settlement amount must not be negative.");
        }

        CashFlowEntry entry = await _cashFlowEntryRepository
            .GetByIdAsync(entryId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Cash-flow entry '{entryId}' was not found.");

        // Increase the settled amount, capped at the entry's gross amount so over-settlement is not
        // recorded, then recompute the settlement status as of the supplied instant (Req 4.12).
        decimal newSettled = Math.Min(entry.SettledAmount.Amount + amount.Amount, entry.Amount.Amount);
        entry.SettledAmount = CommerceMoney.From(newSettled);
        entry.SettlementStatus = ResolveSettlementStatus(newSettled, entry.Amount.Amount, entry.DueDate, asOfUtc);

        await _cashFlowEntryRepository.UpdateAsync(entry, cancellationToken).ConfigureAwait(false);
        return entry;
    }

    /// <inheritdoc />
    public async Task<CashFlowPeriodSummary> GetPeriodSummaryAsync(
        DateRange period,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CashFlowEntry> entries = await _cashFlowEntryRepository
            .GetAllAsync(cancellationToken)
            .ConfigureAwait(false);

        // Aggregation (period filter + transfer exclusion + health score) is delegated to the shared
        // pure calculator so the page can reuse the identical computation without a second read.
        return CashFlowSummaryCalculator.Compute(entries, period);
    }

    /// <inheritdoc />
    public async Task<int> ComputeHealthScoreAsync(DateRange period, CancellationToken cancellationToken = default)
    {
        CashFlowPeriodSummary summary = await GetPeriodSummaryAsync(period, cancellationToken).ConfigureAwait(false);
        return summary.HealthScore;
    }

    /// <summary>Records a fully settled income/expense entry (its settled amount equals its gross amount).</summary>
    private async Task<CashFlowEntry> RecordSettledAsync(
        CashFlowEntryInput input,
        CashFlowDirection direction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var entry = new CashFlowEntry
        {
            WorkspaceId = input.WorkspaceId,
            Direction = direction,
            Amount = input.Amount,
            SettledAmount = input.Amount,
            SettlementStatus = CashFlowSettlementStatus.Settled,
            OccurredAt = input.OccurredAt,
            DueDate = input.DueDate,
            CategoryName = input.CategoryName,
            OrderId = input.OrderId,
            BusinessKey = input.BusinessKey,
            CustomFieldsJson = input.CustomFieldsJson,
        };

        // Idempotent by Business_Key (Req 4.20, 18.6): a settled entry carrying a Business_Key that
        // already exists is reused rather than duplicated. An entry with no Business_Key is created
        // normally.
        return await BusinessKeyIdempotency
            .CreateIdempotentAsync(_cashFlowEntryRepository, entry, e => e.BusinessKey, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Records an outstanding receivable/payable entry: zero settled amount, with its initial status
    /// resolved from the due date relative to the entry's occurrence (Req 4.12).
    /// </summary>
    private async Task<CashFlowEntry> RecordOutstandingAsync(
        CashFlowEntryInput input,
        CashFlowDirection direction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        // An entry already past its due date at the moment it occurs starts Overdue; otherwise Pending.
        CashFlowSettlementStatus status = input.DueDate is DateTime due && due < input.OccurredAt
            ? CashFlowSettlementStatus.Overdue
            : CashFlowSettlementStatus.Pending;

        var entry = new CashFlowEntry
        {
            WorkspaceId = input.WorkspaceId,
            Direction = direction,
            Amount = input.Amount,
            SettledAmount = CommerceMoney.Zero,
            SettlementStatus = status,
            OccurredAt = input.OccurredAt,
            DueDate = input.DueDate,
            CategoryName = input.CategoryName,
            OrderId = input.OrderId,
            BusinessKey = input.BusinessKey,
            CustomFieldsJson = input.CustomFieldsJson,
        };

        // Idempotent by Business_Key (Req 4.20, 18.6): an outstanding entry carrying a Business_Key
        // that already exists is reused rather than duplicated. An entry with no Business_Key is
        // created normally.
        return await BusinessKeyIdempotency
            .CreateIdempotentAsync(_cashFlowEntryRepository, entry, e => e.BusinessKey, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the settlement status for an entry given how much is now settled, its gross amount,
    /// its optional due date, and the evaluation instant: fully settled is
    /// <see cref="CashFlowSettlementStatus.Settled"/>; otherwise a past-due entry is
    /// <see cref="CashFlowSettlementStatus.Overdue"/>, a partially settled entry is
    /// <see cref="CashFlowSettlementStatus.PartiallySettled"/>, and an untouched entry is
    /// <see cref="CashFlowSettlementStatus.Pending"/>.
    /// </summary>
    private static CashFlowSettlementStatus ResolveSettlementStatus(
        decimal settled,
        decimal gross,
        DateTime? dueDate,
        DateTime asOfUtc)
    {
        if (settled >= gross)
        {
            return CashFlowSettlementStatus.Settled;
        }

        if (dueDate is DateTime due && asOfUtc > due)
        {
            return CashFlowSettlementStatus.Overdue;
        }

        return settled > 0m
            ? CashFlowSettlementStatus.PartiallySettled
            : CashFlowSettlementStatus.Pending;
    }
}
