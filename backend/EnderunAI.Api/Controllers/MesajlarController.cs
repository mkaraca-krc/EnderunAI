namespace EnderunAI.Api.Controllers;

using EnderunAI.Api.Services.Messaging;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// MESAJLAŞMA UÇLARI.
///
/// HER UÇ BEYAN TAŞIR (2026-09-04): `mesajlar.view` okuyan uçlarda,
/// `mesajlar.send` yazan uçlarda.
///
/// ── ÖNCEKİ KARAR VE NEDEN DEĞİŞTİ ──
///
/// Burada "yetki anahtarı yok, `[Authorize]` yeterli" yazıyordu.
/// Gerekçesi doğruydu ve DURUYOR: erişimi ÜYELİK belirler, mesajlaşma
/// "yetkisi olan görür" işi değil. Değişen şey gerekçe değil,
/// BEYANIN kendisi: bir ucun neye izin verdiği o ucun üstünde yazılı
/// olmalı, çünkü "izin gerekmiyor" ile "izin yazılmamış" dışarıdan
/// AYNI görünür (KURAL 72/E).
///
/// Eski yorumun korkusu ölçüldü ve YANLIŞ ÇIKTI: "yeni anahtar
/// yalnız Admin ve GM'ye gider, kalan roller sessizce mesajlaşamaz"
/// deniyordu. `DatabaseSeeder.SeedRolePermissionsAsync` HER AÇILIŞTA
/// koşuyor ve ADD-ONLY — `RoleCatalog`'a eklenen anahtar canlıdaki
/// role de düşüyor, hiçbir grant silinmiyor. Gerçek risk yayılma
/// değil, 13 elle yazılan listeden birini UNUTMAKTI; o da tek bir
/// ortak küme (`RoleCatalog.HerRolde`) ve onu sınayan
/// `RolMesajlasmaTests` ile kapatıldı.
///
/// ── ANAHTAR ÜYELİK KAPISININ YERİNE GEÇMEZ ──
///
/// `mesajlar.view` "mesajlaşmayı kullanabilir" demektir, "her mesajı
/// okur" DEĞİL. İki kapı üst üste durur: anahtar özelliğe, üyelik
/// konuşmaya. Anahtarı olan biri hâlâ yalnız kendi konuşmasını
/// görür — `MessagingAccessExtensions` bunu sağlıyor ve GM için bile
/// kısayolu yok.
/// </summary>
[ApiController]
[Authorize]
[Route("api/mesajlar")]
public sealed class MesajlarController(
    IMesajlasmaService mesajlar,
    IMesajEkiServisi ekler) : ControllerBase
{
    /// <summary>Sayfa boyu tavanı — istemci daha fazlasını isteyemez.</summary>
    private const int EnFazlaLimit = 50;

    public sealed record MesajGonderIstegi(string Govde);

    public sealed record BirebirAcIstegi(Guid KarsiUserId);

    [HttpGet("konusmalar")]
    [RequirePermission(PermissionCatalog.Keys.MesajlarView)]
    public Task<IActionResult> Konusmalar(
        [FromQuery] DateTime? imlecZaman,
        [FromQuery] Guid? imlecId,
        [FromQuery] int limit,
        CancellationToken cancellationToken) =>
        SarmalaAsync(async () => Ok(await mesajlar.KonusmalarimAsync(
            imlecZaman, imlecId, Limitle(limit), cancellationToken)));

    [HttpPost("konusmalar/birebir")]
    [RequirePermission(PermissionCatalog.Keys.MesajlarSend)]
    public Task<IActionResult> BirebirAc(
        BirebirAcIstegi istek, CancellationToken cancellationToken) =>
        SarmalaAsync(async () => Ok(
            await mesajlar.BirebirKonusmaAcAsync(istek.KarsiUserId, cancellationToken)));

    [HttpGet("konusmalar/{id:guid}/mesajlar")]
    [RequirePermission(PermissionCatalog.Keys.MesajlarView)]
    public Task<IActionResult> Mesajlar(
        Guid id,
        [FromQuery] DateTime? imlecZaman,
        [FromQuery] Guid? imlecId,
        [FromQuery] int limit,
        CancellationToken cancellationToken) =>
        SarmalaAsync(async () => Ok(await mesajlar.MesajlarAsync(
            id, imlecZaman, imlecId, Limitle(limit), cancellationToken)));

    [HttpPost("konusmalar/{id:guid}/mesajlar")]
    [RequirePermission(PermissionCatalog.Keys.MesajlarSend)]
    public Task<IActionResult> Gonder(
        Guid id, MesajGonderIstegi istek, CancellationToken cancellationToken) =>
        SarmalaAsync(async () => Ok(
            await mesajlar.MesajGonderAsync(id, istek.Govde, cancellationToken)));

    [HttpPost("konusmalar/{id:guid}/okundu")]
    [RequirePermission(PermissionCatalog.Keys.MesajlarView)]
    public Task<IActionResult> Okundu(Guid id, CancellationToken cancellationToken) =>
        SarmalaAsync(async () =>
        {
            await mesajlar.OkunduIsaretleAsync(id, cancellationToken);
            return Ok(new { message = "Okundu işaretlendi." });
        });

    /// <summary>Rozet ve sekme başlığı bunu çağırıyor.</summary>
    [HttpGet("okunmamis")]
    [RequirePermission(PermissionCatalog.Keys.MesajlarView)]
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
    [RequirePermission(PermissionCatalog.Keys.MesajlarView)]
    public Task<IActionResult> Ara(
        [FromQuery] string? q,
        [FromQuery] DateTime? imlecZaman,
        [FromQuery] Guid? imlecId,
        [FromQuery] int limit,
        CancellationToken cancellationToken) =>
        SarmalaAsync(async () => Ok(await mesajlar.AraAsync(
            q ?? string.Empty, imlecZaman, imlecId, Limitle(limit), cancellationToken)));

    [HttpGet("kisiler")]
    [RequirePermission(PermissionCatalog.Keys.MesajlarSend)]
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

    // ═══════════════════════════════════════════════════════════
    // EKLER (MESAJ/3 Parça 3)
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Mesaja dosya ekler.
    ///
    /// İZİN: `mesajlar.send` — ek göndermek mesaj göndermenin
    /// parçası. Üyelik kapısı ayrıca serviste; ikisi ayrı şey
    /// sınıyor (anahtar = özelliği kullanabilir mi, üyelik = BU
    /// konuşmanın tarafı mı).
    /// </summary>
    [HttpPost("mesajlar/{mesajId:guid}/ekler")]
    [RequirePermission(PermissionCatalog.Keys.MesajlarSend)]
    [RequestSizeLimit(MesajEkiKurallari.DosyaBasinaEnFazlaBayt
                      * MesajEkiKurallari.MesajBasinaEnFazlaDosya)]
    public async Task<IActionResult> EkYukle(
        Guid mesajId, [FromForm] IFormFileCollection dosyalar, CancellationToken ct)
    {
        try
        {
            return Ok(await ekler.EkleAsync(mesajId, [.. dosyalar], ct));
        }
        catch (UnauthorizedAccessException)
        {
            // "Yetkin yok" ile "yok" AYNI cevabı alıyor: hangi
            // mesajların var olduğunu sızdırmamak için.
            return NotFound(new { message = "Mesaj bulunamadı." });
        }
        catch (InvalidOperationException hata)
        {
            // SEBEP SÖYLENİYOR: kullanıcı neden reddedildiğini
            // görmeli, yoksa aynı dosyayı tekrar dener.
            return BadRequest(new { message = hata.Message });
        }
    }

    /// <summary>Verilen mesajların eklerini listeler.</summary>
    [HttpGet("ekler")]
    [RequirePermission(PermissionCatalog.Keys.MesajlarView)]
    public async Task<IActionResult> EkleriListele(
        [FromQuery] Guid[] mesajId, CancellationToken ct) =>
        Ok(await ekler.ListeleAsync(mesajId ?? [], ct));

    /// <summary>
    /// Eki indirir — YETKİ KONTROLÜ YAPAN UÇ (C5).
    ///
    /// Dosyalar statik URL'den servis EDİLMİYOR; her indirme bu
    /// uçtan geçiyor ve çağıranın hem konuşmanın katılımcısı hem
    /// izinli olduğu KANONİK çözücüyle doğrulanıyor.
    ///
    /// C6 BAŞLIKLARI:
    ///   Content-Disposition: attachment  — `File(..., dosyaAdı)`
    ///     üçüncü parametresiyle geliyor; tarayıcıda inline açılmaz.
    ///   X-Content-Type-Options: nosniff  — BAŞLIK/1 middleware'i
    ///     her yanıta ekliyor (tek kanonik yer).
    /// </summary>
    [HttpGet("ekler/{ekId:guid}/indir")]
    [RequirePermission(PermissionCatalog.Keys.MesajlarView)]
    public async Task<IActionResult> EkIndir(Guid ekId, CancellationToken ct)
    {
        var sonuc = await ekler.IndirAsync(ekId, ct);

        if (sonuc is null)
            return NotFound(new { message = "Ek bulunamadı." });

        var akis = new FileStream(
            sonuc.TamYol, FileMode.Open, FileAccess.Read, FileShare.Read);

        return File(akis, sonuc.ContentType, sonuc.Ad);
    }
}
