using EnderunAI.Api.Contracts.Isg;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Isg;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// OSGB hizmet sözleşmeleri: şartlar, atanan iş güvenliği uzmanı ve
/// işyeri hekimi, OSGB'nin kestiği faturalar ve hakedişe önerilecek
/// İSG kesintisi.
/// </summary>
[ApiController]
[Authorize]
[Route("api/isg/osgb-sozlesmeleri")]
public sealed class IsgOsgbContractsController(
    IIsgOsgbContractService service) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.IsgView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId, CancellationToken cancellationToken)
    {
        return Ok(await service.GetAllAsync(companyId, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.IsgView)]
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
    [RequirePermission(PermissionCatalog.Keys.IsgCreate)]
    public async Task<IActionResult> Create(
        CreateIsgOsgbContractRequest request, CancellationToken cancellationToken)
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
    [RequirePermission(PermissionCatalog.Keys.IsgEdit)]
    public async Task<IActionResult> Update(
        Guid id, UpdateIsgOsgbContractRequest request, CancellationToken cancellationToken)
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
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.IsgDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await service.DeleteAsync(id, cancellationToken);
            return Ok(new { message = "OSGB sözleşmesi silindi." });
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }

    /// <summary>
    /// Hakediş ekranının çağırdığı öneri ucu. Kayıt yazmaz; sözleşme
    /// yoksa veya tutar hesaplanamıyorsa öneri üretmez, sebebini döner.
    /// </summary>
    [HttpGet("/api/isg/hakedis-kesinti-onerisi")]
    // İki izin: herhangi biri yeterli (middleware OR uygular). Kesintiyi
    // hakedişi hazırlayan giriyor; İSG yetkisi şart koşulsaydı hakediş
    // sorumlusu kendi ekranındaki öneriyi alamazdı.
    [RequirePermission(PermissionCatalog.Keys.IsgView)]
    [RequirePermission(PermissionCatalog.Keys.HakedisView)]
    public async Task<IActionResult> SuggestDeduction(
        [FromQuery] Guid companyId,
        [FromQuery] Guid projectId,
        [FromQuery] DateOnly? donem,
        CancellationToken cancellationToken)
    {
        var periodDate = donem ?? DateOnly.FromDateTime(DateTime.UtcNow);

        return Ok(await service.SuggestDeductionAsync(
            companyId, projectId, periodDate, cancellationToken));
    }
}
