using System.Text.RegularExpressions;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// DOĞRUDAN psql ÇAĞRISI ÇIRCIRI — Y3.
///
/// ═══ NEDEN ═══
///
/// Yanlış veritabanını ölçmek İKİ KEZ oldu (2026-09-07 ve 08):
/// bir kez yanlış tablo, bir kez `psql -d "$DB"` çağrısında $DB
/// boş olduğu için psql'in sessizce `postgres` veritabanına
/// bağlanması. İkincisinde "0/100 eşleşme" sonucu bir an gerçek
/// sanıldı.
///
/// Çözüm bir uyarı değil, bir ARAÇ: `deploy/scripts/vt-sorgu.sh`
/// veritabanı adını zorunlu kılıyor, bakım veritabanlarını
/// reddediyor ve her çıktının başına psql'in kendi bildirdiği
/// `current_database()` ile satır sayısını basıyor.
///
/// ═══ NEDEN ÇIRCIR, NEDEN YASAK DEĞİL ═══
///
/// 25 doğrudan çağrı var ve hepsi işlevsel: yedek alıyor, göç
/// uyguluyor, rig kuruyor, kapı koşuyor. Bunları bir gecede
/// dolaştırmak gerçekçi değil ve gereği de yok — sorun ÖLÇÜM
/// çağrılarındaydı.
///
/// Ama bugün dolaştırılmayan bir çağrı, yarın YENİSİNİN
/// eklenmesine mazeret olamaz. Çizgi bugünkü durumu donduruyor ve
/// YALNIZCA KÜÇÜLEBİLİR.
/// </summary>
public sealed class PsqlCizgisiTests
{
    private const string CizgiDosyasi = "deploy/psql-cizgisi.txt";

    /// <summary>
    /// PSQL'İ SAHİPLENEN ARAÇLAR VE GEREKÇELERİ.
    ///
    /// ═══ NEDEN ÇİZGİYE SATIR EKLEMİYORUZ ═══
    ///
    /// Çizginin başlığı açık: "LİSTE YALNIZCA KÜÇÜLÜR." Bir aracı
    /// çizgiye yazmak o kuralı çiğnerdi ve bir daha kimse kuralı
    /// ciddiye almazdı.
    ///
    /// Ama ARAÇ ile ÇAĞIRICI ayrı şeyler. Çizgi, "psql'i doğrudan
    /// çağıran işlevsel betikler" borcunu sayıyor. Bu sözlükteki
    /// dosyalar borç değil, borcun ÖDENDİĞİ yer: psql'i onlar
    /// sahiplenir ki başkası doğrudan çağırmasın.
    ///
    /// Gerekçe ZORUNLU — gerekçesiz bir araç, sessiz bir istisnadır.
    /// (Aynı disiplin: CoverageBaselineTests.Istisnalar.)
    /// </summary>
    private static readonly Dictionary<string, string> Araclar = new()
    {
        ["deploy/scripts/vt-sorgu.sh"] =
            "ÖLÇÜM YOLU (Y3). Veritabanı adını zorunlu kılar, bakım " +
            "veritabanlarını reddeder, her çıktının başına " +
            "current_database() ve satır sayısını basar. Yanlış " +
            "veritabanını ölçmek iki kez olduğu için var.",

        ["deploy/scripts/prova-zemini.sh"] =
            "PROVA ZEMİNİ KURMA YOLU (PZ2). `vt-sorgu.sh`ten GEÇEMEZ: " +
            "CREATE/DROP DATABASE ve REVOKE CONNECT bakım " +
            "veritabanına bağlanmayı gerektirir ve vt-sorgu bunu " +
            "TASARIM GEREĞİ reddeder. İkisi farklı işler — biri " +
            "var olan veritabanını ölçer, öteki zemin yaratır.",
    };

    private static readonly string[] AtlanacakDizinler =
    [
        "node_modules", ".git", ".next", "obj", "bin",
        "publish", "publish-eski", "publish-yeni", "publish-rollback", "backups"
    ];

    [Fact]
    public void YeniDogrudanPsqlCagrisi_Eklenmemis()
    {
        var suanki = Tara();
        var cizgi = OkuCizgi();

        var yeni = suanki
            .Where(x => !cizgi.ContainsKey(x.Key) || x.Value > cizgi[x.Key])
            .Select(x => $"{x.Key}: {x.Value} çağrı (çizgi {(cizgi.TryGetValue(x.Key, out var c) ? c : 0)})")
            .ToList();

        Assert.True(
            yeni.Count == 0,
            "YENİ DOĞRUDAN psql ÇAĞRISI:\n  " + string.Join("\n  ", yeni) +
            "\n\nÖlçüm için deploy/scripts/vt-sorgu.sh kullanın: veritabanı adını zorunlu kılar, " +
            "bakım veritabanlarını reddeder ve current_database() ile satır " +
            "sayısını her çıktının başına basar. İşlevsel bir çağrıysa " +
            $"{CizgiDosyasi} dosyasını GEREKÇESİYLE güncelleyin.");
    }

