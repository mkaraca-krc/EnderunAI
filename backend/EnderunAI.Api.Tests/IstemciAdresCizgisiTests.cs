using System.Text.RegularExpressions;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// İSTEMCİ ADRESİ ÇİZGİSİ (VEKİL/2, 2026-09-17).
///
/// `HttpContext.Connection.RemoteIpAddress` **yalnız çözücünün içinde**
/// okunur. Başka her yerde `IstemciAdresCozucu` kullanılır.
///
/// ═══ NEDEN ═══
///
/// Üretim zinciri istemci → nginx → Next → arka uç. Bağlantı adresi HER
/// ZAMAN vekilin kendisidir. Doğrudan okuyan her yer, kayda `127.0.0.1`
/// yazar ve o kayıt adli değerini kaybeder. Ölçüldü (17.09): denetim
/// kaydındaki her `Created`/`Updated` satırı ve her
/// `PortalTokenRejected` satırı 127.0.0.1 taşıyordu.
///
/// ═══ ÇİZGİ NEDEN SIFIR DEĞİL ═══
///
/// Başlangıçta 11 okuma noktası vardı. Üçü düzeltildi (denetim
/// izleyicisi, portal, AuthController). Kalan **8**'i tek geçişte, HER
/// BİRİ ÖLÇÜLEREK düzeltilecek (Mehmet Bey'in sırası). Çizgi o yüzden
/// bugün 8; **artamaz**, azaldıkça elle indirilir.
///
/// psql ve pkill çizgileriyle aynı biçim: sıfır tolerans + gerekçeli,
/// GEÇİCİ istisna listesi + ölü istisna kapısı.
/// </summary>
public sealed class IstemciAdresCizgisiTests
{
    /// <summary>
    /// HENÜZ ÇEVRİLMEMİŞ okuma noktaları. Her satır bir BORÇTUR, bir
    /// muafiyet değil: düzeltilecek ve buradan silinecek.
    /// </summary>
    private static readonly Dictionary<string, string> KalanBorclar = new()
    {
        ["Program.cs"] =
            "İki nokta (911, 927): istek günlüğü ve hız sınırı anahtarı. "
            + "Ölçülmeden çevrilmeyecek — hız sınırı anahtarını değiştirmek "
            + "canlı davranışı değiştirir.",
        ["Security/WorkHourAccessMiddleware.cs"] =
            "Mesai dışı giriş reddi denetim satırı. Adli değeri var; sırada.",
        ["Controllers/EmployerPortalLinkController.cs"] =
            "İşveren portal bağlantısı üretimi/iptali denetim satırı.",
        ["Controllers/WorkTasksController.cs"] =
            "Görev işlemi denetim satırı.",
        ["Controllers/CompanySettingsController.cs"] =
            "Şirket ayarı değişikliği denetim satırı.",
        ["Services/Procurement/ProcurementApprovalService.cs"] =
            "Satın alma onayı denetim satırı — 64 karaktere kırpılıyor, "
            + "'(vekilsiz)' işareti kırpmaya takılabilir; çevrilirken ölçülecek.",
        ["Services/Hizir/HizirActionAuditor.cs"] =
            "Hızır eylem denetçisi.",
    };

    private static string DepoKoku()
    {
        var dizin = new DirectoryInfo(AppContext.BaseDirectory);
        while (dizin is not null && !Directory.Exists(Path.Combine(dizin.FullName, "deploy")))
            dizin = dizin.Parent;
        return dizin?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    private static string ApiKoku() => Path.Combine(DepoKoku(), "backend", "EnderunAI.Api");

    /// <summary>
    /// ÇAĞRI SAYAR, BAHSİ DEĞİL.
    ///
    /// Yorumda geçen `Connection.RemoteIpAddress` bir ÇAĞRI DEĞİLDİR.
    /// Bu ayrımı bu depoda beş ayrı sonda yapmak zorunda kaldı; desen
    /// buraya da aynı sebeple yazılıyor.
    /// </summary>
    internal static bool OkumaSatiri(string satir)
    {
        var t = satir.TrimStart();
        if (t.StartsWith("//") || t.StartsWith("*") || t.StartsWith("/*")) return false;
        return Regex.IsMatch(satir, @"Connection\s*\?\s*\.\s*RemoteIpAddress|Connection\.RemoteIpAddress");
    }

    private static List<string> OkumaNoktalari()
    {
        var kok = ApiKoku();
        var bulunan = new List<string>();

        foreach (var yol in Directory.GetFiles(kok, "*.cs", SearchOption.AllDirectories))
        {
            var kisa = Path.GetRelativePath(kok, yol).Replace('\\', '/');
            if (kisa.StartsWith("Migrations/")) continue;
            if (kisa.StartsWith("Security/Adres/")) continue;   // çözücünün kendisi

            foreach (var satir in File.ReadAllLines(yol))
                if (OkumaSatiri(satir)) bulunan.Add(kisa);
        }

        return bulunan;
    }

    [Fact]
    public void SondaIsiriyor_CagriIleYorumuAYIRIYOR()
    {
        // Kural 93: yeşil, sondanın ısırabildiği gösterilmeden rapora girmez.
        Assert.True(OkumaSatiri("        var ip = context.Connection.RemoteIpAddress?.ToString();"));
        Assert.True(OkumaSatiri("    IpAddress = http?.Connection.RemoteIpAddress?.ToString(),"));
        // Gerçek dosyadan alınmış YORUM satırı — sayılmaz:
        Assert.False(OkumaSatiri("        // Burada `Connection.RemoteIpAddress` okunuyordu. Üretim zinciri"));
    }

    [Fact]
    public void TaramaSagligi_DosyaTarandi()
    {
        var kok = ApiKoku();
        Assert.True(Directory.GetFiles(kok, "*.cs", SearchOption.AllDirectories).Length > 100);
    }

    [Fact]
    public void CizgiYiASMAMIS_yeni_dogrudan_okuma_eklenmemis()
    {
        var yeniler = OkumaNoktalari()
            .Where(x => !KalanBorclar.ContainsKey(x))
            .Distinct()
            .ToList();

        Assert.True(
            yeniler.Count == 0,
            "Çözücü dışında YENİ doğrudan `Connection.RemoteIpAddress` okuması: "
            + string.Join(", ", yeniler)
            + ". Bunun yerine EnderunAI.Api.Security.Adres.IstemciAdresCozucu kullanın — "
            + "bağlantı adresi üretimde her zaman vekilin kendisidir ve kayıt adli "
            + "değerini kaybeder.");
    }

    [Fact]
    public void OluBorcBirakilmaz_duzeltilen_listeden_silinir()
    {
        var mevcut = OkumaNoktalari().Distinct().ToHashSet();
        var olu = KalanBorclar.Keys.Where(x => !mevcut.Contains(x)).ToList();

        Assert.True(
            olu.Count == 0,
            "Borç listesinde artık okuma yapmayan dosya(lar) var — düzeltildiyse "
            + "listeden SİLİN, yoksa liste gerçeği anlatmaz: " + string.Join(", ", olu));
    }
}
