namespace Orderly.Core.Commerce.Services;

/// <summary>
/// The fields needed to record a single <see cref="CashFlowEntry"/> through
/// <see cref="ICashFlowService"/> (Req 4.12). The same input shape is reused for income, expense,
/// receivable, and payable entries; the service derives the entry's <see cref="CashFlowDirection"/>
/// and initial <see cref="CashFlowSettlementStatus"/> from the recording method invoked, so callers
/// never have to set those (and cannot set them inconsistently).
///
/// <para>Receivable and payable entries are ordinary income/expense entries that start unsettled and
/// carry a <see cref="DueDate"/>; they are never modeled as a <see cref="CashFlowDirection.Transfer"/>
/// and never repurpose the transfer direction.</para>
/// </summary>
public sealed record CashFlowEntryInput
{
    /// <summary>The owning workspace for the entry. Required.</summary>
    public required Guid WorkspaceId { get; init; }

    /// <summary>The gross amount of the entry. Monetary, scale 2. Expected to be non-negative.</summary>
    public required CommerceMoney Amount { get; init; }

    /// <summary>The UTC moment the cash-flow event occurred. Defaults to <see cref="DateTime.UtcNow"/> when unset.</summary>
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

    /// <summary>An optional income or expense category label.</summary>
    public string? CategoryName { get; init; }

    /// <summary>An optional link to the originating <see cref="Order"/>.</summary>
    public Guid? OrderId { get; init; }

    /// <summary>
    /// An optional due date. Meaningful for receivable and payable entries; used to flag an
    /// outstanding entry as <see cref="CashFlowSettlementStatus.Overdue"/> once it is past due.
    /// </summary>
    public DateTime? DueDate { get; init; }

    /// <summary>An optional stable business key for idempotent generation by the service layer (Req 4.20, 18.6).</summary>
    public string? BusinessKey { get; init; }

    /// <summary>Optional personalization payload stored verbatim on the entry.</summary>
    public string? CustomFieldsJson { get; init; }
}

/// <summary>
/// A period cash-flow summary produced by <see cref="ICashFlowService.GetPeriodSummaryAsync"/>
/// (Req 4.12). All aggregates are derived from the entries whose <see cref="CashFlowEntry.OccurredAt"/>
/// falls within the requested inclusive <see cref="Period"/>. <see cref="CashFlowDirection.Transfer"/>
/// entries are net-zero with respect to income and expense and are therefore excluded from every
/// aggregate.
/// </summary>
public sealed record CashFlowPeriodSummary
{
    /// <summary>The inclusive window these figures summarize.</summary>
    public required DateRange Period { get; init; }

    /// <summary>Total settled income received within the period. Monetary, scale 2.</summary>
    public CommerceMoney RealizedIncome { get; init; }

    /// <summary>Total settled expense paid within the period. Monetary, scale 2.</summary>
    public CommerceMoney RealizedExpense { get; init; }

    /// <summary>Net realized cash flow: <see cref="RealizedIncome"/> minus <see cref="RealizedExpense"/>. Monetary, scale 2.</summary>
    public CommerceMoney NetCashFlow { get; init; }

    /// <summary>Outstanding (not-yet-settled) receivable balance within the period. Monetary, scale 2.</summary>
    public CommerceMoney OutstandingReceivable { get; init; }

    /// <summary>Outstanding (not-yet-settled) payable balance within the period. Monetary, scale 2.</summary>
    public CommerceMoney OutstandingPayable { get; init; }

    /// <summary>
    /// The cash-flow health score for the period, expressed as an integer in the inclusive range
    /// [0, 100] (Req 4.12). Higher is healthier.
    /// </summary>
    public int HealthScore { get; init; }
}

