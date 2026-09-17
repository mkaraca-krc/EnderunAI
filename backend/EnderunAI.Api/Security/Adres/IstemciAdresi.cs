using Microsoft.AspNetCore.Http;
using System.Net;

namespace EnderunAI.Api.Security.Adres;

/// <summary>Çözülen adres ve ona ne kadar güvenildiği.</summary>
/// <param name="Adres">Kayda yazılacak adres.</param>
/// <param name="VekildenGeldi">
/// İstek BİZİM vekilimizden mi geldi. `false` ise `X-Forwarded-For`
/// dikkate ALINMAMIŞTIR ve <paramref name="Adres"/> bağlantı adresidir.
/// </param>
public readonly record struct IstemciAdresSonucu(string Adres, bool VekildenGeldi)
{
    /// <summary>
    /// Denetim kaydına yazılacak hâli. Vekilden GELMEYEN istekler
    /// `(vekilsiz)` ile işaretlenir — sessiz geri düşüş yok.
    ///
    /// Üretimde tüm trafik nginx → Next → arka uç zincirinden geçer,
    /// yani bu işaretin kayda düşmesi BAŞLI BAŞINA BİR BULGUDUR:
    /// biri arka uca doğrudan ulaşmış demektir.
    /// </summary>
    public string KayitIcin => VekildenGeldi ? Adres : $"{Adres} (vekilsiz)";
}

/// <summary>
/// İSTEMCİ ADRESİ — TEK ÇÖZÜCÜ (VEKİL/2, 2026-09-17).
///
/// ═══ NEDEN VAR ═══
///
/// Ölçüldü: `X-Forwarded-For`a bakan **1** dosya vardı
/// (`AuthController`), `RemoteIpAddress`i doğrudan okuyan **11** yer.
/// O 11'in içinde `AuditSaveChangesInterceptor` de vardı — yani HER
/// `Created`/`Updated` denetim satırı bağlantı adresini yazıyordu ve
/// bağlantı adresi hep vekilin kendisiydi (`127.0.0.1`).
///
/// Sonda (17.09, `[...path]` vekilinden geçen portal jetonu reddi):
/// iki farklı adresten iki satır, **ikisi de 127.0.0.1**.
///
/// ═══ XFF'E KOŞULSUZ GÜVENİLMEZ ═══
///
/// Başlığı istemci de gönderebilir. Bu yüzden başlığa **yalnız istek
/// bizim kendi vekilimizden geldiğinde** bakılır: `RemoteIpAddress`
/// bilinen yerel vekil değilse başlık YOK SAYILIR.
///
/// Üretim zinciri: istemci → nginx → Next (127.0.0.1) → arka uç.
/// Arka uca gelen bağlantının eşi her zaman yereldir.
///
/// ═══ SON ELEMAN KURALI TAŞINDI, YENİDEN YAZILMADI ═══
///
/// Kural `AuthController.ResolveClientIp()`ten olduğu gibi alındı.
/// Gerekçesi (2026-09-15 ölçümü): nginx gerçek adresi
/// `$proxy_add_x_forwarded_for` ile **sona** ekler; İLK eleman
/// istemcinin yazdığıdır. İlk elemanı almak, hız sınırını istemcinin
/// kontrolüne bırakıyordu — ölçülüp kapatıldı.
///
/// 17.09'da nginx ÜZERİNDEN uydurma `X-Forwarded-For: 9.9.9.9` ile
/// giriş denendi: kayda **9.9.9.9 DÜŞMEDİ**, gerçek bağlantı adresi
/// düştü. Zincir doğru kuruluyor.
/// </summary>
public static class IstemciAdresCozucu
{
    /// <summary>
    /// Bizim vekilimiz sayılan adresler. Üretimde nginx ve Next aynı
    /// makinede; arka uca gelen bağlantı her zaman yereldir.
    ///
    /// LİSTE DAR TUTULUYOR: buraya bir aralık eklemek, o aralıktaki
    /// herkesin `X-Forwarded-For` yazabilmesi demektir.
    /// </summary>
    private static bool YerelVekilMi(IPAddress? adres)
    {
        if (adres is null) return false;
        if (IPAddress.IsLoopback(adres)) return true;

        // IPv6 eşlemeli IPv4 loopback (::ffff:127.0.0.1)
        if (adres.IsIPv4MappedToIPv6 &&
            IPAddress.IsLoopback(adres.MapToIPv4())) return true;

        return false;
    }

    public static IstemciAdresSonucu Coz(HttpContext? context)
    {
        if (context is null)
            return new IstemciAdresSonucu("unknown", VekildenGeldi: false);

        var baglanti = context.Connection.RemoteIpAddress;
        var baglantiMetni = baglanti?.ToString() ?? "unknown";

        // GÜVEN KAPISI: vekilden gelmiyorsa başlığa BAKILMAZ.
        if (!YerelVekilMi(baglanti))
            return new IstemciAdresSonucu(baglantiMetni, VekildenGeldi: false);

        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            var parcalar = forwardedFor.Split(',');
            var sonuncu = parcalar[^1].Trim();
            if (!string.IsNullOrWhiteSpace(sonuncu))
                return new IstemciAdresSonucu(sonuncu, VekildenGeldi: true);
        }

        // Vekilden geldi ama başlık yok: vekil başlığı iletmiyor olabilir.
        // Bu da sessiz geçmemeli — adres bağlantıdan alınıyor ama işaret
        // "vekilden geldi" kalıyor, çünkü geldi; eksik olan BAŞLIK.
        return new IstemciAdresSonucu(baglantiMetni, VekildenGeldi: true);
    }

    /// <summary>Kısa yol: doğrudan kayda yazılacak metin.</summary>
    public static string KayitAdresi(HttpContext? context) => Coz(context).KayitIcin;
}
