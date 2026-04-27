namespace Orderly.Core.Models;

public enum OrderStatus
{
    PendingCommunication = 0,
    PendingQuote = 1,
    Quoted = 2,
    PendingFollowUp = 3,
    Won = 4,
    Closed = 5
}
