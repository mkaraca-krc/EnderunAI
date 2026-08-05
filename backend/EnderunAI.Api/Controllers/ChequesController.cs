using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Accounting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Çift yönlü çek defteri: alınan çekler (işverenden) ve verilen
/// çekler (tedarikçiye). Her durum geçişi hareket geçmişine yazılır ve
/// muhasebe etkisi olan geçişler dengeli fiş üretir.
/// </summary>
[ApiController]
[Authorize]
[Route("api/cheques")]
public sealed class ChequesController(IChequeService service) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] int? direction,
        [FromQuery] int? status,
        [FromQuery] Guid? currentAccountId,
        [FromQuery] Guid? projectId,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        return Ok(await service.GetAllAsync(
            companyId, direction, status, currentAccountId, projectId, search,
            cancellationToken));
    }

    [HttpGet("summary")]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public async Task<IActionResult> GetSummary(
        [FromQuery] Guid? companyId, CancellationToken cancellationToken)
    {
        return Ok(await service.GetSummaryAsync(companyId, cancellationToken));
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

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.FinanceCreate)]
    public async Task<IActionResult> Create(
        CreateChequeRequest request, CancellationToken cancellationToken)
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

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.FinanceEdit)]
    public async Task<IActionResult> Update(
        Guid id, UpdateChequeRequest request, CancellationToken cancellationToken)
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
    /// Çekin proje/masraf merkezi dağılımını baştan yazar. Boş liste
    /// dağılımı kaldırır ve çek tek parça işlenmeye döner.
    /// </summary>
    [HttpPut("{id:guid}/allocations")]
    [RequirePermission(PermissionCatalog.Keys.FinanceEdit)]
    public async Task<IActionResult> ReplaceAllocations(
        Guid id, ChequeAllocationsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.ReplaceAllocationsAsync(id, request, cancellationToken));
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
    /// Çek erteleme/değişim. Eski çek "Ertelendi" olur, yerine aynı
    /// tutarda yeni vadeli çek açılır ve zincire bağlanır.
    /// </summary>
    [HttpPost("{id:guid}/replace")]
    [RequirePermission(PermissionCatalog.Keys.FinanceEdit)]
    public async Task<IActionResult> Replace(
        Guid id, ReplaceChequeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.ReplaceAsync(id, request, cancellationToken));
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

    [HttpPost("{id:guid}/status")]
    [RequirePermission(PermissionCatalog.Keys.FinanceEdit)]
    public async Task<IActionResult> ChangeStatus(
        Guid id, ChequeStatusChangeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.ChangeStatusAsync(id, request, cancellationToken));
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
