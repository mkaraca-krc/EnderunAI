using EnderunAI.Api.Contracts.Procurement;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Procurement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Satın alma dashboard'u.
///
/// Hesap <see cref="ProcurementDashboardService"/> içinde: yönetim
/// KPI'ları da aynı servisten okuyor. Sorgu burada dursaydı KPI onu
/// ikinci kez yazmak zorunda kalır, iki sayı zamanla ayrışırdı.
/// </summary>
[ApiController]
[Authorize]
[Route("api/procurement/dashboard")]
public sealed class ProcurementDashboardController(
    ProcurementDashboardService service,
    ICurrentDataScopeService dataScope) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.PurchasingView)]
    public async Task<ActionResult<ProcurementDashboardResponse>> Get(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        CancellationToken cancellationToken)
    {
        var scope = await dataScope.GetAsync(cancellationToken);
        if (scope is null)
            return Unauthorized();

        return Ok(await service.GetAsync(
            companyId, projectId, scope, cancellationToken));
    }
}
