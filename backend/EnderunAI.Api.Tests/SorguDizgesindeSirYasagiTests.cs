using System.Text.RegularExpressions;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// UÇ ADRESLERİNDE SORGU DİZGESİNE SIR KONMAZ — YAPISAL MUHAFAZA.
///
/// ═══ NEDEN VAR (ÖLÇÜM: 2026-09-13) ═══
///
/// Bir kaydımız *"sorgu dizgeleri günlüğe yazılmıyor"* diyordu; YANLIŞTI.
/// nginx `log_format` alanı `$request_uri` kullanıyor ve o sorgu
/// dizgesini içeriyor. 16 günlük erişim günlüğünde ölçüldü: hassas
/// parametre adı **0** (pozitif kontrol: `companyId` 994 kez bulundu,
/// yani arama kör değil).
///
/// Bugün 0 olması yarını bağlamaz. Bir uç eklenir, jeton sorgu
/// dizgesine konur ve erişim günlüğüne düşer — 90 gün orada durur,
/// yedeklere girer, günlüğü okuyabilen herkes jetonu alır.
///
/// Portal jetonu bu yüzden zaten nginx'te maskeleniyor
/// (`portal-token-maskeleme.conf`); maskeleme bir yamadır, YASAK
/// kaynaktadır (Kural 79).
///
/// ═══ ÇIRA AYRI, BU TEST AYRI ═══
///
/// `deploy/scripts/sorgu-dizgesi-cirasi.sh` TRAFİĞİ ölçer (dün ne oldu).
/// Bu test KAYNAĞI ölçer (yarın ne olabilir). İkisi farklı soru.
/// </summary>
public sealed class SorguDizgesindeSirYasagiTests
{
    private static string DepoKoku()
    {
        var dizin = new DirectoryInfo(AppContext.BaseDirectory);
        while (dizin is not null && !Directory.Exists(Path.Combine(dizin.FullName, "deploy")))
        {
            dizin = dizin.Parent;
        }

        return dizin?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    // `?token=` / `&access_token=` / `?parola=` … — adres içinde sır.
    private static readonly Regex Yasak = new(
        @"[?&](access_?token|token|jeton|password|parola|passwd|secret|sir|api_?key|auth|credential|pwd)=",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] TarananKokler =
    [
        Path.Combine("frontend", "enderun-ai", "app"),
        Path.Combine("frontend", "enderun-ai", "components"),
        Path.Combine("frontend", "enderun-ai", "lib"),
        Path.Combine("backend", "EnderunAI.Api", "Controllers"),
        Path.Combine("backend", "EnderunAI.Api", "Services"),
    ];

    private static readonly string[] Uzantilar = [".ts", ".tsx", ".cs"];

    private static IEnumerable<string> Dosyalar()
    {
        var kok = DepoKoku();
        foreach (var alt in TarananKokler)
        {
            var tam = Path.Combine(kok, alt);
            if (!Directory.Exists(tam))
            {
                continue;
            }

            foreach (var d in Directory.EnumerateFiles(tam, "*", SearchOption.AllDirectories))
            {
                if (!Uzantilar.Contains(Path.GetExtension(d)))
                {
                    continue;
                }

                if (d.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}") ||
                    d.Contains($"{Path.DirectorySeparatorChar}.next{Path.DirectorySeparatorChar}"))
                {
                    continue;
                }

                yield return d;
            }
        }
    }

