using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Portal;

/// <summary>
/// İŞVEREN PORTALI BAĞLANTI ÇÖZÜMÜ — TEK NOKTA.
///
/// NEDEN SERVİS: portal, sistemin kimlik doğrulaması olmayan TEK veri
/// kapısı. Süre kontrolü, erişim sayacı ve başarısız deneme kaydı dört
/// ucun her birinde ayrı ayrı yazılsaydı, biri unutulduğunda o uç
/// sessizce korumasız kalırdı — tam olarak RetailSalesController'da
/// [Authorize] unutulduğunda olan şey. Karar tek yerde veriliyor.
///
/// TOKEN'IN TAMAMI HİÇBİR YERE YAZILMIYOR. Denetim kaydına yalnız ilk
/// 8 karakter giriyor: bir tarama girişimini tanımaya yetiyor, ama
/// kaydı okuyan birine çalışan bir anahtar vermiyor. Güvenlik kaydı,
/// koruduğu sırrı ele veren bir yer olamaz.
/// </summary>
public interface IPortalLinkResolver
{
    /// <summary>
    /// Token'ı çözer. Geçersiz, iptal edilmiş veya SÜRESİ GEÇMİŞ
    /// bağlantıda `null` döner — çağıran 404 vermeli, 401 değil.
    /// Başarısız denemeyi güvenlik kaydına kendisi yazar.
    /// </summary>
    Task<EmployerPortalLink?> ResolveAsync(
        string? token,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken);
}

public sealed class PortalLinkResolver(AppDbContext db) : IPortalLinkResolver
{
    /// <summary>Denetim kaydına giren token önekinin uzunluğu.</summary>
    private const int OnekUzunlugu = 8;

    /// <summary>
    /// ŞÜPHELİ TARAMA EŞİĞİ: aynı IP'den bu pencerede bu kadar
    /// başarısız deneme olursa ayrı bir olay düşüyor.
    ///
    /// Sayılar hız sınırından türetildi: portal ucu dakikada 60 istek
    /// kabul ediyor, yani meşru bir tarayıcı oturumu bu eşiğe asla
    /// yaklaşmaz — meşru kullanıcı zaten GEÇERLİ token'la geliyor,
    /// başarısız denemesi olmaz. On dakikada on başarısız deneme,
    /// elle yapılan bir hatanın değil, aramanın imzasıdır.
    /// </summary>
    private static readonly TimeSpan TaramaPenceresi = TimeSpan.FromMinutes(10);
    private const int TaramaEsigi = 10;

    public async Task<EmployerPortalLink?> ResolveAsync(
        string? token,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            await BasarisizYazAsync(
                token, ipAddress, userAgent, "bos_token", cancellationToken);
            return null;
        }

        var simdi = DateTime.UtcNow;

        /*
         * ÜÇ ŞART BİRDEN: aktif, iptal edilmemiş, süresi geçmemiş.
         *
         * Süre kontrolü sorgunun İÇİNDE, sonuç üzerinde değil.
         * Dışarıda yapılsaydı kayıt önce belleğe çekilir ve
         * "bulundu ama geçersiz" ile "hiç yok" ayrımı kodun akışında
         * görünür hale gelirdi; zamanlama farkı bile bilgi sızdırır.
         */
        var link = await db.EmployerPortalLinks
            .SingleOrDefaultAsync(
                x => x.Token == token &&
                     x.IsActive &&
                     x.RevokedAtUtc == null &&
                     x.ExpiresAtUtc > simdi,
                cancellationToken);

        if (link is null)
        {
            /*
             * SEBEBİ AYIRT ETMEK İÇİN İKİNCİ SORGU: kayıt var ama
             * geçersiz mi, yoksa hiç yok mu. Bu ayrım YALNIZ DENETİM
             * KAYDINA giriyor; dışarıya dönen yanıt her iki durumda da
             * aynı (404). İçeride bilmek gerekiyor çünkü "süresi
             * dolmuş bağlantıyı kullanmaya çalışan işveren" ile
             * "token arayan yabancı" farklı olaylardır.
             */
            var sebep = await SebepBulAsync(token, simdi, cancellationToken);

            await BasarisizYazAsync(
                token, ipAddress, userAgent, sebep, cancellationToken);

            return null;
        }

