using EnderunAI.Api.Contracts.GoodsReceipts;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.GoodsReceipts;
using EnderunAI.Api.Services.Procurement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/goods-receipts")]
public sealed class GoodsReceiptsController(IGoodsReceiptService service) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.PurchasingReceiptsView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? warehouseId,
        [FromQuery] Guid? purchaseOrderId,
        [FromQuery] int? status,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.GetAllAsync(
            companyId,
            warehouseId,
            purchaseOrderId,
            status,
            cancellationToken));

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PurchasingReceiptsView)]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.GetByIdAsync(id, cancellationToken));

    [HttpPost("create-from-purchase-order/{purchaseOrderId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PurchasingReceiptsCreate)]
    public async Task<IActionResult> CreateFromPurchaseOrder(
        Guid purchaseOrderId,
        CreateGoodsReceiptRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.CreateFromPurchaseOrderAsync(
            purchaseOrderId,
            request,
            cancellationToken));

    [HttpGet("{id:guid}/inventory-options")]
    [RequirePermission(PermissionCatalog.Keys.PurchasingReceiptsView)]
    public async Task<IActionResult> GetInventoryOptions(
        Guid id,
        [FromQuery] string? search,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.GetInventoryOptionsAsync(
            id,
            search,
            cancellationToken));

    [HttpPut("{id:guid}/draft")]
    [RequirePermission(PermissionCatalog.Keys.PurchasingReceiptsEdit)]
    public async Task<IActionResult> UpdateDraft(
        Guid id,
        UpdateGoodsReceiptDraftRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.UpdateDraftAsync(
            id,
            request,
            cancellationToken));

    [HttpPost("{id:guid}/post")]
    [RequirePermission(PermissionCatalog.Keys.PurchasingReceiptsApprove)]
    public async Task<IActionResult> Post(
        Guid id,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.PostAsync(id, cancellationToken));

    [HttpPost("{id:guid}/cancel")]
    [RequirePermission(PermissionCatalog.Keys.PurchasingReceiptsEdit)]
    public async Task<IActionResult> Cancel(
        Guid id,
        GoodsReceiptReasonRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.CancelAsync(
            id,
            request.Reason,
            cancellationToken));

    private async Task<IActionResult> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (ProcurementNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (ProcurementValidationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}

