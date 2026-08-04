using EnderunAI.Api.Contracts.Isg;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Isg;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Kaza ve ramak kala kayıt defteri — yasal zorunluluk.
///
/// isg.view'dan AYRI bir izinle korunur: sahada İSG kaydı girebilen
/// herkesin tüm kaza geçmişini görmesi gerekmiyor.
/// </summary>
[ApiController]
[Authorize]
[Route("api/isg/kazalar")]
public sealed class IsgIncidentsController(
    IIsgIncidentService service) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.IsgIncidentView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] int? status,
        [FromQuery] int? incidentType,
        CancellationToken cancellationToken)
    {
        return Ok(await service.GetAllAsync(
            companyId, projectId, status, incidentType, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.IsgIncidentView)]
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
    [RequirePermission(PermissionCatalog.Keys.IsgIncidentManage)]
    public async Task<IActionResult> Create(
        CreateIsgIncidentRequest request, CancellationToken cancellationToken)
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
    [RequirePermission(PermissionCatalog.Keys.IsgIncidentManage)]
    public async Task<IActionResult> Update(
        Guid id, UpdateIsgIncidentRequest request, CancellationToken cancellationToken)
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
    [RequirePermission(PermissionCatalog.Keys.IsgIncidentManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await service.DeleteAsync(id, cancellationToken);
            return Ok(new { message = "Kaza kaydı silindi." });
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }
}
