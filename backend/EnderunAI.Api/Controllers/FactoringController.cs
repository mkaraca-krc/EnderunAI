using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Accounting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Çek kırdırma (faktoring). Komisyon, BSMV ve masraf ayrı kesintiler
/// olarak hesaplanır; net tutar banka hesabına girer, kesintiler
/// finansman gideri olarak projeye ve muhasebeye işlenir.
/// </summary>
[ApiController]
[Authorize]
[Route("api/factoring")]
public sealed class FactoringController(IFactoringService service) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        CancellationToken cancellationToken)
    {
        return Ok(await service.GetAllAsync(
            companyId, projectId, startDate, endDate, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
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

    /// <summary>Kaydetmeden önce kesinti ve net tutar önizlemesi.</summary>
    [HttpPost("preview")]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public IActionResult Preview(FactoringPreviewRequest request)
    {
        try
        {
            return Ok(service.Preview(request));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.FinanceCreate)]
    public async Task<IActionResult> Create(
        CreateFactoringTransactionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.CreateAsync(request, cancellationToken));
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
