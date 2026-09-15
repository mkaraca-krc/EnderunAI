using System.Text.RegularExpressions;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// ÇIPLAK GLOB SAYIMI MUHAFIZI — KURAL 84'ÜN ARACA DÖNMÜŞ HÂLİ.
///
/// ═══ NEDEN VAR ═══
///
/// `ls Migrations/*.cs | wc -l` bir kez 207 dedi; gerçek 214'tü.
/// `Migrations/HumanResources/` altındaki 7 göç glob'un altına
/// düşmüyordu ve sonuç "canlıda kodda olmayan göç var" diye YANLIŞ BİR
/// ALARM oldu. Aynı sınıf hata `sorgu-dizgesi-cirasi.sh`te de vardı:
/// `ls access.log* | wc -l` SAYIYORDU ama betik başka bir kümeyi
/// OKUYORDU — kapsamını fazla beyan eden ölçüm, eksik ölçtüğünü gizler.
///
/// Üçüncü kez tekrarlayan hata disiplinle değil ARAÇLA kapatılır.
/// Araç `deploy/scripts/say.sh`; bu test aracın atlanmasını engeller.
///
/// ═══ NE YASAK, NE SERBEST ═══
///
/// YASAK  : `ls &lt;glob&gt; | wc -l` — dosya sayısını glob'la okumak.
/// SERBEST: `printf '%s\n' "$degisken" | grep -c .` — bu AKIŞTAKİ SATIRI
///          sayar, dosya taramaz; say.sh'ın cevapladığı soru değildir.
///          Bunları da çevirmek araç taşımacılığı olurdu.
///
/// Meşru bir istisna çıkarsa AŞAĞIYA ADIYLA ve SEBEBİYLE yazılır.
/// Sessiz atlama yoktur; ölü istisna da bırakılmaz.
/// </summary>
public sealed class CiplakGlobSayimiTests
{
    private static readonly Dictionary<string, string> GerekceliIstisnalar = new();

    private static readonly Regex CiplakGlob =
        new(@"\bls\s+[^|;&]*\|\s*wc\s+-l", RegexOptions.Compiled);

    private static string DepoKoku()
    {
        var dizin = new DirectoryInfo(AppContext.BaseDirectory);
        while (dizin is not null && !Directory.Exists(Path.Combine(dizin.FullName, "deploy")))
        {
            dizin = dizin.Parent;
        }

        return dizin?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    private static List<string> Betikler()
    {
        var kok = DepoKoku();
        var bulunan = new List<string>();

        foreach (var altDizin in new[] { "deploy/scripts", "scripts" })
        {
            var yol = Path.Combine(kok, altDizin);
            if (!Directory.Exists(yol)) continue;
            bulunan.AddRange(Directory.GetFiles(yol, "*.sh", SearchOption.AllDirectories));
        }

        return bulunan;
    }

    [Fact]
    public void TaramaSagligi_BetikBulundu_BosKumeKanitDegil()
    {
        // Kural 48: bu satır olmadan aşağıdaki test boş kümede yeşil yanar.
        var betikler = Betikler();
        Assert.True(
            betikler.Count >= 20,
            $"Taranan betik sayısı beklenenden az: {betikler.Count}. Kök yanlış olabilir.");
    }

    [Fact]
    public void SondaIsiriyor_OrnekDizgeYakalaniyor()
    {
        // Muhafızın kendi pozitif kontrolü: desen ısırabildiğini gösterir.
        Assert.Matches(CiplakGlob, "SAYI=$(ls \"$D\"/access.log* 2>/dev/null | wc -l)");
        // Ve akıştaki satır sayımını YAKALAMAZ (yanlış kırmızı yakmasın).
        Assert.DoesNotMatch(CiplakGlob, "adet=\"$(printf '%s\\n' \"$x\" | grep -c . || true)\"");
    }

    [Fact]
    public void HicbirBetikte_CiplakGlobSayimi_Olmamali()
    {
        var kok = DepoKoku();
        var ihlaller = new List<string>();

        foreach (var yol in Betikler())
        {
            var kisa = Path.GetRelativePath(kok, yol).Replace('\\', '/');
            if (kisa.EndsWith("say.sh")) continue;                 // aracın kendisi
            if (GerekceliIstisnalar.ContainsKey(kisa)) continue;

            var satirlar = File.ReadAllLines(yol);
            for (var i = 0; i < satirlar.Length; i++)
            {
                var satir = satirlar[i];
                if (satir.TrimStart().StartsWith('#')) continue;   // yorum değil kod aranıyor
                if (CiplakGlob.IsMatch(satir))
                    ihlaller.Add($"{kisa}:{i + 1}  {satir.Trim()}");
            }
        }

        Assert.True(
            ihlaller.Count == 0,
            "Çıplak glob dosya sayımı bulundu. `deploy/scripts/say.sh` kullanın "
            + "ya da okuduğunuz kümeyi TEK listeden besleyin:\n  "
            + string.Join("\n  ", ihlaller));
    }

    [Fact]
    public void OluIstisnaBirakilmaz()
    {
        var kok = DepoKoku();
        foreach (var kisa in GerekceliIstisnalar.Keys)
        {
            Assert.True(
                File.Exists(Path.Combine(kok, kisa)),
                $"İstisna artık geçersiz (dosya yok): {kisa}");
        }
    }
}
