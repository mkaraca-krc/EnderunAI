using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Hakedis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Keşif–gerçekleşen takibi. Sözleşmede öngörülen metraj ile
/// hakedişlerden biriken gerçekleşen miktarı karşılaştırır ve sapmayı
/// sözleşme tipine göre yorumlar.
///
/// Tutar içerdiği için hakediş görüntüleme izniyle korunur.
/// </summary>
[ApiController]
[Authorize]
[Route("api")]
public sealed class ProgressTrackingController(
    IProgressTrackingService service) : ControllerBase
{
    [HttpGet("projects/{projectId:guid}/progress-tracking")]
    [RequirePermission(PermissionCatalog.Keys.HakedisView)]
    public async Task<IActionResult> Get(
        Guid projectId, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.BuildAsync(projectId, cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }

    /// <summary>
    /// Sapma uyarısı üreten projeler — dashboard bildirim merkezi için.
    /// Sözleşme tipi belirlenmemiş projeler değerlendirilmez.
    /// </summary>
    [HttpGet("progress-tracking/alerts")]
    [RequirePermission(PermissionCatalog.Keys.HakedisView)]
    public async Task<IActionResult> Alerts(CancellationToken cancellationToken) =>
        Ok(await service.GetAlertsAsync(null, cancellationToken));
}
