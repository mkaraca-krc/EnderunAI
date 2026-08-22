using System.Text.RegularExpressions;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// KAPSAM AÇIĞI CIRCIRI — yeni açık eklenmesini durdurur.
///
/// SORUN: şirket taşıyan 96 varlık var ve kapsam süzgeci (ApplyScope)
/// yalnız satın alma ailesinde uygulanıyordu. Ölçüldü: 462 okumanın
/// 439'unda kapsam yok. Bu yüzden ek ücret (maaş bilgisi) ucu
/// `companyId` gönderilmediğinde bütün şirketlerin kayıtlarını
/// döndürüyordu ve kimse fark etmedi.
///
/// NEDEN CIRCIR: 439 okumanın hepsini tek pakette gerekçelendirmek
/// gerçekçi değil. Ama bugün düzeltilmeyen bir açık, yarın YENİSİNİN
/// eklenmesine mazeret olamaz. Bu test bugünkü durumu TEMEL ÇİZGİ
/// olarak dondurur:
///   (a) listede olmayan yeni bir kapsamsız okuma eklenirse düşer,
///   (b) toplam sayı artarsa düşer.
///
/// Temel çizgi yalnızca KÜÇÜLEBİLİR. Her G3 paketi kapattığı satırları
/// `kapsam-temel-cizgi.txt` dosyasından siler ve dosya küçülür.
///
/// GEREKÇELİ İSTİSNALAR AYRI: temel çizgi "henüz düzeltilmedi" demek;
/// istisna listesi "düzeltilmeyecek ve sebebi şu" demek. İkisi
/// karıştırılırsa borç, karara dönüşür ve kimse geri dönmez.
/// </summary>
public sealed class CoverageBaselineTests
{
    /// <summary>
    /// KAPSAM SÜZGECİ GEREKMEYEN okumalar ve GEREKÇELERİ.
    /// Gerekçe zorunlu: boş bırakılan bir istisna, sessiz bir karardır.
    /// </summary>
    private static readonly Dictionary<string, string> Istisnalar = new()
    {
        ["Services/DocumentNumbers/DocumentNumberService.cs"] =
            "BELGE NUMARASI ÜRETİMİ. Şirket kimliği çağıranın verdiği " +
            "parametreden geliyor ve satır zaten o şirkete yazılıyor; " +
            "okuma bir listeleme değil, sıradaki numarayı bulma.",
    };

    [Fact]
    public void KapsamsizOkumaSayisi_ArtmamisOlmali()
    {
        var kok = BulKok();
        var suanki = TaraKapsamsizOkumalar(kok);
        var temel = OkuTemelCizgi(kok);

        var yeni = suanki.Except(temel).ToList();

        Assert.True(
            yeni.Count == 0,
            "KAPSAM SÜZGECİ OLMAYAN YENİ OKUMA eklenmiş:\n  " +
            string.Join("\n  ", yeni) +
            "\n\nŞirket taşıyan bir varlığı okurken kapsam süzgeci " +
            "uygulayın (ApplyScope). Gerçekten gerekmiyorsa " +
            "CoverageBaselineTests içindeki İstisnalar listesine " +
            "GEREKÇESİYLE ekleyin. Bu liste yalnızca küçülür.");

        Assert.True(
            suanki.Count <= temel.Count,
            $"Kapsamsız okuma sayısı {suanki.Count}, temel çizgi " +
            $"{temel.Count}. Sayı artamaz.");
    }

    /// <summary>
    /// Temel çizgi GERÇEĞİ yansıtmalı: düzeltilen satırlar silinmezse
    /// dosya şişer ve cırcır dişlerini kaybeder — araya sessizce
    /// yenisi eklenebilir.
    /// </summary>
    [Fact]
    public void TemelCizgi_OlmayanSatirTasimamali()
    {
        var kok = BulKok();
        var suanki = TaraKapsamsizOkumalar(kok);
        var temel = OkuTemelCizgi(kok);

        var kapanmis = temel.Except(suanki).ToList();

        Assert.True(
            kapanmis.Count == 0,
            "Bu satırlar artık kapsamlı (ya da silinmiş) ama temel " +
            "çizgide duruyor:\n  " + string.Join("\n  ", kapanmis) +
            "\n\nkapsam-temel-cizgi.txt dosyasından silin — dosya " +
            "borcun GERÇEK boyutunu göstermeli.");
    }

