using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Accounting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Tedarikçi (alış) faturaları: kalem bazlı KDV'li fatura girişi,
/// 3 yönlü kontrol (sipariş = mal kabul = fatura) ve onayda otomatik
/// muhasebe fişi üretimi.
/// </summary>
[ApiController]
[Authorize]
[Route("api/supplier-invoices")]
public sealed class SupplierInvoicesController(
    ISupplierInvoiceService service) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.AccountingView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] int? status,
        [FromQuery] Guid? projectId,
        [FromQuery] Guid? supplierId,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        return Ok(await service.GetAllAsync(
            companyId, status, projectId, supplierId, search, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.AccountingView)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.GetByIdAsync(id, cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.AccountingCreate)]
    public async Task<IActionResult> Create(
        CreateSupplierInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.CreateAsync(request, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.AccountingEdit)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateSupplierInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.UpdateAsync(id, request, cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    /// <summary>
    /// Tedarikçiye mal iadesi. Orijinal faturaya bağlı taslak iade
    /// faturası üretir; onaylandığında ters fiş kesilir ve stok çıkar.
    /// </summary>
    [HttpPost("{id:guid}/returns")]
    [RequirePermission(PermissionCatalog.Keys.AccountingCreate)]
    public async Task<IActionResult> CreateReturn(
        Guid id,
        CreateInvoiceReturnRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.CreateReturnAsync(id, request, cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpPost("{id:guid}/submit")]
    [RequirePermission(PermissionCatalog.Keys.AccountingEdit)]
    public Task<IActionResult> Submit(Guid id, CancellationToken cancellationToken) =>
        RunAction(() => service.SubmitAsync(id, cancellationToken));

    [HttpPost("{id:guid}/approve")]
    [RequirePermission(PermissionCatalog.Keys.AccountingApprove)]
    public Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken) =>
        RunAction(() => service.ApproveAsync(id, cancellationToken));

    [HttpPost("{id:guid}/reject")]
    [RequirePermission(PermissionCatalog.Keys.AccountingApprove)]
    public Task<IActionResult> Reject(
        Guid id,
        RejectSupplierInvoiceRequest request,
        CancellationToken cancellationToken) =>
        RunAction(() => service.RejectAsync(id, request.Reason, cancellationToken));

    /// <summary>
    /// Fatura iptali. Onaylanmış faturada gerekçe zorunlu; ters fiş
    /// kesilir, stok girmişse geri çıkar.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [RequirePermission(PermissionCatalog.Keys.AccountingDelete)]
    public Task<IActionResult> Cancel(
        Guid id,
        CancelInvoiceRequest? request,
        CancellationToken cancellationToken) =>
        RunAction(() => service.CancelAsync(id, request?.Reason, cancellationToken));

    private async Task<IActionResult> RunAction(
        Func<Task<SupplierInvoiceActionResponse>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }
}
