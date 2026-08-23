using System.Net;
using System.Text.RegularExpressions;
using EnderunAI.Api.Tests.Infrastructure;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// KİMLİK DOĞRULAMA BEKÇİSİ — HER CONTROLLER [Authorize] TAŞIMALI.
///
/// NEDEN GEREKLİ: `RequirePermission` düz bir ATTRIBUTE'tur, filtre
/// değil. Zorlamayı `PermissionAuthorizationMiddleware` yapıyor ve o
/// middleware kimlik doğrulanmamış isteği KONTROL ETMEDEN geçiriyor:
///
///     if (context.User.Identity?.IsAuthenticated != true)
///     {
///         await next(context);   // izin kontrolü HİÇ çalışmıyor
///         return;
///     }
///
/// Yani izin kontrolü yalnızca giriş yapmış kullanıcılar için var.
/// Kimlik zorlaması TEK BAŞINA [Authorize]'dan geliyor ve bir
/// controller'da unutulduğunda o modülün tamamı anonime açılıyor —
/// üstelik uçta `[RequirePermission(...)]` yazdığı için KORUNUYOR
/// GİBİ görünüyor. 2026-08-22'de RetailSalesController'da tam olarak
/// bu vardı: satış listesi, ürün fiyatları ve gün sonu kasa raporu
/// kimlik doğrulamasız çağrılabiliyordu.
///
/// İSTİSNALAR GEREKÇELİ: gerekçesiz istisna sessiz bir karardır.
/// </summary>
public sealed class AuthorizeGuardTests
{
    private static readonly Dictionary<string, string> Istisnalar = new()
    {
        ["AuthController.cs"] =
            "GİRİŞ UCU. Kimlik doğrulamanın kendisi burada yapılıyor; " +
            "[Authorize] konulsaydı kimse giriş yapamazdı.",
        ["PortalController.cs"] =
            "KENDİ TOKEN MODELİ. Portal, JWT ile değil rota içindeki " +
            "paylaşım anahtarıyla doğrulanıyor; erişim kontrolü " +
            "controller'ın kendi içinde.",
    };

    [Fact]
    public void HerController_AuthorizeTasimali()
    {
        var kok = BulKok();
        var dizin = Path.Combine(kok, "EnderunAI.Api", "Controllers");

        var korumasiz = new List<string>();

        foreach (var dosya in Directory
                     .EnumerateFiles(dizin, "*.cs", SearchOption.AllDirectories)
                     .OrderBy(x => x, StringComparer.Ordinal))
        {
            var ad = Path.GetFileName(dosya);
            if (Istisnalar.ContainsKey(ad)) continue;

            var metin = File.ReadAllText(dosya);

            // Sınıf düzeyinde [Authorize] ya da [Authorize(...)].
            if (Regex.IsMatch(metin, @"^\[Authorize(\(.*\))?\]", RegexOptions.Multiline))
                continue;

            korumasiz.Add(ad);
        }

        Assert.True(
            korumasiz.Count == 0,
            "KİMLİK DOĞRULAMASI OLMAYAN CONTROLLER:\n  " +
            string.Join("\n  ", korumasiz) +
            "\n\nSınıfa [Authorize] ekleyin. `RequirePermission` tek " +
            "başına YETMEZ: izin middleware'i kimlik doğrulanmamış " +
            "isteği kontrol etmeden geçiriyor, yani uç anonime açık " +
            "kalır ama korunuyormuş gibi görünür. Gerçekten anonim " +
            "olması gerekiyorsa AuthorizeGuardTests içindeki İstisnalar " +
            "listesine GEREKÇESİYLE ekleyin.");
    }

    [Fact]
    public void Istisnalar_GerekcesizOlamaz()
    {
        foreach (var (ad, gerekce) in Istisnalar)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(gerekce),
                $"{ad} istisna listesinde ama gerekçesi yok.");
        }
    }

    private static string BulKok()
    {
        var dizin = new DirectoryInfo(AppContext.BaseDirectory);

        while (dizin is not null &&
               !Directory.Exists(Path.Combine(dizin.FullName, "EnderunAI.Api")))
        {
            dizin = dizin.Parent;
        }

        return dizin?.FullName
            ?? throw new InvalidOperationException("Çözüm kökü bulunamadı.");
    }
}
