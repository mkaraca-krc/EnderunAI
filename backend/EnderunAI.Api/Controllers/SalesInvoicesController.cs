using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Accounting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Hakediş dışı satış faturaları (malzeme/hizmet satışı). Kesinleşince
/// 120/600/391 gelir fişi üretir. Hakediş faturalarıyla karışmaz.
/// </summary>
[ApiController]
[Authorize]
[Route("api/sales-invoices")]
public sealed class SalesInvoicesController(ISalesInvoiceService service) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.AccountingView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] int? status,
        [FromQuery] Guid? projectId,
        [FromQuery] Guid? customerId,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        return Ok(await service.GetAllAsync(
            companyId, status, projectId, customerId, search, cancellationToken));
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
        CreateSalesInvoiceRequest request, CancellationToken cancellationToken)
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
        Guid id, UpdateSalesInvoiceRequest request, CancellationToken cancellationToken)
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
    /// Müşteriden mal iadesi. Orijinal faturaya bağlı taslak iade
    /// faturası üretir; kesinleştiğinde 610 Satıştan İadeler fişi kesilir.
    /// </summary>
    [HttpPost("{id:guid}/returns")]
    [RequirePermission(PermissionCatalog.Keys.AccountingCreate)]
    public async Task<IActionResult> CreateReturn(
        Guid id, CreateInvoiceReturnRequest request, CancellationToken cancellationToken)
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

    [HttpPost("{id:guid}/post")]
    [RequirePermission(PermissionCatalog.Keys.AccountingEdit)]
    public async Task<IActionResult> Post(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.PostAsync(id, cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpPost("{id:guid}/cancel")]
    [RequirePermission(PermissionCatalog.Keys.AccountingEdit)]
    public async Task<IActionResult> Cancel(
        Guid id, CancelSalesInvoiceRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.CancelAsync(id, request.Reason, cancellationToken));
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
