using System.Text.RegularExpressions;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// "YEDEK ALINDI" SATIRI KİMİN AĞZINDAN ÇIKIYOR — NÖBET.
///
/// SORUNUN KAYNAĞI (Mehmet, 2026-09-05): *"AYNI SÖZCÜK İKİ AYRI ŞEY...
/// ben sekiz kez onay verirken hangisini okuduğumu bilmiyordum."*
/// safe-deploy günlüğünde "yedek" diyen dört satır vardı ve ikisi
/// tamamen farklı şeyleri anlatıyordu:
///
///   · SÜRÜM YEDEĞİ  — derlenmiş çıktının kopyası. Veri İÇERMEZ.
///                     Yayın bozulursa eski ikiliyi geri koyar.
///   · VERİ YEDEĞİ   — pg_dump + uploads + proje dosyaları, şifreli.
///                     Veritabanı silinirse onu geri getiren tek şey.
///
/// Onay veren kişi için bu ikisi arasındaki fark, "yayın geri alınabilir"
/// ile "veri geri getirilebilir" arasındaki farktır. Günlük satırı bunu
/// söylemiyordu.
///
/// BU TESTİN İŞİ: yeni bir "yedek" satırı, konuşanın adını taşımadan
/// eklenemesin. Kabuk betiği testsiz bir yüzey — düzeltme yapıldıktan
/// sonra kimse onu yerinde tutmuyorsa düzeltme değil, bir seferlik
/// temizliktir (aynı disiplin: <see cref="DerlemeKosucuGuardTests"/>).
/// </summary>
public sealed class YedekGunlukKimligiTests
{
    /// <summary>Türkçe büyük/küçük harf tuzağına düşmemek için desenler açıkça yazılıyor.</summary>
    private static readonly string[] YedekSozcukleri =
    [
        "yedek", "Yedek", "YEDEK",
        "yedeğ", "Yedeğ", "YEDEĞ",
        "backup", "Backup", "BACKUP"
    ];

    private static DirectoryInfo RepoKoku()
    {
        var dizin = new DirectoryInfo(AppContext.BaseDirectory);

        while (dizin is not null &&
               !Directory.Exists(Path.Combine(dizin.FullName, "deploy")))
        {
            dizin = dizin.Parent;
        }

        Assert.True(dizin is not null, "Repo kökü bulunamadı.");
        return dizin!;
    }

    private static string[] SafeDeploySatirlari()
    {
        var yol = Path.Combine(RepoKoku().FullName, "deploy", "scripts", "safe-deploy.sh");
        Assert.True(File.Exists(yol), $"safe-deploy.sh yok: {yol}");
        return File.ReadAllLines(yol);
    }

    /// <summary>
    /// YALNIZ GÜNLÜĞE BASILAN METİN — yorumlar değil.
    ///
    /// Yorum satırlarını almıyoruz: bu betiğin yorumları da "yedek"
    /// diyor ve onları saymak nöbetçiyi gürültüye boğardı. Aranan şey
    /// KULLANICININ EKRANDA GÖRDÜĞÜ satır.
    /// </summary>
    private static List<string> YedekDiyenGunlukMesajlari()
    {
        var desen = new Regex("""^\s*log\s+"[A-ZÇĞİÖŞÜ]+"\s+"(?<mesaj>.*)"\s*$""");
        var bulunan = new List<string>();

        foreach (var satir in SafeDeploySatirlari())
        {
            var eslesme = desen.Match(satir);
            if (!eslesme.Success)
            {
                continue;
            }

            var mesaj = eslesme.Groups["mesaj"].Value;
            if (YedekSozcukleri.Any(s => mesaj.Contains(s, StringComparison.Ordinal)))
            {
                bulunan.Add(mesaj);
            }
        }

        return bulunan;
    }

    /// <summary>
    /// POZİTİF KONTROL AYNI TESTİN İÇİNDE (Kural 48).
    ///
    /// Ayıklama bozulursa liste boşalır ve `All` boş listede YEŞİL
    /// döner — yani test hiçbir şey ölçmediği hâlde geçerdi. Bu yüzden
    /// önce "hiç bulundu mu" soruluyor.
    /// </summary>
    [Fact]
    public void YedekDiyenHerGunlukSatiri_KonusaniniAdlandirir()
    {
        var mesajlar = YedekDiyenGunlukMesajlari();

        Assert.True(
            mesajlar.Count >= 3,
            $"POZİTİF KONTROL DÜŞTÜ: safe-deploy.sh'ta 'yedek' diyen günlük satırı "
            + $"beklenenden az bulundu ({mesajlar.Count}). Ayıklama deseni bozulmuş "
            + "olabilir; test bu hâlde hiçbir şey ölçmüyor.");

        var kimliksiz = mesajlar
            .Where(m => !m.Contains('[') || !m.Contains(']'))
            .ToList();

        Assert.True(
            kimliksiz.Count == 0,
            "Şu günlük satırları 'yedek' diyor ama HANGİ yedek olduğunu söylemiyor. "
            + "Köşeli parantez içinde konuşanın adı yazılmalı "
            + "(ör. [enderun-backup.sh] ya da [safe-deploy.sh:backup_current_release]):\n  - "
            + string.Join("\n  - ", kimliksiz));
    }

    /// <summary>
    /// AYNI ADI HERKESE VERMEK DE ÇÖZÜM DEĞİL.
    ///
    /// Bir önceki test tek başına, dört satıra da aynı etiketi
    /// koyarak susturulabilirdi — o zaman satırlar "kimliğini
    /// söylüyor" ama hâlâ birbirinden AYIRT EDİLEMİYOR olurdu.
    /// Asıl istenen ayrım budur.
    /// </summary>
    [Fact]
    public void SurumYedegiIleVeriYedegi_FarkliKonusanlarOlarakGorunur()
    {
        var etiketDeseni = new Regex(@"\[(?<ad>[^\]]+)\]");

        var etiketler = YedekDiyenGunlukMesajlari()
            .SelectMany(m => etiketDeseni.Matches(m).Select(e => e.Groups["ad"].Value))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            etiketler.Count >= 2,
            "Yedek satırlarının hepsi aynı konuşana ait görünüyor: "
            + $"[{string.Join("], [", etiketler)}]. Sürüm yedeği ile veri yedeği "
            + "günlükte ayırt edilebilmeli.");
    }
}
