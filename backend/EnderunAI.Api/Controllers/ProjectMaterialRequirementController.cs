using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Purchasing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Proje malzeme ihtiyacı ve eksikten talep açma.
///
/// Talep OTOMATİK açılmaz: liste bir ÖNERİdir, kullanıcı seçer ve
/// talebi kendisi başlatır. İhtiyaç tüm proje süresi için hesaplanır,
/// satın alma ise zamanlıdır.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/material-requirement")]
[Authorize]
public sealed class ProjectMaterialRequirementController(
    IProjectMaterialRequirementService requirementService,
    IProjectMaterialRequestBridge bridge) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.PurchasingRequestsView)]
    public async Task<IActionResult> Get(
        Guid projectId,
        [FromQuery] bool includeCentralWarehouse,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await requirementService.GetAsync(
                projectId, includeCentralWarehouse, cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }

    /// <summary>
    /// Seçilen eksik satırlarından TASLAK satın alma talebi açar.
    ///
    /// İzin, oluşturduğu belgenin izni: talep oluşturma. (Mevcut
    /// teklif→talep ucu sipariş oluşturma izni istiyor; o ayrı bir
    /// tutarsızlık, burada tekrarlanmadı.)
    /// </summary>
    [HttpPost("create-request")]
    [RequirePermission(PermissionCatalog.Keys.PurchasingRequestsCreate)]
    public async Task<IActionResult> CreateRequest(
        Guid projectId,
        CreateMaterialRequestFromRequirementRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await bridge.CreateAsync(projectId, request, cancellationToken));
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
