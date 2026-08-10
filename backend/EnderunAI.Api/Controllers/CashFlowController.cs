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
public sealed class CashFlowController(
    ICashFlowService service,
    ICashFlowProjectionService projection) : ControllerBase
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

    /// <summary>
    /// Likidite takvimi: TARİH BAZLI yürüyen bakiye, finansman açığı
    /// ve aylık özet.
    ///
    /// AYRI VE DAR İZİN (cashflow.view): tablo bordro çıkışını ELDEN
    /// DAHİL tam tutarla taşıyor. finance.view'e bırakılsaydı Teknik
    /// Ofis ve Teknik Koordinatör de görürdü — ikisinde de ek ödeme
    /// yetkisi yok ve elden toplamı buradan sızardı. Kapı dar
    /// tutulduğu için tablo İÇERİDE tek ve eksiksiz: kalem gizleme
    /// yok, iki kullanıcı aynı bakiyeyi okuyor.
    /// </summary>
    [HttpGet("projeksiyon")]
    [RequirePermission(PermissionCatalog.Keys.CashFlowView)]
    public async Task<IActionResult> GetProjection(
        [FromQuery] Guid companyId,
        [FromQuery] int? months,
        [FromQuery] DateTime? targetDate,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
            return BadRequest(new { message = "Şirket seçimi zorunludur." });

        return Ok(await projection.GetAsync(
            companyId, months ?? 6, targetDate, cancellationToken));
    }
}
