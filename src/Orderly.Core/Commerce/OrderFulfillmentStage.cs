namespace Orderly.Core.Commerce;

/// <summary>
/// The fulfillment dimension of an order's three independent stage dimensions (Req 4.3).
/// Evolves independently of sales and payment.
/// </summary>
public enum OrderFulfillmentStage
{
    NotStarted = 0,
    InProgress = 1,
    Ready = 2,
    Fulfilled = 3,
    Returned = 4
}
