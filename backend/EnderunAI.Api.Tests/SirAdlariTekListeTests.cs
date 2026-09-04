using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// SIR ADLARI TEK LİSTEDE — İKİ TARAYICI, TEK KAYNAK.
///
/// ── NEDEN (2026-09-04) ──
///
/// Sır kontrolü iki yerde koşuyor ve kapsamları FARKLI olmak zorunda:
///
///   1. `SecretInSourceGuardTests` — tüm depo, yayın turunda (~278 sn)
///   2. `deploy/scripts/sir-tara.py` — push edilecek COMMIT ARALIĞI
///      (saniyeler), pre-push kancasında
///
/// İkincisi neden var: sır bekçisinin kaçırdığı şey GERİ ALINAMAZ —
/// geçmişe yazılır. Diğer kapıların kaçırdığı bir sonraki turda
/// yakalanır. Bu asimetri, push öncesinde bir sır kontrolünü zorunlu
/// kılıyor; ama tam tarama SSH bağlantısını düşürdüğü için aralık
/// tarayıcı yazıldı.
///
/// KAPSAMLAR FARKLI, LİSTE AYNI. Liste iki yerde yazılsaydı zamanla
/// ayrışırdı — bu kod tabanının en sık hatası, bir günde altı kez
/// görüldü.
/// </summary>
public sealed class SirAdlariTekListeTests
{
    private static string DepoKok()
    {
        var dizin = new DirectoryInfo(AppContext.BaseDirectory);

        while (dizin is not null &&
               !Directory.Exists(Path.Combine(dizin.FullName, "deploy", "scripts")))
        {
            dizin = dizin.Parent;
        }

        Assert.True(dizin is not null, "Depo kökü bulunamadı.");
        return dizin!.FullName;
    }

    private static string Oku(params string[] p) =>
        File.ReadAllText(Path.Combine(new[] { DepoKok() }.Concat(p).ToArray()));

    private const string ListeYolu = "deploy/bekci/uretim-sir-adlari.txt";

    [Fact]
    public void Liste_Dosyasi_Var_Ve_Dolu_POZITIF_KONTROL()
    {
        var yol = Path.Combine(DepoKok(), "deploy", "bekci", "uretim-sir-adlari.txt");
        Assert.True(File.Exists(yol), $"Sır adları listesi yok: {yol}");

        var veriSatirlari = File.ReadAllLines(yol)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0 && !x.StartsWith('#'))
            .ToList();

        Assert.True(
            veriSatirlari.Count >= 6,
            $"Listede yalnız {veriSatirlari.Count} satır var; tarayıcılar " +
            "neredeyse hiçbir şey sınamaz (Kural 48).");

        Assert.Contains(veriSatirlari, x => x.StartsWith("zorunlu", StringComparison.Ordinal));
        Assert.Contains(veriSatirlari, x => x.StartsWith("henuz-yok", StringComparison.Ordinal));
    }

    [Fact]
    public void Her_Iki_Tarayici_Da_AYNI_Listeyi_Okuyor()
    {
        var csharp = Oku("backend", "EnderunAI.Api.Tests", "SecretInSourceGuardTests.cs");
        var python = Oku("deploy", "scripts", "sir-tara.py");

        Assert.Contains("uretim-sir-adlari.txt", csharp);
        Assert.Contains("uretim-sir-adlari.txt", python);
    }

    [Fact]
    public void Tarayicilarda_GOMULU_Sir_Adi_Listesi_YOK()
    {
        /*
         * ASIL İDDİA: adlar tarayıcıların İÇİNDE yeniden yazılmasın.
         *
         * Bir sır adı, tarayıcı kodunda dizi/liste olarak geçiyorsa o
         * ikinci bir kaynaktır. Yorumda geçmesi serbest — anlatmak,
         * yeniden yazmak değildir.
         */
        var adlar = new[]
        {
            "JWT_SECRET", "SMTP_PASS", "SEED_ADMIN_PASSWORD",
            "ANTHROPIC_API_KEY", "VAPID_PRIVATE_KEY",
        };

        var kaynaklar = new (string Ad, string[] Yol)[]
        {
            ("SecretInSourceGuardTests.cs",
             ["backend", "EnderunAI.Api.Tests", "SecretInSourceGuardTests.cs"]),
            ("sir-tara.py", ["deploy", "scripts", "sir-tara.py"]),
        };

        var bulgular = new List<string>();

        foreach (var (ad, yol) in kaynaklar)
        {
            foreach (var satir in Oku(yol).Split('\n'))
            {
                var s = satir.Trim();

                // Yorumlar serbest.
                if (s.StartsWith("//") || s.StartsWith("#") ||
                    s.StartsWith("*") || s.StartsWith("/*")) continue;

                // Dizgi sabiti içinde sır adı = gömülü liste işareti.
                foreach (var sir in adlar)
                {
                    if (s.Contains($"\"{sir}\"", StringComparison.Ordinal))
                        bulgular.Add($"{ad}  ->  {s}");
                }
            }
        }

        Assert.True(
            bulgular.Count == 0,
            "TARAYICIDA GÖMÜLÜ SIR ADI:\n  " +
            string.Join("\n  ", bulgular) +
            $"\n\nAdlar yalnız `{ListeYolu}` içinde yaşar. İki liste " +
            "zamanla ayrışır ve ayrışan her nokta, birinin sınamadığı " +
            "bir noktadır.");
    }
}
