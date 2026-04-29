namespace Orderly.Core.Models;

public enum PipelineStage
{
    New = 0,
    Contacted = 1,
    Interested = 2,
    Quoted = 3,
    DraftPrepared = 4,
    WaitingPayment = 5,
    Paid = 6,
    Fulfilled = 7,
    Lost = 8
}
