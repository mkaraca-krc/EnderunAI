using System.Text.RegularExpressions;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// `pkill -f` YASAK — TEKRARLAYAN İNSAN HATASI ARAÇLA ÇÖZÜLÜR (Y2).
///
/// ═══ ÖLÇÜLEN OLAY ═══
///
/// `pkill -f &lt;desen&gt;` bir oturumda DÖRT KEZ çağıran kabuğu öldürdü
/// (çıkış 144). Sebep her seferinde aynı: desen, çağıran kabuğun
/// KENDİ komut satırında da geçiyor ve `pkill` onu da eşleştiriyor.
/// Bir kez bir düzenlemenin yarıda kalmasına ve dosyanın eksik
/// yazılmasına yol açtı.
///
/// ═══ NEDEN MUHAFIZ, NEDEN "DİKKAT EDECEĞİM" DEĞİL ═══
///
/// Dört kez tekrarlanan bir hata, dikkat sorunu değil araç
/// sorunudur. Yerine `deploy/scripts/surec-durdur.sh` var: kendini
/// ve atasını asla öldürmüyor.
///
/// ═══ MUAFİYET YOK — VE BU BİLEREK ═══
///
/// Meşru bir `pkill -f` kullanımı düşünemiyorum; çıkarsa muafiyet
/// listesi değil, aracın eksiği tartışılmalı.
/// </summary>
public sealed class PkillYasagiTests
{
    private static string DepoKoku()
    {
        var dizin = new DirectoryInfo(AppContext.BaseDirectory);

        while (dizin is not null &&
               !Directory.Exists(Path.Combine(dizin.FullName, "deploy")))
        {
            dizin = dizin.Parent;
        }

        return dizin?.FullName
            ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    private static IReadOnlyList<string> BetikDosyalari()
    {
        var kok = DepoKoku();

        return new[] { "deploy", "scripts" }
            .Select(alt => Path.Combine(kok, alt))
            .Where(Directory.Exists)
            .SelectMany(dizin =>
                Directory.EnumerateFiles(dizin, "*.sh", SearchOption.AllDirectories))
            .ToList();
    }

    /// <summary>Yorumlar atılıyor — kapı kendi gerekçesini ihlal saymasın.</summary>
    private static string YorumsuzGovde(string metin) =>
        string.Join("\n", metin.Split('\n')
            .Select(satir => Regex.Replace(satir, @"#.*$", string.Empty)));

    /// <summary>İDDİA: hiçbir betik `pkill -f` kullanmıyor.</summary>
    [Fact]
    public void HicbirBetik_PkillFKullanmiyor()
    {
        var ihlaller = new List<string>();

        foreach (var yol in BetikDosyalari())
        {
            var govde = YorumsuzGovde(File.ReadAllText(yol));

            if (Regex.IsMatch(govde, @"\bpkill\s+(-\w+\s+)*-\w*f"))
                ihlaller.Add(Path.GetFileName(yol));
        }

        Assert.True(
            ihlaller.Count == 0,
            "`pkill -f` kullanan betik(ler): " + string.Join(", ", ihlaller)
            + ". Yerine deploy/scripts/surec-durdur.sh kullanın — o kendini "
            + "ve atasını öldürmüyor.");
    }

    /// <summary>
    /// POZİTİF KONTROL — MUHAFIZ GERÇEKTEN OKUYOR VE ISIRIYOR.
    ///
    /// Üstteki test hiçbir dosya okunmasaydı da yeşil kalırdı
    /// (Kural 48). Üç ayak: yüzey gerçekten taranıyor, yasak desen
    /// sahte ihlali yakalıyor, meşru kullanım yanlış alarm vermiyor.
    /// </summary>
    [Fact]
    public void Muhafiz_Isiriyor()
    {
        Assert.True(
            BetikDosyalari().Count > 10,
            $"Taranan betik sayısı beklenenden az ({BetikDosyalari().Count}).");

        Assert.Matches(@"\bpkill\s+(-\w+\s+)*-\w*f",
            YorumsuzGovde("pkill -f \"next start\""));
        Assert.Matches(@"\bpkill\s+(-\w+\s+)*-\w*f",
            YorumsuzGovde("pkill -9 -f something"));

        // `pgrep -f` MEŞRU: okuma, öldürme değil.
        Assert.DoesNotMatch(@"\bpkill\s+(-\w+\s+)*-\w*f",
            YorumsuzGovde("pgrep -f \"next start\""));

        // Yorumdaki bahis ihlal sayılmıyor.
        Assert.DoesNotMatch(@"\bpkill\s+(-\w+\s+)*-\w*f",
            YorumsuzGovde("# pkill -f kullanmayin"));
    }

    /// <summary>
    /// YERİNE KONAN ARAÇ GERÇEKTEN VAR VE KENDİNİ DIŞLIYOR.
    ///
    /// Yasak tek başına bir çözüm değil: alternatifi olmayan bir
    /// yasak, insanı yasağı delmeye iter.
    /// </summary>
    [Fact]
    public void Alternatif_VarVeKendiniDisliyor()
    {
        var yol = Path.Combine(DepoKoku(), "deploy", "scripts", "surec-durdur.sh");

        Assert.True(File.Exists(yol), "surec-durdur.sh yok.");

        var govde = File.ReadAllText(yol);

        Assert.Contains("KENDI", govde, StringComparison.Ordinal);
        Assert.Contains("ATA", govde, StringComparison.Ordinal);
        Assert.Contains("pgrep", govde, StringComparison.Ordinal);
    }
}
