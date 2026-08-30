using System.Text.RegularExpressions;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// YETİM MUHAFIZ BEKÇİSİ — yazılmış ama ÇAĞRILMAMIŞ koruma.
///
/// BU KIRMIZIYA DÖNERSE: adı geçen fonksiyon bir koruma gibi duruyor
/// ama hiçbir üretim yolundan çağrılmıyor. Yani koruma YOK; kod
/// okuyan biri var sanıyor. En sinsi hâli, testin doğrudan çağırıp
/// yeşil vermesidir — o yüzden test dosyalarından yapılan çağrılar
/// SAYILMIYOR.
///
/// KAYNAK: 2026-08-30, HP/1. Aynı hata AYNI GÜN İKİ KEZ yapıldı:
///   - `UstHesapVarOlmaliAsync` / `UstHesabinHareketiOlmamaliAsync`
///     yazıldı, `CreateAsync`e bağlanmadı (elle fark edildi)
///   - `KayitSurumu.Dogrula` yazıldı, `UpdateAsync`e bağlanmadı
///     (yalnız test yakaladı)
///
/// ───────────────────────────────────────────────────────────────
/// DÜRÜST SINIR
/// ───────────────────────────────────────────────────────────────
///
/// Bu bekçi ADLANDIRMA DESENİNE dayanır; desene uymayan bir muhafız
/// KAÇAR (Kural 58). Kapsamı "tüm muhafızlar" değil, "deseni tutan
/// muhafızlar"dır.
///
/// Desen depodan ölçülerek çıkarıldı, uydurulmadı: `Validate` 42
/// ayrı ad, `Ensure` 12, `Guard` 4, `Kontrol` 3, `Dogrula` 2,
/// `Olmali` ailesi 2.
/// </summary>
public sealed class YetimMuhafizTests
{
    private static readonly string[] Desenler =
    [
        "Validate", "Ensure", "Guard", "Kontrol",
        "Dogrula", "Olmali", "Gerekli"
    ];

    /// <summary>
    /// Muafiyet: arayüz/soyut bildirimi tek başına çağrı sayılmaz ama
    /// gerçek uygulamayı da yetim göstermemeli. Gerekçesi yazılı
    /// muafiyet dışında liste boş kalmalı.
    /// </summary>
    private static readonly Dictionary<string, string> Muafiyetler = new();

    private static string UretimKoku()
    {
        var dizin = AppContext.BaseDirectory;

        while (dizin is not null &&
               !Directory.Exists(Path.Combine(dizin, "backend", "EnderunAI.Api")))
        {
            dizin = Directory.GetParent(dizin)?.FullName;
        }

        Assert.NotNull(dizin);
        return Path.Combine(dizin!, "backend", "EnderunAI.Api");
    }

    private static string[] UretimDosyalari() =>
        [.. Directory
            .EnumerateFiles(UretimKoku(), "*.cs", SearchOption.AllDirectories)
            .Where(x => !x.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}")
                     && !x.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !x.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))];

    /// <summary>
    /// TARAMA BOŞA DÜŞMÜYOR.
    ///
    /// Kök bulunamazsa ya da dosya listesi boşalırsa "yetim yok"
    /// testi sessizce yeşil kalırdı — boş küme her iddiayı doğrular
    /// (Kural 48).
    /// </summary>
    [Fact]
    public void Tarama_BosaDusmuyor()
    {
        var dosyalar = UretimDosyalari();

        Assert.True(dosyalar.Length > 300,
            $"Üretim dosyası sayısı beklenenden az: {dosyalar.Length}");

        var adlar = MuhafizAdlari(dosyalar);

        Assert.True(adlar.Count > 30,
            $"Desene uyan muhafız sayısı beklenenden az: {adlar.Count}. "
            + "Desen bozulmuş olabilir.");
    }

    private static Dictionary<string, string> MuhafizAdlari(string[] dosyalar)
    {
        /*
         * SINIF/KAYIT/ARAYÜZ ADLARI ELENİYOR.
         *
         * `public sealed class RehireGuardService(` birincil kurucu
         * yüzünden metot tanımına benziyordu ve bekçi onu YETİM
         * sandı — oysa DI'da kayıtlı ve controller'a enjekte
         * ediliyor. Yanlış alarm üreten bekçi, çıktısını ciddiye
         * almamayı öğretir (Kural 47).
         */
        var tanim = new Regex(
            @"(?:private|internal|public|protected)(?![^;=\r\n]*\b(?:class|record|struct|interface|enum)\b)"
            + @"[^;=\r\n]*?\b(\w*(?:"
            + string.Join("|", Desenler)
            + @")\w*)\s*\(",
            RegexOptions.Compiled);

        var bulunan = new Dictionary<string, string>();

        foreach (var dosya in dosyalar)
        {
            foreach (Match m in tanim.Matches(File.ReadAllText(dosya)))
            {
                var ad = m.Groups[1].Value;

                // Tip adları ve kurucu benzeri eşleşmeler elenir.
                if (ad.Length < 5) continue;

                bulunan.TryAdd(ad, Path.GetFileName(dosya));
            }
        }

        return bulunan;
    }

    /// <summary>
    /// HER MUHAFIZIN ÜRETİMDE EN AZ BİR ÇAĞRI YERİ VAR.
    ///
    /// BU KIRMIZIYA DÖNERSE: adı bildirilen koruma hiçbir yerden
    /// çalışmıyor. Kod okuyan "burada kontrol var" sanır; yoktur.
    ///
    /// TEST ÇAĞRILARI SAYILMAZ — yalnız testin çağırdığı bir muhafız
    /// yeşil geçerdi ve bu, 2026-08-30'da yaşanan hatanın ta kendisi.
    /// </summary>
    [Fact]
    public void HerMuhafizin_UretimdeCagriYeriVar()
    {
        var dosyalar = UretimDosyalari();
        var adlar = MuhafizAdlari(dosyalar);

        var metinler = dosyalar.Select(File.ReadAllText).ToArray();
        var yetimler = new List<string>();

        foreach (var (ad, dosya) in adlar)
        {
            if (Muafiyetler.ContainsKey(ad)) continue;

            /*
             * PARANTEZ ARANMAZ — METOT GRUBU DA KULLANIMDIR.
             *
             * `.Where(OdemePlaniKurallari.KapanisSebebiGerekliMi)`
             * gerçek bir kullanım ama adın ardında parantez YOK.
             * `Ad(` aransaydı bekçi onu yetim sanardı — nitekim ilk
             * koşuda sandı.
             */
            var kalip = new Regex($@"\b{Regex.Escape(ad)}\b");
            var toplam = metinler.Sum(x => kalip.Matches(x).Count);

            // 1 = yalnız tanımın kendisi. En az bir kullanım için ≥2.
            if (toplam < 2)
                yetimler.Add($"{ad}  ({dosya})");
        }

        Assert.True(
            yetimler.Count == 0,
            "ÜRETİMDE ÇAĞRISI OLMAYAN MUHAFIZ(LAR):\n"
            + string.Join("\n", yetimler.OrderBy(x => x, StringComparer.Ordinal))
            + "\n\nBir muhafız yazılıp bağlanmazsa koruma YOKTUR, ama kod "
            + "okuyan VAR sanır. Ya çağrı yerine bağlayın ya da silin.\n"
            + "Testten çağrılması SAYILMAZ: yalnız testin çağırdığı bir "
            + "muhafız üretimde hiçbir şey korumaz.");
    }
}