/// <summary>
/// Universal cash-flow service (Req 4.1, 4.12). Records income, expense, receivable, and payable
/// entries, settles receivable/payable entries through <see cref="CashFlowSettlementStatus"/>,
/// produces period summaries, and computes a cash-flow health score expressed as an integer in the
/// inclusive range [0, 100].
///
/// <para><b>Direction and settlement model.</b> <see cref="CashFlowDirection"/> has exactly three
/// values — income, expense, and transfer. Receivable and payable are <i>not</i> directions: a
/// receivable is an income entry that starts unsettled, and a payable is an expense entry that starts
/// unsettled. Both carry their outstanding state in <see cref="CashFlowSettlementStatus"/> and an
/// optional due date, and neither removes nor repurposes <see cref="CashFlowDirection.Transfer"/>.</para>
///
/// <para>All time-sensitive operations take an explicit UTC instant so results are deterministic and
/// reproducible with no hidden wall-clock dependency. This contract is industry-agnostic and free of
/// any Forbidden_Term, and reads/writes only through the Commerce repositories so the
/// P0_Security_System (C-2) is unaffected.</para>
/// </summary>
public interface ICashFlowService
{
    /// <summary>
    /// Records a settled income entry: <see cref="CashFlowDirection.Income"/> with
    /// <see cref="CashFlowSettlementStatus.Settled"/> and a settled amount equal to the gross amount.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="input"/> is null.</exception>
    Task<CashFlowEntry> RecordIncomeAsync(CashFlowEntryInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a settled expense entry: <see cref="CashFlowDirection.Expense"/> with
    /// <see cref="CashFlowSettlementStatus.Settled"/> and a settled amount equal to the gross amount.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="input"/> is null.</exception>
    Task<CashFlowEntry> RecordExpenseAsync(CashFlowEntryInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a receivable: an <see cref="CashFlowDirection.Income"/> entry that starts unsettled
    /// (settled amount zero). Its initial settlement status is
    /// <see cref="CashFlowSettlementStatus.Overdue"/> when a <see cref="CashFlowEntryInput.DueDate"/>
    /// is present and already in the past relative to the entry's occurrence, otherwise
    /// <see cref="CashFlowSettlementStatus.Pending"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="input"/> is null.</exception>
    Task<CashFlowEntry> RecordReceivableAsync(CashFlowEntryInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a payable: an <see cref="CashFlowDirection.Expense"/> entry that starts unsettled
    /// (settled amount zero). Its initial settlement status follows the same due-date rule as
    /// <see cref="RecordReceivableAsync"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="input"/> is null.</exception>
    Task<CashFlowEntry> RecordPayableAsync(CashFlowEntryInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Settles part or all of an outstanding receivable or payable entry by increasing its
    /// <see cref="CashFlowEntry.SettledAmount"/> by <paramref name="amount"/> (capped at the entry's
    /// gross amount) and recomputing its <see cref="CashFlowSettlementStatus"/> as of
    /// <paramref name="asOfUtc"/>: fully settled becomes <see cref="CashFlowSettlementStatus.Settled"/>;
    /// otherwise an entry past its due date becomes <see cref="CashFlowSettlementStatus.Overdue"/>, a
    /// partially settled entry becomes <see cref="CashFlowSettlementStatus.PartiallySettled"/>, and an
    /// untouched entry remains <see cref="CashFlowSettlementStatus.Pending"/>.
    /// </summary>
    /// <param name="entryId">The identity of the entry to settle.</param>
    /// <param name="amount">The amount to apply to the outstanding balance. Must not be negative.</param>
    /// <param name="asOfUtc">The UTC instant used to evaluate whether a still-outstanding entry is overdue.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The updated entry.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="amount"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the entry does not exist.</exception>
    Task<CashFlowEntry> SettleAsync(
        Guid entryId,
        CommerceMoney amount,
        DateTime asOfUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Produces the cash-flow summary for the supplied inclusive <paramref name="period"/>, including
    /// realized income/expense, net cash flow, outstanding receivable/payable balances, and the
    /// integer health score in [0, 100] (Req 4.12).
    /// </summary>
    Task<CashFlowPeriodSummary> GetPeriodSummaryAsync(DateRange period, CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes the cash-flow health score for the supplied inclusive <paramref name="period"/> as an
    /// integer in the inclusive range [0, 100] (Req 4.12). Higher is healthier.
    /// </summary>
    Task<int> ComputeHealthScoreAsync(DateRange period, CancellationToken cancellationToken = default);
}
