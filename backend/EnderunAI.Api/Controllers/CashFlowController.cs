using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Accounting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Vade bazlı nakit akışı: önümüzdeki 30/60/90 günde beklenen
/// tahsilatlar ve ödemeler, mevcut kasa/banka bakiyesiyle birlikte.
/// </summary>
[ApiController]
[Authorize]
[Route("api/cash-flow")]
public sealed class CashFlowController(ICashFlowService service) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public async Task<IActionResult> Get(
        [FromQuery] Guid companyId,
        [FromQuery] Guid? projectId,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
            return BadRequest(new { message = "Şirket seçimi zorunludur." });

        return Ok(await service.GetAsync(companyId, projectId, cancellationToken));
    }
}
