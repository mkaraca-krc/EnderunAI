using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Tax;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Vergi yükü YÖNETİM GÖRÜNÜMÜ.
///
/// Bu uçlar beyanname üretmez; müşavirin beyanıyla mutabakat için
/// defterdeki rakamı ve ileriye dönük tahmini verir. Tahminler yanıtta
/// varsayımlarıyla birlikte döner ki ekran onları gizlemeden gösterebilsin.
/// </summary>
[ApiController]
[Authorize]
[Route("api/tax")]
public sealed class TaxController(ITaxLedgerService taxLedger) : ControllerBase
{
    [HttpGet("overview")]
    [RequirePermission(PermissionCatalog.Keys.AccountingView)]
    public async Task<IActionResult> GetOverview(
        [FromQuery] Guid companyId,
        [FromQuery] int? year,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await taxLedger.GetOverviewAsync(
                companyId, year ?? DateTime.UtcNow.Year, cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }
}