    [Fact]
    public void Tarama_BosaDusmuyor_POZITIF_KONTROL()
    {
        var dosyalar = Dosyalar().ToList();

        // Kural 48: boş küme, ancak aramanın gerçekten baktığı gösterilirse
        // bir şey kanıtlar.
        Assert.True(dosyalar.Count > 300,
            $"Yalnız {dosyalar.Count} dosya tarandı — kapsam çökmüş olabilir.");
        Assert.Contains(dosyalar, d => d.EndsWith(".tsx", StringComparison.Ordinal));
        Assert.Contains(dosyalar, d => d.EndsWith(".cs", StringComparison.Ordinal));

        // Dedektör kör mü: sentetik bir adres YAKALANMALI.
        Assert.Matches(Yasak, "/api/rapor/indir?access_token=abc");
        Assert.Matches(Yasak, "/portal/ac?token=xyz");
        // Ve masum adres YAKALANMAMALI (yanlış kırmızı, Kural 86).
        Assert.DoesNotMatch(Yasak, "/api/stok?companyId=1&limit=50");
        Assert.DoesNotMatch(Yasak, "/api/auth/me");
    }

    /// <summary>
    /// İSTİSNALAR — HER BİRİ GEREKÇELİ, YOKSA KIRMIZI.
    ///
    /// Yasak BİZİM uç adreslerimiz içindir (gelen istek, kendi erişim
    /// günlüğümüze düşer). Üçüncü tarafa GİDEN bir çağrıda anahtarın
    /// nereye konacağını satıcı belirler; oraya yamayla karışamayız.
    /// Ama "giden çağrı" bir muafiyet değil, GEREKÇELİ İSTİSNADIR:
    /// anahtar bizim kendi günlüğümüze de düşebilir.
    /// </summary>
    private static readonly Dictionary<string, string> Istisnalar = new()
    {
        ["backend/EnderunAI.Api/Services/Market/MetalPriceApiLmeSource.cs"] =
            "GİDEN çağrı (metalpriceapi). Anahtar sorgu dizgesinde çünkü satıcının " +
            "şeması bu. ÖLÇÜLDÜ 2026-09-13: journalde 'api_key=' 0 kez (7 gün, " +
            "837.177 satır; pozitif kontrol: 252 HttpClient istek satırı var, yani " +
            "günlükleme açık) ve METAL_API_KEY canlıda TANIMLI DEĞİL — entegrasyon " +
            "ölü. KOŞUL: anahtar tanımlanmadan ÖNCE ya satıcının başlık yolu " +
            "ölçülmeli ya System.Net.Http.HttpClient günlük seviyesi kısılmalı; " +
            "yoksa tam URI journale düşer.",
    };

    [Fact]
    public void HerIstisna_GerekceliOlmali()
    {
        foreach (var (yol, gerekce) in Istisnalar)
        {
            Assert.False(string.IsNullOrWhiteSpace(gerekce), $"Gerekçesiz istisna: {yol}");
            Assert.True(gerekce.Length > 60, $"Gerekçe fazla kısa, ölçüm içermiyor: {yol}");
        }
    }

    [Fact]
    public void Istisnalar_HalaGecerliOlmali()
    {
        // Ölü bir istisna, listeyi yalancı yapar: dosya taşınmış olabilir.
        foreach (var yol in Istisnalar.Keys)
        {
            Assert.True(File.Exists(Path.Combine(DepoKoku(), yol)),
                $"İstisna listesindeki dosya yok, liste çürümüş: {yol}");
        }
    }

    [Fact]
    public void HicbirUcAdresi_SorguDizgesinde_SirTasimaz()
    {
        var bulgular = new List<string>();

        foreach (var dosya in Dosyalar())
        {
            var satirlar = File.ReadAllLines(dosya);
            for (var i = 0; i < satirlar.Length; i++)
            {
                if (!Yasak.IsMatch(satirlar[i]))
                {
                    continue;
                }

                var goreli = Path.GetRelativePath(DepoKoku(), dosya).Replace('\\', '/');
                if (Istisnalar.ContainsKey(goreli))
                {
                    continue;
                }

                bulgular.Add($"{goreli}:{i + 1}  {satirlar[i].Trim()}");
            }
        }

        Assert.True(bulgular.Count == 0,
            "Uç adresinde sorgu dizgesiyle sır taşınıyor — erişim günlüğüne düşer:\n" +
            string.Join("\n", bulgular));
    }
}
