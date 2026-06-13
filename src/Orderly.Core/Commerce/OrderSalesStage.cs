namespace Orderly.Core.Commerce;

/// <summary>
/// The sales dimension of an order's three independent stage dimensions (Req 4.3).
/// Evolves independently of payment and fulfillment.
/// </summary>
public enum OrderSalesStage
{
    Draft = 0,
    Quoted = 1,
    Confirmed = 2,
    Completed = 3,
    Cancelled = 4
}
