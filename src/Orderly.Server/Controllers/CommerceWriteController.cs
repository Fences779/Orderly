using Microsoft.AspNetCore.Mvc;
using Orderly.Contracts.Commerce;
using Orderly.Server.Services;

namespace Orderly.Server.Controllers;

[Route("api/workspaces/{workspaceId:guid}")]
public class CommerceWriteController : CloudControllerBase
{
    public CommerceWriteController(ICurrentUserContext currentUser, ICloudAuthService authService, ICloudPermissionService permissions)
        : base(currentUser, authService, permissions)
    {
    }

    [HttpPost("orders")]
    public Task<ActionResult<CloudOrderDto>> CreateOrderAsync(Guid workspaceId, [FromBody] CreateOrderCommand command)
        => Task.FromResult<ActionResult<CloudOrderDto>>(StatusCode(501, new { Error = "Not implemented in this stage." }));

    [HttpPut("orders/{orderId:guid}")]
    public Task<ActionResult<CloudOrderDto>> UpdateOrderAsync(Guid workspaceId, Guid orderId, [FromBody] UpdateOrderCommand command)
        => Task.FromResult<ActionResult<CloudOrderDto>>(StatusCode(501, new { Error = "Not implemented in this stage." }));

    [HttpPost("orders/{orderId:guid}/complete")]
    public Task<IActionResult> CompleteOrderAsync(Guid workspaceId, Guid orderId, [FromBody] CompleteOrderCommand command)
        => Task.FromResult<IActionResult>(StatusCode(501, new { Error = "Not implemented in this stage." }));

    [HttpPost("orders/{orderId:guid}/stage")]
    public Task<IActionResult> StageOrderAsync(Guid workspaceId, Guid orderId, [FromBody] OrderStageCommand command)
        => Task.FromResult<IActionResult>(StatusCode(501, new { Error = "Not implemented in this stage." }));

    [HttpPost("orders/{orderId:guid}/payment-status")]
    public Task<IActionResult> UpdatePaymentStatusAsync(Guid workspaceId, Guid orderId, [FromBody] OrderStageCommand command)
        => Task.FromResult<IActionResult>(StatusCode(501, new { Error = "Not implemented in this stage." }));

    [HttpPost("orders/{orderId:guid}/fulfillment-status")]
    public Task<IActionResult> UpdateFulfillmentStatusAsync(Guid workspaceId, Guid orderId, [FromBody] OrderStageCommand command)
        => Task.FromResult<IActionResult>(StatusCode(501, new { Error = "Not implemented in this stage." }));

    [HttpPost("inventory/movements")]
    public Task<IActionResult> RecordMovementAsync(Guid workspaceId, [FromBody] InventoryMovementCommand command)
        => Task.FromResult<IActionResult>(StatusCode(501, new { Error = "Not implemented in this stage." }));

    [HttpPost("inventory/stocktake-adjustments")]
    public Task<IActionResult> StocktakeAdjustmentAsync(Guid workspaceId, [FromBody] InventoryMovementCommand command)
        => Task.FromResult<IActionResult>(StatusCode(501, new { Error = "Not implemented in this stage." }));

    [HttpPost("customers")]
    public Task<ActionResult<CloudCustomerDto>> CreateCustomerAsync(Guid workspaceId, [FromBody] CreateCustomerCommand command)
        => Task.FromResult<ActionResult<CloudCustomerDto>>(StatusCode(501, new { Error = "Not implemented in this stage." }));

    [HttpPut("customers/{customerId:guid}")]
    public Task<ActionResult<CloudCustomerDto>> UpdateCustomerAsync(Guid workspaceId, Guid customerId, [FromBody] UpdateCustomerCommand command)
        => Task.FromResult<ActionResult<CloudCustomerDto>>(StatusCode(501, new { Error = "Not implemented in this stage." }));

    [HttpPost("customers/{customerId:guid}/notes")]
    public Task<IActionResult> AddCustomerNoteAsync(Guid workspaceId, Guid customerId, [FromBody] CustomerNoteCommand command)
        => Task.FromResult<IActionResult>(StatusCode(501, new { Error = "Not implemented in this stage." }));

    [HttpPost("cashflow/income")]
    public Task<ActionResult<CloudCashFlowEntryDto>> RecordIncomeAsync(Guid workspaceId, [FromBody] CashFlowEntryCommand command)
        => Task.FromResult<ActionResult<CloudCashFlowEntryDto>>(StatusCode(501, new { Error = "Not implemented in this stage." }));

    [HttpPost("cashflow/expense")]
    public Task<ActionResult<CloudCashFlowEntryDto>> RecordExpenseAsync(Guid workspaceId, [FromBody] CashFlowEntryCommand command)
        => Task.FromResult<ActionResult<CloudCashFlowEntryDto>>(StatusCode(501, new { Error = "Not implemented in this stage." }));

    [HttpPost("cashflow/receivable")]
    public Task<ActionResult<CloudCashFlowEntryDto>> RecordReceivableAsync(Guid workspaceId, [FromBody] CashFlowEntryCommand command)
        => Task.FromResult<ActionResult<CloudCashFlowEntryDto>>(StatusCode(501, new { Error = "Not implemented in this stage." }));

    [HttpPost("cashflow/payable")]
    public Task<ActionResult<CloudCashFlowEntryDto>> RecordPayableAsync(Guid workspaceId, [FromBody] CashFlowEntryCommand command)
        => Task.FromResult<ActionResult<CloudCashFlowEntryDto>>(StatusCode(501, new { Error = "Not implemented in this stage." }));

    [HttpPost("cashflow/{entryId:guid}/settle")]
    public Task<ActionResult<CloudCashFlowEntryDto>> SettleAsync(Guid workspaceId, Guid entryId, [FromBody] SettleCashFlowCommand command)
        => Task.FromResult<ActionResult<CloudCashFlowEntryDto>>(StatusCode(501, new { Error = "Not implemented in this stage." }));
}
