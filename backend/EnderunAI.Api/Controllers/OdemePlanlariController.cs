using EnderunAI.Api.Models.Finance;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Services.Finance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// HAFTALIK ÖDEME PLANI (ÖP/1a).
///
/// İKİ AYRI KAPI: hazırlama ve onaylama farklı anahtarlar. Onay
/// anahtarı YALNIZ Genel Müdür'de — Admin dahil hiçbir role otomatik
/// verilmiyor (İ2, testle sabit).
///
/// K4 KAPIDA DEĞİL KODDA: "hazırlayan kendi satırını onaylayamaz"
/// kuralı izin sistemiyle çözülemez, çünkü GM hem hazırlayabilir hem
/// onaylayabilir — engellenen şey AYNI SATIRDA ikisini birden
/// yapmak. Servis içinde kişi kimliğine bakılarak zorlanıyor.
/// </summary>
[ApiController]
[Authorize]
[Route("api/odeme-planlari")]
public sealed class OdemePlanlariController(
    OdemePlaniService service,
    ICurrentUserService currentUser) : ControllerBase
{
    public sealed record TaslakOlusturIstegi(Guid CompanyId, DateTime Hafta);

    public sealed record KararIstegi(
        int Karar, decimal? OnaylananTutar, DateTime? CekVadesi, int? Oncelik);

    public sealed record OdemeIstegi(decimal OdenenTutar);

    public sealed record BakiyeIstegi(
        Guid CashAccountId, decimal? ElleGirilenTutar);

    public sealed record SatirIstegi(
        Guid CurrentAccountId, decimal Tutar, int Yontem, DateTime? CekVadesi,
        int Oncelik, Guid? CashAccountId, string? Aciklama);

    /// <summary>E1 — plan listesi.</summary>
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.PaymentPlanPrepare)]
    public async Task<IActionResult> Listele(
        [FromQuery] Guid companyId, CancellationToken cancellationToken)
        => Ok(await service.PlanlariListeleAsync(companyId, cancellationToken));

    /// <summary>
    /// E2/E3 — plan detayı: satırlar, bütçe, geçen haftanın plan dışı
    /// ödemeleri ve K2 durumu.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PaymentPlanPrepare)]
    public async Task<IActionResult> Detay(Guid id, CancellationToken cancellationToken)
    {
        try { return Ok(await service.PlanDetayiAsync(id, cancellationToken)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    /// <summary>E2 — satır ekle.</summary>
    [HttpPost("{id:guid}/satirlar")]
    [RequirePermission(PermissionCatalog.Keys.PaymentPlanPrepare)]
    public async Task<IActionResult> SatirEkle(
        Guid id, [FromBody] SatirIstegi istek, CancellationToken cancellationToken)
    {
        try
        {
            var satirId = await service.SatirEkleAsync(
                id, istek.CurrentAccountId, istek.Tutar,
                (OdemeYontemi)istek.Yontem, istek.CekVadesi, istek.Oncelik,
                istek.CashAccountId, istek.Aciklama,
                currentUser.UserId, cancellationToken);

            return Ok(new { id = satirId });
        }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    /// <summary>E2 — satır güncelle.</summary>
    [HttpPut("satirlar/{satirId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PaymentPlanPrepare)]
    public async Task<IActionResult> SatirGuncelle(
        Guid satirId, [FromBody] SatirIstegi istek, CancellationToken cancellationToken)
    {
        try
        {
            await service.SatirGuncelleAsync(
                satirId, istek.Tutar, (OdemeYontemi)istek.Yontem, istek.CekVadesi,
                istek.Oncelik, istek.CashAccountId, istek.Aciklama,
                currentUser.UserId, cancellationToken);

            return NoContent();
        }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    /// <summary>E2 — satır sil (yumuşak).</summary>
    [HttpDelete("satirlar/{satirId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PaymentPlanPrepare)]
    public async Task<IActionResult> SatirSil(
        Guid satirId, CancellationToken cancellationToken)
    {
        try
        {
            await service.SatirSilAsync(satirId, currentUser.UserId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    /// <summary>D1 — haftanın taslağı.</summary>
    [HttpPost("taslak")]
    [RequirePermission(PermissionCatalog.Keys.PaymentPlanPrepare)]
    public async Task<IActionResult> TaslakOlustur(
        [FromBody] TaslakOlusturIstegi istek, CancellationToken cancellationToken)
    {
        var plan = await service.HaftalikTaslakOlusturAsync(
            istek.CompanyId, istek.Hafta, currentUser.UserId, cancellationToken);

        return Ok(new { plan.Id, plan.HaftaBaslangici, plan.OdemeGunu, plan.Durum });
    }

    /// <summary>D2 — onaya sunma.</summary>
    [HttpPost("{id:guid}/onaya-sun")]
    [RequirePermission(PermissionCatalog.Keys.PaymentPlanPrepare)]
    public async Task<IActionResult> OnayaSun(
        Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await service.OnayaSunAsync(id, currentUser.UserId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    /// <summary>
    /// B1/B2 — bakiyeyi plana yazar.
    ///
    /// TUTAR VERİLİRSE ELLE GİRİLMİŞ sayılır, verilmezse hareketlerden
    /// HESAPLANIR. Her iki hâlde de plan GÖSTERİLENİ saklar ve kimin
    /// yazdığı kayda geçer.
    ///
    /// AÇIK İSTEK ÜZERİNE: ekran her açılışta bütün hareketleri
    /// taramaz, sakladığı değeri gösterir.
    /// </summary>
    [HttpPost("{id:guid}/bakiye")]
    [RequirePermission(PermissionCatalog.Keys.PaymentPlanPrepare)]
    public async Task<IActionResult> BakiyeYaz(
        Guid id, [FromBody] BakiyeIstegi istek, CancellationToken cancellationToken)
    {
        try
        {
            var tutar = istek.ElleGirilenTutar
                ?? await service.BakiyeHesaplaAsync(istek.CashAccountId, cancellationToken);

            var kaynak = istek.ElleGirilenTutar is null
                ? BakiyeKaynagi.Hesaplandi
                : BakiyeKaynagi.ElleGirildi;

            await service.BakiyeYazAsync(
                id, istek.CashAccountId, tutar, kaynak,
                currentUser.UserId, cancellationToken);

            return Ok(new { tutar, kaynak });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    /// <summary>
    /// D3/K1 — SATIR SATIR KARAR. YALNIZ GENEL MÜDÜR.
    /// </summary>
    [HttpPost("satirlar/{satirId:guid}/karar")]
    [RequirePermission(PermissionCatalog.Keys.PaymentPlanApprove)]
    public async Task<IActionResult> SatirKarar(
        Guid satirId, [FromBody] KararIstegi istek, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } onaylayan)
            return Unauthorized();

        try
        {
            await service.SatirKararVerAsync(
                satirId, (OdemeSatirKarari)istek.Karar, istek.OnaylananTutar,
                istek.CekVadesi, istek.Oncelik, onaylayan, cancellationToken);

            return NoContent();
        }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    /// <summary>
    /// D4/K11 — ÖDEME KAYDI. Sistem kendi başına ödeme yapmaz;
    /// bu uç bir insanın kararıyla çağrılır.
    /// </summary>
    [HttpPost("satirlar/{satirId:guid}/odeme")]
    [RequirePermission(PermissionCatalog.Keys.PaymentPlanPrepare)]
    public async Task<IActionResult> SatirOdeme(
        Guid satirId, [FromBody] OdemeIstegi istek, CancellationToken cancellationToken)
    {
        try
        {
            await service.SatirOdemeKaydetAsync(
                satirId, istek.OdenenTutar, currentUser.UserId, cancellationToken);

            return NoContent();
        }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    /// <summary>K6/K9 — iki ayrı bütçe sayısı ve yetmezlik uyarısı.</summary>
    [HttpGet("{id:guid}/butce")]
    [RequirePermission(PermissionCatalog.Keys.PaymentPlanPrepare)]
    public async Task<IActionResult> Butce(Guid id, CancellationToken cancellationToken)
    {
        try { return Ok(await service.ButceOzetiAsync(id, cancellationToken)); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    /// <summary>D5/K10 — kapanış; sebepsiz satır varken kapanmaz.</summary>
    [HttpPost("{id:guid}/kapat")]
    [RequirePermission(PermissionCatalog.Keys.PaymentPlanPrepare)]
    public async Task<IActionResult> Kapat(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await service.KapatAsync(id, currentUser.UserId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }
}
