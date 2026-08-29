using System.ComponentModel.DataAnnotations;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// İSTEMCİ HATALARININ KAYDI (KABUK DAYANIKLILIĞI).
///
/// NEDEN VAR: hata sınırı kullanıcıya bir ekran gösteriyor ama kimse
/// haberdar olmuyordu. Kullanıcı "bir şeyler ters gitti" görüp başka
/// bir ekrana geçtiğinde olay kayıtsız kayboluyor; aynı hata yüz
/// kişide olsa bile kimse bilmiyor.
///
/// VERİTABANI TABLOSU YOK — BİLEREK. Tablo, göç demektir; bu bir
/// sertleştirme yaması ve göç taşımıyor. Ayrıca istemci hatası
/// istemciden TETİKLENEN bir kayıttır: tabloya yazılsaydı, bozuk ya
/// da kötü niyetli bir istemci veritabanını şişirebilirdi. Sunucu
/// günlüğü bu iş için doğru yer; dönerli (rotating) ve zaten
/// izleniyor.
///
/// İZİN YOK, YALNIZ KİMLİK. Ayrı bir izin anahtarı açılmadı: her
/// giriş yapmış kullanıcı kendi ekranında hata alabilir ve bunu
/// bildirebilmelidir. `[Authorize]` yine de ZORUNLU — anonime açık
/// bir günlük yazma ucu, günlüğü şişirmenin en kolay yoludur.
/// </summary>
[ApiController]
[Authorize]
[Route("api/istemci-hatalari")]
public sealed class IstemciHatalariController(
    ILogger<IstemciHatalariController> logger,
    ICurrentUserService currentUser) : ControllerBase
{
    /// <summary>
    /// KABUL EDİLEN ALANLAR BEYAZ LİSTE.
    ///
    /// Serbest bir nesne alınsaydı istemci günlüğe istediğini
    /// yazdırabilirdi — tutar, IBAN, cari unvanı dahil. Alanlar tek
    /// tek tanımlı ve hepsi uzunluk sınırlı.
    /// </summary>
    public sealed class IstemciHatasiIstegi
    {
        /// <summary>"kabuk" ya da "içerik".</summary>
        [Required, MaxLength(40)]
        public string Nerede { get; set; } = string.Empty;

        /// <summary>"TypeError" gibi.</summary>
        [Required, MaxLength(80)]
        public string HataAdi { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? Mesaj { get; set; }

        [MaxLength(300)]
        public string? Yol { get; set; }
    }

    [HttpPost]
    public IActionResult Bildir([FromBody] IstemciHatasiIstegi istek)
    {
        /*
         * KULLANICI KİMLİĞİ OTURUMDAN, İSTEKTEN DEĞİL. İstemcinin
         * gönderdiği bir kimliğe güvenilseydi, bir kullanıcı başka
         * birinin adına hata kaydı ürettirebilirdi.
         *
         * YOL MASKELENEREK YAZILIR: portal bağlantısı sırrını yolun
         * kendisinde taşıyor. Maskeleme mantığı GlobalExceptionHandler
         * ile aynı kaynaktan (SensitivePathMasker) geliyor; ikinci bir
         * maskeleme yazılsaydı biri güncellenip diğeri kalırdı.
         *
         * MESAJ KISALTILIYOR: bir servis hatası iş metnini mesajın
         * içinde taşıyabilir. İstemci de kısaltıyor ama sunucu ona
         * güvenmiyor — sınırı iki tarafta da uygulamak, istemcinin
         * atlatılabilir olmasındandır.
         */
        var mesaj = istek.Mesaj is null
            ? string.Empty
            : istek.Mesaj.Length > 200 ? istek.Mesaj[..200] : istek.Mesaj;

        logger.LogWarning(
            "İstemci hatası. Nerede={Nerede} Hata={HataAdi} Mesaj={Mesaj} "
            + "Yol={Yol} UserId={UserId}",
            istek.Nerede,
            istek.HataAdi,
            mesaj,
            SensitivePathMasker.Mask(istek.Yol),
            currentUser.UserId);

        return NoContent();
    }
}
