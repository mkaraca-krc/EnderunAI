using EnderunAI.Api.Services.Management;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Yönetim KPI'ları.
///
/// UÇTA GENİŞ BİR YETKİ YOK ve bu bilinçli: her KPI kendi anahtarına
/// bağlı ve yetkisi olmayan KPI yanıta hiç girmiyor. Uca tek bir üst
/// anahtar konsaydı, nakit görmeyen bir yönetici hiçbir gösterge
/// göremezdi; oysa satın alma ya da bordro göstergelerini görmesinde
/// sakınca yok. Kapı KPI başına, sayfa başına değil.
/// </summary>
[ApiController]
[Authorize]
[Route("api/yonetim/kpi")]
public sealed class ManagementKpiController(ManagementKpiService service)
    : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] Guid companyId,
        [FromQuery] int? year,
        [FromQuery] int? month,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
            return BadRequest(new { message = "Şirket seçimi zorunludur." });

        var now = DateTime.UtcNow;
        var resolvedYear = year ?? now.Year;
        var resolvedMonth = month ?? now.Month;

        if (resolvedMonth is < 1 or > 12)
            return BadRequest(new { message = "Geçersiz ay." });

        return Ok(await service.GetAsync(
            companyId, resolvedYear, resolvedMonth, cancellationToken));
    }
}
