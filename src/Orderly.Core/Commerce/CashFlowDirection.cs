namespace Orderly.Core.Commerce;

/// <summary>
/// The direction of a cash-flow entry. Exactly three values are defined.
/// <para>
/// <see cref="Transfer"/> represents a neutral account-to-account / internal fund movement that is
/// net-zero with respect to business income and expense. Receivable and payable are NOT directions:
/// they are represented through <see cref="CashFlowSettlementStatus"/> and due dates on income/expense
/// entries, never by removing or repurposing <see cref="Transfer"/>.
/// </para>
/// </summary>
public enum CashFlowDirection
{
    Income = 0,
    Expense = 1,
    Transfer = 2
}
