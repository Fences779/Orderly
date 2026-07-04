using Microsoft.AspNetCore.Mvc;
using Orderly.Contracts.Commerce;
using Orderly.Server.Services;

namespace Orderly.Server.Controllers;

[Route("api/workspaces/{workspaceId:guid}")]
public class CommerceWriteController : CloudControllerBase
{
    private readonly CommerceCommandService _commandService;

    public CommerceWriteController(CommerceCommandService commandService, ICurrentUserContext currentUser, ICloudAuthService authService, ICloudPermissionService permissions)
        : base(currentUser, authService, permissions)
    {
        _commandService = commandService;
    }

    [HttpPost("orders")]
    public async Task<ActionResult<CloudOrderDto>> CreateOrderAsync(Guid workspaceId, [FromBody] CreateOrderCommand command)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var result = await _commandService.CreateOrderAsync(workspaceId, command);
        return Ok(result.Value);
    }

    [HttpPut("orders/{orderId:guid}")]
    public async Task<ActionResult<CloudOrderDto>> UpdateOrderAsync(Guid workspaceId, Guid orderId, [FromBody] UpdateOrderCommand command)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        command.OrderId = orderId;
        var result = await _commandService.UpdateOrderAsync(workspaceId, orderId, command);
        return Ok(result.Value);
    }

    [HttpPost("orders/{orderId:guid}/complete")]
    public async Task<ActionResult<CloudOrderDto>> CompleteOrderAsync(Guid workspaceId, Guid orderId, [FromBody] CompleteOrderCommand command)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var result = await _commandService.CompleteOrderAsync(workspaceId, orderId, command);
        return Ok(result.Value);
    }

    [HttpPost("orders/{orderId:guid}/stage")]
    public Task<ActionResult<CloudOrderDto>> StageOrderAsync(Guid workspaceId, Guid orderId, [FromBody] OrderStageCommand command)
        => UpdateOrderStageAsync(workspaceId, orderId, command, "sales");

    [HttpPost("orders/{orderId:guid}/payment-status")]
    public Task<ActionResult<CloudOrderDto>> UpdatePaymentStatusAsync(Guid workspaceId, Guid orderId, [FromBody] OrderStageCommand command)
        => UpdateOrderStageAsync(workspaceId, orderId, command, "payment");

    [HttpPost("orders/{orderId:guid}/fulfillment-status")]
    public Task<ActionResult<CloudOrderDto>> UpdateFulfillmentStatusAsync(Guid workspaceId, Guid orderId, [FromBody] OrderStageCommand command)
        => UpdateOrderStageAsync(workspaceId, orderId, command, "fulfillment");

    private async Task<ActionResult<CloudOrderDto>> UpdateOrderStageAsync(Guid workspaceId, Guid orderId, OrderStageCommand command, string dimension)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var result = await _commandService.UpdateOrderStageAsync(workspaceId, orderId, command, dimension);
        return Ok(result.Value);
    }

    [HttpPost("products")]
    public async Task<ActionResult<CloudProductDto>> CreateProductAsync(Guid workspaceId, [FromBody] CreateProductCommand command)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var result = await _commandService.CreateProductAsync(workspaceId, command);
        return Ok(result.Value);
    }

    [HttpPut("products/{productId:guid}")]
    public async Task<ActionResult<CloudProductDto>> UpdateProductAsync(Guid workspaceId, Guid productId, [FromBody] UpdateProductCommand command)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        command.ProductId = productId;
        var result = await _commandService.UpdateProductAsync(workspaceId, productId, command);
        return Ok(result.Value);
    }

    [HttpPost("inventory/items")]
    public async Task<ActionResult<CloudInventoryItemDto>> CreateInventoryItemAsync(Guid workspaceId, [FromBody] CreateInventoryItemCommand command)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var result = await _commandService.CreateInventoryItemAsync(workspaceId, command);
        return Ok(result.Value);
    }

    [HttpPut("inventory/items/{itemId:guid}")]
    public async Task<ActionResult<CloudInventoryItemDto>> UpdateInventoryItemAsync(Guid workspaceId, Guid itemId, [FromBody] UpdateInventoryItemCommand command)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        command.InventoryItemId = itemId;
        var result = await _commandService.UpdateInventoryItemAsync(workspaceId, itemId, command);
        return Ok(result.Value);
    }

    [HttpPost("inventory/movements")]
    public async Task<ActionResult<CloudInventoryMovementDto>> RecordMovementAsync(Guid workspaceId, [FromBody] InventoryMovementCommand command)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var result = await _commandService.RecordInventoryMovementAsync(workspaceId, command);
        return Ok(result.Value);
    }

    [HttpPost("inventory/stocktake-adjustments")]
    public async Task<ActionResult<CloudInventoryMovementDto>> StocktakeAdjustmentAsync(Guid workspaceId, [FromBody] InventoryMovementCommand command)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        command.IsStocktake = true;
        var result = await _commandService.RecordInventoryMovementAsync(workspaceId, command);
        return Ok(result.Value);
    }

    [HttpPost("customers")]
    public async Task<ActionResult<CloudCustomerDto>> CreateCustomerAsync(Guid workspaceId, [FromBody] CreateCustomerCommand command)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var result = await _commandService.CreateCustomerAsync(workspaceId, command);
        return Ok(result.Value);
    }

    [HttpPut("customers/{customerId:guid}")]
    public async Task<ActionResult<CloudCustomerDto>> UpdateCustomerAsync(Guid workspaceId, Guid customerId, [FromBody] UpdateCustomerCommand command)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        command.CustomerId = customerId;
        var result = await _commandService.UpdateCustomerAsync(workspaceId, customerId, command);
        return Ok(result.Value);
    }

    [HttpPost("customers/{customerId:guid}/notes")]
    public async Task<ActionResult<CloudCustomerDto>> AddCustomerNoteAsync(Guid workspaceId, Guid customerId, [FromBody] CustomerNoteCommand command)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var result = await _commandService.AddCustomerNoteAsync(workspaceId, customerId, command);
        return Ok(result.Value);
    }

    [HttpPost("cashflow/income")]
    public Task<ActionResult<CloudCashFlowEntryDto>> RecordIncomeAsync(Guid workspaceId, [FromBody] CashFlowEntryCommand command)
        => RecordCashFlowAsync(workspaceId, command, "income");

    [HttpPost("cashflow/expense")]
    public Task<ActionResult<CloudCashFlowEntryDto>> RecordExpenseAsync(Guid workspaceId, [FromBody] CashFlowEntryCommand command)
        => RecordCashFlowAsync(workspaceId, command, "expense");

    [HttpPost("cashflow/receivable")]
    public Task<ActionResult<CloudCashFlowEntryDto>> RecordReceivableAsync(Guid workspaceId, [FromBody] CashFlowEntryCommand command)
        => RecordCashFlowAsync(workspaceId, command, "receivable");

    [HttpPost("cashflow/payable")]
    public Task<ActionResult<CloudCashFlowEntryDto>> RecordPayableAsync(Guid workspaceId, [FromBody] CashFlowEntryCommand command)
        => RecordCashFlowAsync(workspaceId, command, "payable");

    private async Task<ActionResult<CloudCashFlowEntryDto>> RecordCashFlowAsync(Guid workspaceId, CashFlowEntryCommand command, string kind)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var result = await _commandService.RecordCashFlowAsync(workspaceId, command, kind);
        return Ok(result.Value);
    }

    [HttpPut("cashflow/{entryId:guid}")]
    public async Task<ActionResult<CloudCashFlowEntryDto>> UpdateCashFlowEntryAsync(Guid workspaceId, Guid entryId, [FromBody] UpdateCashFlowEntryCommand command)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        command.CashFlowEntryId = entryId;
        var result = await _commandService.UpdateCashFlowEntryAsync(workspaceId, entryId, command);
        return Ok(result.Value);
    }

    [HttpPost("cashflow/{entryId:guid}/settle")]
    public async Task<ActionResult<CloudCashFlowEntryDto>> SettleAsync(Guid workspaceId, Guid entryId, [FromBody] SettleCashFlowCommand command)
    {
        if (!await EnsureWorkspaceAccessAsync(workspaceId)) return Forbid();
        var result = await _commandService.SettleCashFlowAsync(workspaceId, entryId, command);
        return Ok(result.Value);
    }
}