    [Fact]
    public void Istisnalar_GerekcesizOlamaz()
    {
        foreach (var (yol, gerekce) in Istisnalar)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(gerekce),
                $"{yol} istisna listesinde ama gerekçesi yok. " +
                "Gerekçesiz istisna, sessiz bir karardır.");
        }
    }

    // ---------------------------------------------------------------

    private static List<string> OkuTemelCizgi(string kok)
    {
        var yol = Path.Combine(kok, "EnderunAI.Api.Tests", "kapsam-temel-cizgi.txt");

        return File.ReadAllLines(yol)
            .Where(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith('#'))
            .Select(x => x.Trim())
            .ToList();
    }

    private static List<string> TaraKapsamsizOkumalar(string kok)
    {
        var api = Path.Combine(kok, "EnderunAI.Api");
        var dbset = DbSetHaritasi(api);
        var sirketli = SirketTasiyanVarliklar(api);

        var sonuc = new List<string>();

        foreach (var altDizin in new[] { "Controllers", "Services" })
        {
            var tam = Path.Combine(api, altDizin);
            if (!Directory.Exists(tam)) continue;

            foreach (var dosya in Directory
                         .EnumerateFiles(tam, "*.cs", SearchOption.AllDirectories)
                         .OrderBy(x => x, StringComparer.Ordinal))
            {
                var goreli = Path.GetRelativePath(api, dosya).Replace('\\', '/');

                if (Istisnalar.ContainsKey(goreli)) continue;

                var kod = YorumlariAt(File.ReadAllText(dosya));

                foreach (Match m in Regex.Matches(
                             kod, @"db(?:Context)?\.(\w+)\s*\.\s*AsNoTracking\(\)"))
                {
                    var ad = m.Groups[1].Value;

                    if (!dbset.TryGetValue(ad, out var varlik)) continue;
                    if (!sirketli.Contains(varlik)) continue;

                    var pencere = kod.Substring(
                        m.Index, Math.Min(400, kod.Length - m.Index));

                    if (pencere.Contains("ApplyScope")) continue;

                    var satir = kod[..m.Index].Count(c => c == '\n') + 1;
                    sonuc.Add($"{goreli}:{satir}:{ad}");
                }
            }
        }

        sonuc.Sort(StringComparer.Ordinal);
        return sonuc;
    }

    private static Dictionary<string, string> DbSetHaritasi(string api)
    {
        var ctx = File.ReadAllText(Path.Combine(api, "Data", "AppDbContext.cs"));

        /*
         * NİTELİKLİ AD DA YAKALANIR — BEKÇİNİN KÖR NOKTASIYDI.
         *
         * İlk sürüm `DbSet<(\w+)>` arıyordu. `\w` NOKTA İÇERMEZ, yani
         * `DbSet<Models.Expenses.ExpenseEntry> ExpenseEntries` biçiminde
         * yazılmış 22 tablo haritaya HİÇ girmiyordu. Bekçi onları
         * görmediği için o tablolardaki 40 kapsamsız okuma temel
         * çizgide de yoktu: borç 418 görünüyordu, gerçekte 458'di.
         *
         * Kaçanların arasında PARA tabloları vardı — ExpenseEntries,
         * BankLoans, BankLoanInstallments, CreditCards, PartnerAccounts.
         * Yani bekçi tam da korumakla görevli olduğu yeri saymıyordu.
         *
         * Varlık adı son parçadan alınıyor: `Models.Expenses.ExpenseEntry`
         * → `ExpenseEntry`. Model taraması sınıf adıyla çalışıyor.
         */
        return Regex.Matches(ctx, @"DbSet<([\w.]+)>\s+(\w+)\s*=>")
            .ToDictionary(
                m => m.Groups[2].Value,
                m => m.Groups[1].Value.Split('.')[^1]);
    }

    private static HashSet<string> SirketTasiyanVarliklar(string api)
    {
        var sonuc = new HashSet<string>(StringComparer.Ordinal);

        foreach (var dosya in Directory.EnumerateFiles(
                     Path.Combine(api, "Models"), "*.cs", SearchOption.AllDirectories))
        {
            var metin = File.ReadAllText(dosya);

            foreach (Match m in Regex.Matches(metin, @"class (\w+)\s*:\s*BaseEntity"))
            {
                var kuyruk = metin[m.Index..Math.Min(m.Index + 3000, metin.Length)];
                if (kuyruk.Contains("CompanyId")) sonuc.Add(m.Groups[1].Value);
            }
        }

        return sonuc;
    }

    private static string YorumlariAt(string kaynak)
    {
        static string Bosalt(Match m) => Regex.Replace(m.Value, @"[^\n]", " ");

        var bloksuz = Regex.Replace(kaynak, @"/\*[\s\S]*?\*/", Bosalt);
        return Regex.Replace(bloksuz, @"//[^\n]*", Bosalt);
    }

    private static string BulKok()
    {
        var dizin = new DirectoryInfo(AppContext.BaseDirectory);

        while (dizin is not null &&
               !Directory.Exists(Path.Combine(dizin.FullName, "EnderunAI.Api")))
        {
            dizin = dizin.Parent;
        }

        Assert.NotNull(dizin);
        return dizin!.FullName;
    }
}