        /*
         * ERİŞİM İZİ: son açılma ve toplam açılma sayısı. Yönetim
         * ekranı "bu bağlantı kullanılıyor mu" sorusunu buradan
         * cevaplıyor — kullanılmayanı iptal etmek, kullanılanı
         * uzatmak için.
         */
        link.LastAccessedAtUtc = simdi;
        link.AccessCount += 1;
        await db.SaveChangesAsync(cancellationToken);

        return link;
    }

    private async Task<string> SebepBulAsync(
        string token, DateTime simdi, CancellationToken cancellationToken)
    {
        var kayit = await db.EmployerPortalLinks
            .AsNoTracking()
            .Where(x => x.Token == token)
            .Select(x => new
            {
                x.IsActive,
                x.RevokedAtUtc,
                x.ExpiresAtUtc
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (kayit is null) return "bilinmeyen_token";
        if (kayit.RevokedAtUtc != null || !kayit.IsActive) return "iptal_edilmis";
        if (kayit.ExpiresAtUtc <= simdi) return "suresi_gecmis";

        return "gecersiz";
    }

    private async Task BasarisizYazAsync(
        string? token,
        string? ipAddress,
        string? userAgent,
        string sebep,
        CancellationToken cancellationToken)
    {
        // TOKEN'IN TAMAMI DEĞİL, ÖNEKİ. Kısa token'da önek de kısalır;
        // Substring yerine bu yüzden uzunluk sınırlanıyor.
        var onek = string.IsNullOrEmpty(token)
            ? "(bos)"
            : token[..Math.Min(OnekUzunlugu, token.Length)];

        db.SecurityAuditEvents.Add(new SecurityAuditEvent
        {
            Action = "PortalTokenRejected",
            EntityType = "EmployerPortalLink",
            DetailsJson = JsonSerializer.Serialize(new
            {
                summary = $"Geçersiz portal bağlantısı denendi ({sebep}).",
                sebep,
                tokenOneki = onek
            }),
            IpAddress = ipAddress,
            UserAgent = userAgent,
            OccurredAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);

        await TaramaKontrolAsync(ipAddress, userAgent, cancellationToken);
    }

    /// <summary>
    /// Aynı IP'den yoğun başarısız deneme varsa AYRI bir olay düşer.
    /// Tek tek redler kaydın içinde kaybolur; "tarama" olayı, kaydı
    /// okuyan kişinin gözüne çarpması gereken şeydir.
    /// </summary>
    private async Task TaramaKontrolAsync(
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ipAddress)) return;

        var pencereBasi = DateTime.UtcNow - TaramaPenceresi;

        var basarisizSayisi = await db.SecurityAuditEvents
            .AsNoTracking()
            .CountAsync(
                x => x.Action == "PortalTokenRejected" &&
                     x.IpAddress == ipAddress &&
                     x.OccurredAtUtc >= pencereBasi,
                cancellationToken);

        if (basarisizSayisi < TaramaEsigi) return;

        /*
         * PENCERE BAŞINA BİR KEZ: eşik aşıldıktan sonraki her istek
         * yeni bir "tarama" olayı yazsaydı, kayıt aynı olayın
         * kopyalarıyla dolar ve asıl bilgi görünmez olurdu.
         */
        var zatenVar = await db.SecurityAuditEvents
            .AsNoTracking()
            .AnyAsync(
                x => x.Action == "PortalTokenScanSuspected" &&
                     x.IpAddress == ipAddress &&
                     x.OccurredAtUtc >= pencereBasi,
                cancellationToken);

        if (zatenVar) return;

        db.SecurityAuditEvents.Add(new SecurityAuditEvent
        {
            Action = "PortalTokenScanSuspected",
            EntityType = "EmployerPortalLink",
            DetailsJson = JsonSerializer.Serialize(new
            {
                summary =
                    $"Aynı IP'den {TaramaPenceresi.TotalMinutes:0} dakikada " +
                    $"{basarisizSayisi} başarısız portal bağlantısı denemesi.",
                basarisizSayisi,
                pencereDakika = TaramaPenceresi.TotalMinutes
            }),
            IpAddress = ipAddress,
            UserAgent = userAgent,
            OccurredAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
    }
}
