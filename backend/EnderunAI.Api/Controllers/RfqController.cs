using EnderunAI.Api.Contracts.Rfq;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Procurement;
using EnderunAI.Api.Services.Rfq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/rfq")]
public sealed class RfqController(IRfqService service) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.PurchasingRfqView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] int? status,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.GetAllAsync(companyId, status, cancellationToken));

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PurchasingRfqView)]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.GetByIdAsync(id, cancellationToken));

    [HttpPost("create-from-purchase-request/{purchaseRequestId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PurchasingRfqCreate)]
    public async Task<IActionResult> CreateFromPurchaseRequest(
        Guid purchaseRequestId,
        CreateRfqRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.CreateFromPurchaseRequestAsync(
            purchaseRequestId,
            request,
            cancellationToken));

    [HttpPost("{id:guid}/send")]
    [RequirePermission(PermissionCatalog.Keys.PurchasingRfqEdit)]
    public async Task<IActionResult> Send(
        Guid id,
        CancellationToken cancellationToken) =>
        await ExecuteMessageAsync(
            () => service.SendAsync(id, cancellationToken),
            "RFQ tedarikçilere gönderildi.");

    [HttpPost("{rfqId:guid}/suppliers/{rfqSupplierId:guid}/quotation")]
    [RequirePermission(PermissionCatalog.Keys.PurchasingRfqEdit)]
    public async Task<IActionResult> SaveQuotation(
        Guid rfqId,
        Guid rfqSupplierId,
        SaveQuotationRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteMessageAsync(
            () => service.SaveQuotationAsync(
                rfqId,
                rfqSupplierId,
                request,
                cancellationToken),
            "Tedarikçi teklifi kaydedildi.");

    [HttpGet("{id:guid}/comparison")]
    [RequirePermission(PermissionCatalog.Keys.PurchasingRfqView)]
    public async Task<IActionResult> GetComparison(
        Guid id,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.GetComparisonAsync(id, cancellationToken));

    [HttpPost("{id:guid}/award/{rfqSupplierId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PurchasingRfqApprove)]
    public async Task<IActionResult> Award(
        Guid id,
        Guid rfqSupplierId,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => service.AwardAsync(id, rfqSupplierId, cancellationToken));

    [HttpPost("{id:guid}/close")]
    [RequirePermission(PermissionCatalog.Keys.PurchasingRfqEdit)]
    public async Task<IActionResult> Close(
        Guid id,
        CancellationToken cancellationToken) =>
        await ExecuteMessageAsync(
            () => service.CloseAsync(id, cancellationToken),
            "RFQ kapatıldı.");

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

    private async Task<IActionResult> ExecuteMessageAsync(
        Func<Task> action,
        string message)
    {
        try
        {
            await action();
            return Ok(new { message });
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
