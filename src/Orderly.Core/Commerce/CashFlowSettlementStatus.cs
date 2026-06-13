namespace Orderly.Core.Commerce;

/// <summary>
/// The settlement state of a cash-flow entry. Used together with due dates to represent
/// receivable and payable entries (Req 4.12), rather than encoding them as a direction.
/// </summary>
public enum CashFlowSettlementStatus
{
    /// <summary>Fully settled; no outstanding amount remains.</summary>
    Settled = 0,

    /// <summary>Outstanding and awaiting settlement (receivable or payable).</summary>
    Pending = 1,

    /// <summary>Partially settled; an outstanding balance remains.</summary>
    PartiallySettled = 2,

    /// <summary>Outstanding and past its due date.</summary>
    Overdue = 3
}
