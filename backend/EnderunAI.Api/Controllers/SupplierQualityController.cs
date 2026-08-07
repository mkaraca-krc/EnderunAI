using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Purchasing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Tedarikçi kalite karnesi: hangi tedarikçiden gelen mal sık sık
/// reddediliyor ya da hasarlı geliyor.
///
/// Kaynak yalnızca KESİNLEŞMİŞ mal kabullerdir; taslak mal kabul henüz
/// bir teslimat değil ve düzeltilmiş bir hatayı tedarikçinin
/// karnesine yazmak haksızlık olurdu.
/// </summary>
[ApiController]
[Authorize]
[Route("api/purchasing/supplier-quality")]
public sealed class SupplierQualityController(
    SupplierQualityService service) : ControllerBase
{
    /// <summary>
    /// Tedarikçi bazında red/hasar oranı ve geciken sipariş sayısı.
    /// </summary>
    /// <param name="companyId">Şirket filtresi; boşsa tümü.</param>
    /// <param name="months">Kaç aylık geçmiş; varsayılan 12.</param>
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.PurchasingView)]
    public async Task<IActionResult> Get(
        [FromQuery] Guid? companyId,
        [FromQuery] int? months,
        CancellationToken cancellationToken) =>
        Ok(await service.GetReportAsync(companyId, months, cancellationToken));
}
