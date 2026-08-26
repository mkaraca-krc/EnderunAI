namespace EnderunAI.Api.Controllers;

using EnderunAI.Api.Services.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// MESAJLAŞMA UÇLARI.
///
/// YETKİ ANAHTARI YOK — `[Authorize]` yeterli. Mesajlaşma "yetkisi
/// olan görür" işi değil: giriş yapmış herkes KENDİ konuşmasını
/// görür, kimse başkasınınkini göremez. Erişimi üyelik belirliyor ve
/// üyelik kapısı serviste her uçta geçiliyor.
///
/// Yeni bir `messaging.use` anahtarı açsaydım `RoleCatalog`
/// yansıması onu yalnız Admin ve Genel Müdür'e verirdi; kalan her
/// role elle eklemek gerekirdi ve biri unutulsaydı o rol sessizce
/// mesajlaşamazdı. Sessiz yetki kaybı, gürültülü hatadan kötüdür.
/// </summary>
[ApiController]
[Authorize]
[Route("api/mesajlar")]
public sealed class MesajlarController(IMesajlasmaService mesajlar) : ControllerBase
{
    /// <summary>Sayfa boyu tavanı — istemci daha fazlasını isteyemez.</summary>
    private const int EnFazlaLimit = 50;

    public sealed record MesajGonderIstegi(string Govde);

    public sealed record BirebirAcIstegi(Guid KarsiUserId);

    [HttpGet("konusmalar")]
    public Task<IActionResult> Konusmalar(
        [FromQuery] DateTime? imlecZaman,
        [FromQuery] Guid? imlecId,
        [FromQuery] int limit,
        CancellationToken cancellationToken) =>
        SarmalaAsync(async () => Ok(await mesajlar.KonusmalarimAsync(
            imlecZaman, imlecId, Limitle(limit), cancellationToken)));

    [HttpPost("konusmalar/birebir")]
    public Task<IActionResult> BirebirAc(
        BirebirAcIstegi istek, CancellationToken cancellationToken) =>
        SarmalaAsync(async () => Ok(
            await mesajlar.BirebirKonusmaAcAsync(istek.KarsiUserId, cancellationToken)));

    [HttpGet("konusmalar/{id:guid}/mesajlar")]
    public Task<IActionResult> Mesajlar(
        Guid id,
        [FromQuery] DateTime? imlecZaman,
        [FromQuery] Guid? imlecId,
        [FromQuery] int limit,
        CancellationToken cancellationToken) =>
        SarmalaAsync(async () => Ok(await mesajlar.MesajlarAsync(
            id, imlecZaman, imlecId, Limitle(limit), cancellationToken)));

    [HttpPost("konusmalar/{id:guid}/mesajlar")]
    public Task<IActionResult> Gonder(
        Guid id, MesajGonderIstegi istek, CancellationToken cancellationToken) =>
        SarmalaAsync(async () => Ok(
            await mesajlar.MesajGonderAsync(id, istek.Govde, cancellationToken)));

    [HttpPost("konusmalar/{id:guid}/okundu")]
    public Task<IActionResult> Okundu(Guid id, CancellationToken cancellationToken) =>
        SarmalaAsync(async () =>
        {
            await mesajlar.OkunduIsaretleAsync(id, cancellationToken);
            return Ok(new { message = "Okundu işaretlendi." });
        });

    /// <summary>Rozet ve sekme başlığı bunu çağırıyor.</summary>
    [HttpGet("okunmamis")]
    public Task<IActionResult> Okunmamis(CancellationToken cancellationToken) =>
        SarmalaAsync(async () => Ok(new
        {
            sayi = await mesajlar.ToplamOkunmamisAsync(cancellationToken)
        }));

    // `q` NULLABLE — bilerek.
    //
    // Zorunlu yapıldığında boş sorgu ASP.NET model doğrulamasına
    // takılıyor ve kullanıcı "The q field is required." görüyordu:
    // İngilizce, kuralı anlatmayan, bizim yazmadığımız bir mesaj.
    // Kuralın mesajını kural versin (MesajAramaKurali.Uyari).
    [HttpGet("ara")]
    public Task<IActionResult> Ara(
        [FromQuery] string? q,
        [FromQuery] DateTime? imlecZaman,
        [FromQuery] Guid? imlecId,
        [FromQuery] int limit,
        CancellationToken cancellationToken) =>
        SarmalaAsync(async () => Ok(await mesajlar.AraAsync(
            q ?? string.Empty, imlecZaman, imlecId, Limitle(limit), cancellationToken)));

    [HttpGet("kisiler")]
    public Task<IActionResult> Kisiler(
        [FromQuery] string? q, CancellationToken cancellationToken) =>
        SarmalaAsync(async () => Ok(
            await mesajlar.KisiAraAsync(q ?? string.Empty, cancellationToken)));

    /// <summary>
    /// Sayfa boyu istemciden geliyor ama TAVANI sunucu koyuyor.
    /// Sınırsız limit, tek istekle tüm mesaj geçmişini çekmenin yolu
    /// olurdu.
    /// </summary>
    private static int Limitle(int limit) =>
        limit <= 0 ? 30 : Math.Min(limit, EnFazlaLimit);

    /// <summary>
    /// İŞ KURALI HATASI 400, 500 DEĞİL.
    ///
    /// "Konuşma bulunamadı", "en az 3 harf", "kendinizle konuşma
    /// açamazsınız" — hepsi kullanıcının düzeltebileceği durumlar.
    /// 500 dönseydi kullanıcı Türkçe uyarı yerine genel hata görürdü
    /// ve ne yapacağını bilemezdi.
    /// </summary>
    private async Task<IActionResult> SarmalaAsync(Func<Task<IActionResult>> is_)
    {
        try
        {
            return await is_();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
