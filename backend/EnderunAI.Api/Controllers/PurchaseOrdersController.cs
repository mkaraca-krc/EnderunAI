using EnderunAI.Api.Contracts.PurchaseOrders;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Procurement;
using EnderunAI.Api.Services.PurchaseOrders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/purchase-orders")]
public sealed class PurchaseOrdersController(IPurchaseOrderService service) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.PurchasingView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] int? status,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.GetAllAsync(
            companyId,
            projectId,
            status,
            cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.GetByIdAsync(id, cancellationToken));

    [HttpPost("create-from-rfq/{rfqId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PurchasingManage)]
    public async Task<IActionResult> CreateFromRfq(
        Guid rfqId,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.CreateFromRfqAsync(rfqId, cancellationToken));

    [HttpPost("{id:guid}/submit")]
    [RequirePermission(PermissionCatalog.Keys.PurchasingManage)]
    public async Task<IActionResult> Submit(
        Guid id,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.SubmitAsync(id, cancellationToken));

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(
        Guid id,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.ApproveAsync(id, cancellationToken));

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(
        Guid id,
        PurchaseOrderReasonRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.RejectAsync(id, request.Reason, cancellationToken));

    [HttpPost("{id:guid}/cancel")]
    [RequirePermission(PermissionCatalog.Keys.PurchasingManage)]
    public async Task<IActionResult> Cancel(
        Guid id,
        PurchaseOrderReasonRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.CancelAsync(id, request.Reason, cancellationToken));

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