    /// <summary>
    /// Çizgi GERÇEĞİ yansıtmalı: dolaştırılan çağrılar listede
    /// kalırsa dosya şişer ve araya sessizce yenisi eklenebilir.
    /// </summary>
    [Fact]
    public void Cizgi_OlmayanCagriTasimamali()
    {
        var suanki = Tara();
        var cizgi = OkuCizgi();

        var fazla = cizgi
            .Where(x => !suanki.ContainsKey(x.Key) || suanki[x.Key] < x.Value)
            .Select(x => $"{x.Key}: çizgide {x.Value}, gerçekte {(suanki.TryGetValue(x.Key, out var s) ? s : 0)}")
            .ToList();

        Assert.True(
            fazla.Count == 0,
            $"Bu çağrılar azalmış ya da silinmiş ama {CizgiDosyasi} hâlâ " +
            "eskisini yazıyor:\n  " + string.Join("\n  ", fazla) +
            "\n\nDosyayı güncelleyin — borcun GERÇEK boyutunu göstermeli.");
    }

    /// <summary>
    /// POZİTİF KONTROL (Kural 48): tarayıcı gerçekten psql buluyor mu.
    /// Hiçbir şey bulmadığı için yeşil olan bir çıra ile, ihlal
    /// olmadığı için yeşil olan bir çıra aynı görünür.
    /// </summary>
    [Fact]
    public void Tarayici_BilinenBirCagriyiBulabiliyor()
    {
        var suanki = Tara();

        Assert.True(
            suanki.Count > 0,
            "Tarayıcı hiç psql çağrısı bulamadı. 25 tane olduğu " +
            "ölçüldü (2026-09-08) — tarayıcı yanlış yere bakıyor.");

        Assert.True(
            suanki.ContainsKey("scripts/enderun-backup.sh"),
            "Bilinen çağrı bulunamadı: scripts/enderun-backup.sh");
    }

    /// <summary>Araçlar yerinde mi — çizgi onlara yönlendiriyor.</summary>
    [Fact]
    public void Araclar_Mevcut()
    {
        foreach (var (yol, _) in Araclar)
        {
            Assert.True(
                File.Exists(Path.Combine(DepoKoku(), yol)),
                $"Araç yok: {yol}. Çizgi var olmayan bir araca yönlendiremez.");
        }
    }

    /// <summary>
    /// GEREKÇESİZ ARAÇ OLAMAZ — sessiz bir istisna, karardır.
    /// </summary>
    [Fact]
    public void Araclar_GerekcesizOlamaz()
    {
        foreach (var (yol, gerekce) in Araclar)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(gerekce),
                $"{yol} araç listesinde ama gerekçesi yok.");
        }
    }

    // ---------------------------------------------------------------

    private static Dictionary<string, int> Tara()
    {
        var kok = DepoKoku();
        var sonuc = new Dictionary<string, int>(StringComparer.Ordinal);

        // DİZİN BUDANARAK GEZİLİYOR, SONRA SÜZÜLEREK DEĞİL.
        // `EnumerateFiles(AllDirectories)` node_modules'ü de gezip
        // yüz binlerce dosya okurdu; budama hem hızlı hem de izin
        // hatası olan dizinlerde patlamıyor.
        foreach (var dosya in Gez(kok, kok))
        {
            var goreli = Path.GetRelativePath(kok, dosya).Replace('\\', '/');

            if (Araclar.ContainsKey(goreli)) continue;

            var adet = File.ReadLines(dosya)
                .Where(s => !s.TrimStart().StartsWith('#'))
                .Sum(s => Regex.Matches(s, @"\bpsql\b").Count);

            if (adet > 0) sonuc[goreli] = adet;
        }

        return sonuc;
    }

    /// <summary>Budayarak gezer; yalnız .sh ve .py döner.</summary>
    private static IEnumerable<string> Gez(string kok, string dizin)
    {
        foreach (var dosya in Directory.EnumerateFiles(dizin))
        {
            if (dosya.EndsWith(".sh", StringComparison.Ordinal) ||
                dosya.EndsWith(".py", StringComparison.Ordinal))
                yield return dosya;
        }

        foreach (var alt in Directory.EnumerateDirectories(dizin))
        {
            var ad = Path.GetFileName(alt);

            if (AtlanacakDizinler.Contains(ad)) continue;
            if (ad.StartsWith(".next", StringComparison.Ordinal)) continue;

            foreach (var dosya in Gez(kok, alt)) yield return dosya;
        }
    }

    private static Dictionary<string, int> OkuCizgi()
    {
        var yol = Path.Combine(DepoKoku(), CizgiDosyasi);
        Assert.True(File.Exists(yol), $"Çizgi dosyası yok: {yol}");

        return File.ReadAllLines(yol)
            .Where(s => !string.IsNullOrWhiteSpace(s) && !s.TrimStart().StartsWith('#'))
            .Select(s => s.Split(':'))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim(), p => int.Parse(p[1].Trim()), StringComparer.Ordinal);
    }

    private static string DepoKoku()
    {
        var dizin = new DirectoryInfo(AppContext.BaseDirectory);

        while (dizin is not null && !Directory.Exists(Path.Combine(dizin.FullName, ".git")))
            dizin = dizin.Parent;

        return dizin?.FullName
            ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }
}
