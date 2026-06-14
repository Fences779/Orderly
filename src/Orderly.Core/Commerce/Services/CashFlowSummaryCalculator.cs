namespace Orderly.Core.Commerce.Services;

/// <summary>
/// Pure, side-effect-free aggregation for the cash-flow period summary (Req 4.12). The calculation
/// is split out of <see cref="ICashFlowService"/> so it can be reused without an extra data read:
/// callers that already hold the full set of <see cref="CashFlowEntry"/> rows (e.g. a page that also
/// needs to render the recent-entries list) compute the summary from that same in-memory set instead
/// of issuing a second full-table query.
///
/// <para>The result is identical to <see cref="ICashFlowService.GetPeriodSummaryAsync"/>: only
/// entries whose <see cref="CashFlowEntry.OccurredAt"/> falls within the inclusive period contribute,
/// <see cref="CashFlowDirection.Transfer"/> entries are net-zero and excluded from every aggregate,
/// and the health score is a bounded integer in the inclusive range [0, 100].</para>
/// </summary>
public static class CashFlowSummaryCalculator
{
    /// <summary>
    /// Computes the period summary from an already-loaded entry set, applying the period filter and
    /// transfer exclusion in memory. No I/O is performed.
    /// </summary>
    /// <param name="entries">The full set of cash-flow entries to aggregate from.</param>
    /// <param name="period">The inclusive window the figures summarize.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entries"/> is null.</exception>
    public static CashFlowPeriodSummary Compute(IEnumerable<CashFlowEntry> entries, DateRange period)
    {
        ArgumentNullException.ThrowIfNull(entries);

        decimal realizedIncome = 0m, realizedExpense = 0m;
        decimal outstandingReceivable = 0m, outstandingPayable = 0m;

        foreach (CashFlowEntry entry in entries)
        {
            if (entry.Direction == CashFlowDirection.Transfer || !period.Contains(entry.OccurredAt))
            {
                continue;
            }

            decimal amount = entry.Amount.Amount;
            decimal settled = entry.SettledAmount.Amount;
            // The unsettled remainder never goes below zero even if more was settled than billed.
            decimal outstanding = Math.Max(amount - settled, 0m);

            if (entry.Direction == CashFlowDirection.Income)
            {
                realizedIncome += settled;
                outstandingReceivable += outstanding;
            }
            else // Expense
            {
                realizedExpense += settled;
                outstandingPayable += outstanding;
            }
        }

        int healthScore = ComputeHealthScore(realizedIncome, realizedExpense, outstandingReceivable, outstandingPayable);

        return new CashFlowPeriodSummary
        {
            Period = period,
            RealizedIncome = ToMoney(realizedIncome),
            RealizedExpense = ToMoney(realizedExpense),
            NetCashFlow = ToMoney(realizedIncome - realizedExpense),
            OutstandingReceivable = ToMoney(outstandingReceivable),
            OutstandingPayable = ToMoney(outstandingPayable),
            HealthScore = healthScore,
        };
    }

    /// <summary>
    /// Computes the deterministic cash-flow health score as an integer in the inclusive range
    /// [0, 100] (Req 4.12): the inflow share of total cash demand,
    /// <c>inflows / (inflows + outflows)</c> scaled to 0..100, where <i>inflows</i> are realized
    /// income plus outstanding receivables and <i>outflows</i> are realized expense plus outstanding
    /// payables. With no activity at all the score is the maximum.
    /// </summary>
    public static int ComputeHealthScore(
        decimal realizedIncome,
        decimal realizedExpense,
        decimal outstandingReceivable,
        decimal outstandingPayable)
    {
        decimal inflows = realizedIncome + outstandingReceivable;
        decimal outflows = realizedExpense + outstandingPayable;
        decimal total = inflows + outflows;

        // No cash activity at all is treated as fully healthy (there is nothing owed).
        if (total <= 0m)
        {
            return 100;
        }

        int score = (int)Math.Round(Clamp01(inflows / total) * 100m, MidpointRounding.AwayFromZero);
        return Math.Clamp(score, 0, 100);
    }

    /// <summary>
    /// Converts an aggregate decimal to a <see cref="CommerceMoney"/>, rounding to scale 2 and
    /// clamping into the valid monetary range so a large sum over many entries cannot throw.
    /// </summary>
    private static CommerceMoney ToMoney(decimal value)
    {
        decimal rounded = Math.Round(value, CommerceMoney.Scale, MidpointRounding.AwayFromZero);
        decimal clamped = Math.Clamp(rounded, CommerceMoney.MinValue, CommerceMoney.MaxValue);
        return CommerceMoney.From(clamped);
    }

    /// <summary>Clamps a ratio to the inclusive range [0, 1].</summary>
    private static decimal Clamp01(decimal value) => Math.Clamp(value, 0m, 1m);
}
