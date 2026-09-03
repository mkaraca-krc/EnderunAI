using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// SIR BEKÇİSİ — DEPONUN TAMAMI TARANIR, İSTİSNALAR AÇIKÇA YAZILIR.
///
/// ═══ NEDEN VAR ═══
///
/// 2026-08-23'te canlıda O SIRADA GEÇERLİ olan portal tokenı test
/// verisi olarak bir teste yazıldı, commit ve push edildi. Yani bu
/// bekçinin bütün konusu olan hatayı testin kendisi yapmıştı.
///
/// ═══ NEDEN YENİDEN YAZILDI (2026-09-03) ═══
///
/// Bekçinin ilk hâli kapsamını LİSTE ile tanımlıyordu: "EnderunAI.Api
/// ve EnderunAI.Api.Tests altındaki .cs dosyalarını tara". Bu yüzden
/// `deploy/scripts/*.sh` ve `.github/workflows/*.yml` yıllarca
/// TARANMADI — ve bekçi o yüzeyde her zaman yeşil kaldı.
///
/// O yeşil hiçbir şey söylemiyordu. **Boş bir yüzey, boş bir küme
/// gibidir: her iddiayı doğrular, hiçbir şeyi kanıtlamaz.**
///
/// Desen kusurlu değildi: kabuk betiğindeki 44 karakterlik değer
/// bekçinin aradığı biçime UYUYORDU; bir `.cs` dosyasında olsaydı
/// yakalanırdı. Eksik olan KAPSANAN YÜZEYDİ.
///
/// ═══ KAPSAM ARTIK DIŞLAMA İLE TANIMLI ═══
///
/// Her şey taranır; istisnalar aşağıda GEREKÇESİYLE yazılıdır. Böylece
/// yarın eklenecek her yeni dosya türü KORUMALI DOĞAR, korumasız
/// değil. Bu, KAPI/1'in "bilinmeyen tipte kapalı düş" kuralının
/// bekçinin kendi kapsamına uygulanmış hâlidir.
///
/// ═══ İKİ KONTROL, İKİ FARKLI KAPSAM — BİLEREK ═══
///
/// 1. DESEN kontrolü sezgiseldir ve yanlış alarm üretir (npm bütünlük
///    özetleri, EF'in ürettiği uzun kimlikler). Dışlama listesi ONUN
///    içindir.
///
/// 2. GERÇEK SIR kontrolü yapısı gereği yanlış alarm ÜRETEMEZ: bir
///    özet ya tutar ya tutmaz. Bu yüzden onun HİÇBİR DIŞLAMASI YOKTUR
///    — `package-lock.json` ve göç dosyaları dahil her şey taranır.
///    Bir sır, en çok gözden kaçan dosyaya sızar.
/// </summary>
public sealed class SecretInSourceGuardTests
{
    // ─────────── DIŞLAMALAR — HER BİRİ GEREKÇELİ ───────────

    /// <summary>
    /// Taranmayan dizinler. GEREKÇESİZ İSTİSNA SESSİZ BİR KARARDIR;
    /// boş gerekçe ayrı bir testle yasaklanıyor.
    /// </summary>
    private static readonly Dictionary<string, string> DislananDizinler = new()
    {
        [".git"] = "Sürüm veritabanı; çalışma ağacı değil.",
        ["node_modules"] = "Üçüncü taraf bağımlılıklar; bizim yazdığımız kod değil.",
        ["bin"] = "Derleme çıktısı.",
        ["obj"] = "Derleme ara çıktısı.",
        [".next"] = "Next.js derleme çıktısı.",
        ["publish"] = "Yayın çıktısı.",
        ["backups"] = "Şifreli yedekler; ikili.",
        ["frontend-next-rollback"] = "Geri alma için tutulan eski derleme çıktısı.",
    };

    /// <summary>Taranmayan dosyalar.</summary>
    private static readonly Dictionary<string, string> DislananDosyalar = new()
    {
        ["package-lock.json"] =
            "npm BÜTÜNLÜK ÖZETLERİ (sha512-…): 650 satır, hepsi sır " +
            "biçiminde ama hiçbiri sır değil. Makine üretimi.",
        ["SecretInSourceGuardTests.cs"] =
            "Bekçinin kendisi: desenleri ve örnekleri içeriyor.",
    };

    /// <summary>
    /// Taranmayan uzantılar — ikili dosyalar. Metin olarak okunmaları
    /// anlamsız ve rastgele bayt dizileri sonsuz yanlış alarm üretir.
    /// </summary>
    private static readonly HashSet<string> IkiliUzantilar = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".ico", ".pdf", ".zip", ".gz",
        ".dll", ".exe", ".so", ".woff", ".woff2", ".ttf", ".eot",
    };

    /// <summary>
    /// DESEN kontrolünden muaf yollar (GERÇEK SIR kontrolü bunlara da
    /// bakar).
    /// </summary>
    private static readonly Dictionary<string, string> DesenMuafYollar = new()
    {
        ["/Migrations/"] =
            "EF'in ürettiği göç adları (\"20260804132300_NetEsasli…\") " +
            "43 karakteri aşıyor ve biçime uyuyor; hepsi yanlış alarm. " +
            "Elle yazılmıyorlar. GERÇEK SIR kontrolü yine de bakıyor.",
    };

    /*
     * DESEN KATMANININ EŞİĞİ: 40.
     *
     * ═══ 43'TEN 40'A NEDEN İNDİ — ÖLÇÜLDÜ ═══
     *
     * Eşik 43'tü (32 bayt -> base64 -> 43 karakter). Sonda Q bu eşiğin
     * bir SINIR olduğunu gösterdi: sabotaj dizgisi 42 karakterdi ve
     * bekçi görmedi. Aynı sebeple `safe-deploy.sh`'deki 40 karakterlik
     * değer de hiç yakalanmamıştı — yüzey dışında olduğu için değil,
     * EŞİĞİN ALTINDA kaldığı için.
     *
     * Eşik düşürmenin maliyeti ölçüldü (bekçinin süzgeç zinciri
     * çoğaltılıp izlenen dosyalara uygulanarak; çoğaltma eşik 43'te
     * gerçek bekçiyle aynı sonucu — 0 bulgu — vererek doğrulandı):
     *
     *     43 → 0 bulgu      36 → 11 bulgu
     *     40 → 0 bulgu      32 → 11 bulgu
     *                       24 → 16 bulgu
     *
     * 40'a inmek BEDAVA: tek bir yanlış alarm bile eklemiyor. 40'ın
     * altında gürültü başlıyor (test verisi kimlikleri).
     *
     * ═══ BU EŞİK BİR SINIRDIR, GİZLENMİYOR ═══
     *
     * 40 karakterin ALTINDAKİ bir sır bu katmandan geçer. Gerçek DB
     * parolası 10 karakter — hiçbir makul eşik onu yakalayamaz. Onu
     * yakalamak GERÇEK SIR katmanının işi ve o katman eşikten TAMAMEN
     * bağımsız.
     *
     * Yalnız DİZGİ SABİTLERİ taranıyor: bir sır koda her zaman dizgi
     * olarak girer, tanımlayıcı adı olarak değil.
     */
    private static readonly Regex DizgiSabiti = new(
        "[\"']([A-Za-z0-9_\\-]{40,})[\"']",
        RegexOptions.Compiled);

    /*
     * GERÇEK SIR ADAYLARI: alfanümerik ve sır alfabesinde geçebilecek
     * ayraçları içeren maksimal koşular. Ayrıca `ANAHTAR=değer`
     * biçiminde yazılmış olma ihtimaline karşı `=` sonrası da aday
     * sayılıyor.
     */
    private static readonly Regex AdayKosu = new(
        "[A-Za-z0-9_\\-+/=.]{16,}",
        RegexOptions.Compiled);

    private static string DepoKok()
    {
        var dizin = new DirectoryInfo(AppContext.BaseDirectory);

        while (dizin is not null &&
               !(Directory.Exists(Path.Combine(dizin.FullName, "deploy")) &&
                 Directory.Exists(Path.Combine(dizin.FullName, "backend"))))
        {
            dizin = dizin.Parent;
        }

        return dizin?.FullName
            ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    /// <summary>
    /// TARANAN EVREN: git'in İZLEDİĞİ dosyalar.
    ///
    /// ═══ NEDEN `git ls-files`, NEDEN DİZİN GEZİNTİSİ DEĞİL ═══
    ///
    /// İlk sürüm dizinleri geziyordu ve ZAMAN AŞIMINA UĞRADI: gerçek
    /// sır kontrolünün dışlaması olmadığı için `backups/` altındaki
    /// 40 MB'lık şifreli yedekleri ve `publish/` çıktısını METİN olarak
    /// okumaya kalktı.
    ///
    /// Doğru kapsam zaten buydu: bir sır ancak COMMIT EDİLEBİLEN bir
    /// dosyaya sızar. Derleme çıktısı, yedek ve bağımlılık ağacı
    /// depoya hiç girmiyor — onları "dışlamak" bir istisna değil,
    /// evrenin tanımı.
    ///
    /// Böylece "her şey taranır" iddiası GERÇEKTEN doğru: git'in
    /// izlediği her dosya, uzantısı ne olursa olsun.
    /// </summary>
    private static List<string> IzlenenDosyalar(string kok)
    {
        var süreç = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "ls-files -z",
                WorkingDirectory = kok,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            }
        };

        süreç.Start();
        var cikti = süreç.StandardOutput.ReadToEnd();
        süreç.WaitForExit();

        return cikti
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(g => Path.Combine(kok, g))
            .Where(File.Exists)
            .ToList();
    }

    private static IEnumerable<string> TaranacakDosyalar(string kok)
    {
        foreach (var dosya in IzlenenDosyalar(kok))
        {
            var goreli = Path.GetRelativePath(kok, dosya).Replace('\\', '/');
            var parcalar = goreli.Split('/');

            if (parcalar.Any(p => DislananDizinler.ContainsKey(p))) continue;
            if (DislananDosyalar.ContainsKey(Path.GetFileName(dosya))) continue;
            if (IkiliUzantilar.Contains(Path.GetExtension(dosya))) continue;

            yield return dosya;
        }
    }

    private static string Ozet(string deger) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(deger)))
            .ToLowerInvariant();

    /// <summary>
    /// Üretim sırlarının özetleri: (özet, uzunluk, ad).
    /// </summary>
    private static List<(string Ozet, int Uzunluk, string Ad)> SirOzetleri(string kok)
    {
        var yol = Path.Combine(
            kok, "backend", "EnderunAI.Api.Tests", "bekci",
            "uretim-sir-ozetleri.txt");

        var sonuc = new List<(string, int, string)>();
        if (!File.Exists(yol)) return sonuc;

        foreach (var satir in File.ReadAllLines(yol))
        {
            var s = satir.Trim();
            if (s.Length == 0 || s.StartsWith('#')) continue;

            var parcalar = s.Split(
                ' ', StringSplitOptions.RemoveEmptyEntries);

            if (parcalar.Length >= 3 &&
                int.TryParse(parcalar[1], out var uzunluk))
            {
                sonuc.Add((parcalar[0].ToLowerInvariant(), uzunluk, parcalar[2]));
            }
        }

        return sonuc;
    }

    [Fact]
    public void Tarama_BosaDusmuyor_POZITIF_KONTROL()
    {
        /*
         * KURAL 48: boş sonuç yokluğun kanıtı değildir. Kök bulunamaz
         * ya da süzgeç her şeyi elerse aşağıdaki testler SESSİZCE
         * yeşile düşerdi.
         *
         * AYRICA YÜZEYİN KENDİSİ SINANIYOR: bu bekçinin yeniden
         * yazılma sebebi `.sh` ve `.yml` dosyalarının hiç taranmamış
         * olmasıydı. O yüzeylerin kapsamda olduğu ayrıca kanıtlanıyor
         * — yoksa kapsam sessizce daralabilir.
         */
        var kok = DepoKok();
        var dosyalar = TaranacakDosyalar(kok).ToList();

        Assert.True(dosyalar.Count > 500,
            $"Yalnız {dosyalar.Count} dosya tarandı; kapsam daralmış.");

        Assert.Contains(dosyalar, d => d.EndsWith(".sh", StringComparison.Ordinal));
        Assert.Contains(dosyalar, d => d.EndsWith(".yml", StringComparison.Ordinal));
        Assert.Contains(dosyalar, d => d.EndsWith(".cs", StringComparison.Ordinal));
        Assert.Contains(dosyalar, d => d.EndsWith(".tsx", StringComparison.Ordinal));
    }

    [Fact]
    public void HerIstisna_GerekceliOlmali()
    {
        /*
         * GEREKÇESİZ İSTİSNA SESSİZ BİR KARARDIR. Liste uzayabilir —
         * ama her satırı bir cümleyle savunulmak zorunda.
         */
        foreach (var (ad, gerekce) in DislananDizinler)
            Assert.False(string.IsNullOrWhiteSpace(gerekce), $"Dizin gerekçesiz: {ad}");

        foreach (var (ad, gerekce) in DislananDosyalar)
            Assert.False(string.IsNullOrWhiteSpace(gerekce), $"Dosya gerekçesiz: {ad}");

        foreach (var (ad, gerekce) in DesenMuafYollar)
            Assert.False(string.IsNullOrWhiteSpace(gerekce), $"Yol gerekçesiz: {ad}");
    }

    /// <summary>
    /// ÜRETİM SIRLARININ ADLARI — ORTAMDAN OKUNACAK OLANLAR.
    ///
    /// `DB_PAROLASI` bir ortam değişkeni değil; `DB_CONNECTION` içinden
    /// çıkarılıyor. Ayrı ad taşıması bilinçli: bulgu raporunda
    /// "bağlantı dizesi" değil "parola" yazması gerekiyor.
    /// </summary>
    private static readonly string[] ZorunluSirlar =
    [
        "JWT_SECRET",
        "DB_PAROLASI",
        "SMTP_PASS",
        "SMTP_USER",
        "SEED_ADMIN_PASSWORD",
        "ANTHROPIC_API_KEY",
    ];

    /// <summary>
    /// HENÜZ VAR OLMAYAN SIRLAR — ortaya çıktıklarında
    /// <see cref="ZorunluSirlar"/>'a TAŞINACAKLAR.
    ///
    /// Burada durmaları bir muafiyet değil, bir KAYIT: "bu sır
    /// kontrol edilmiyor ve bunu biliyoruz". Liste olmasaydı, M3
    /// geldiğinde VAPID özel anahtarı sessizce korumasız kalırdı —
    /// tam olarak bu bekçinin yeniden yazılma sebebi.
    /// </summary>
    private static readonly Dictionary<string, string> HenuzYokSirlar = new()
    {
        ["PORTAL_TOKEN_ANAHTARI"] =
            "Portal tokenları kayıt başına üretiliyor, ortamda tek bir " +
            "anahtar yok. Merkezî bir anahtara geçilirse buraya taşınır.",
        ["VAPID_PRIVATE_KEY"] =
            "M3 (mobil bildirim) ile gelecek. Geldiği gün ZorunluSirlar'a " +
            "taşınmalı.",
    };

    private const string OrtamDosyasi = "/etc/enderunai/backend.env";

    /// <summary>
    /// Üretim sırlarını ortam dosyasından okur. DEĞERLER HİÇBİR YERE
    /// BASILMAZ — yalnız bu sözlükte tutulur ve arama için kullanılır.
    /// </summary>
    private static Dictionary<string, string> UretimSirlari()
    {
        var sonuc = new Dictionary<string, string>();
        if (!File.Exists(OrtamDosyasi)) return sonuc;

        string[] satirlar;
        try { satirlar = File.ReadAllLines(OrtamDosyasi); }
        catch { return sonuc; }

        var ham = new Dictionary<string, string>();

        foreach (var satir in satirlar)
        {
            var s = satir.Trim();
            if (s.Length == 0 || s.StartsWith('#')) continue;

            var esittir = s.IndexOf('=');
            if (esittir <= 0) continue;

            var ad = s[..esittir].Trim();
            var deger = s[(esittir + 1)..].Trim().Trim('"', '\'');
            if (deger.Length > 0) ham[ad] = deger;
        }

        foreach (var ad in ZorunluSirlar)
        {
            if (ad == "DB_PAROLASI")
            {
                if (ham.TryGetValue("DB_CONNECTION", out var baglanti))
                {
                    var m = Regex.Match(baglanti, "Password=([^;]+)");
                    if (m.Success) sonuc["DB_PAROLASI"] = m.Groups[1].Value;
                }

                continue;
            }

            if (ham.TryGetValue(ad, out var d)) sonuc[ad] = d;
        }

        return sonuc;
    }

    [SkippableFact]
    public void HicbirDosyada_GERCEK_URETIM_SIRRI_Olmamali()
    {
        /*
         * ═══ BU KATMAN EŞİKTEN TAMAMEN BAĞIMSIZ ═══
         *
         * Desen katmanı sezgiseldir: uzunluk, alfabe, entropi bakar ve
         * yanlış alarmı azaltmak için bir EŞİK taşır. Bu katman hiçbir
         * filtre uygulamaz — sırrın kendisini BİREBİR arar.
         *
         * GEREKÇE ÖLÇÜLMÜŞTÜR: gerçek DB parolası 10 KARAKTER. Hiçbir
         * makul desen eşiği onu geçirmez; ama bu katmanın onu yakalaması
         * ZORUNLUDUR. Uzunluk, sırrın değerini belirlemez.
         *
         * ═══ HİÇBİR DIŞLAMASI YOK ═══
         *
         * Bir özet/dizgi eşleşmesi yanlış alarm ÜRETEMEZ; o yüzden
         * `package-lock.json` ve göç dosyaları dahil izlenen HER dosya
         * taranır. Bir sır, en çok gözden kaçan dosyaya sızar.
         *
         * ═══ SIR HİÇBİR ÇIKTIYA YAZILMAZ ═══
         *
         * Bulgu raporunda yalnız sırrın ADI ve dosya/satır bilgisi
         * geçer. Değerin kendisi ne hata mesajına, ne günlüğe, ne de
         * test çıktısına düşer — yoksa bekçi, koruduğu şeyi ifşa eden
         * araca dönüşürdü.
         */
        var sirlar = UretimSirlari();

        Skip.If(
            !File.Exists(OrtamDosyasi),
            $"ÜRETİM ORTAM DOSYASI YOK ({OrtamDosyasi}) — gerçek sır " +
            "kontrolü KOŞULAMADI. Bu ortamda (ör. CI) sırlar okunamıyor; " +
            "koruma sunucudaki koşuda geçerli. ATLANDI, GEÇMEDİ.");

        /*
         * KURAL 48 — BOŞ KÜME SORUNU. Ortam dosyası VAR ama hiçbir sır
         * okunamadıysa, bu test hiçbir şey sınamadan yeşile düşerdi.
         */
        Assert.True(
            sirlar.Count > 0,
            $"{OrtamDosyasi} okunuyor ama HİÇBİR üretim sırrı " +
            "çıkarılamadı. Bu katman şu an hiçbir şey sınamıyor.");

        /*
         * SESSİZ ATLAMA YOK: beklenen bir sır ortamda yoksa "kontrol
         * edilemedi" diye KIRMIZI verir. Sessizce atlamak, boş küme
         * sorununun küçük hâlidir.
         */
        var eksik = ZorunluSirlar.Where(a => !sirlar.ContainsKey(a)).ToList();

        Assert.True(
            eksik.Count == 0,
            "KONTROL EDİLEMEDİ — şu üretim sırları ortamda bulunamadı:\n  " +
            string.Join("\n  ", eksik) +
            $"\n\nBu sırlar taranmadı, yani onlar için bu bekçi HİÇBİR " +
            "ŞEY söylemiyor. Ortamda gerçekten yoksa `ZorunluSirlar` " +
            "listesinden çıkarılıp `HenuzYokSirlar`'a gerekçesiyle " +
            "taşınmalı — sessizce atlanmamalı.");

        var kok = DepoKok();
        var bulgular = new List<string>();

        foreach (var dosya in IzlenenDosyalar(kok))
        {
            var goreli = Path.GetRelativePath(kok, dosya).Replace('\\', '/');

            // İKİLİ DOSYALAR: metin olarak okunamaz. Bu bir DIŞLAMA
            // değil, teknik bir sınır — ve dar tutuluyor. Bunun dışında
            // İZLENEN HER DOSYA taranıyor.
            if (IkiliUzantilar.Contains(Path.GetExtension(dosya))) continue;

            string[] satirlar;
            try { satirlar = File.ReadAllLines(dosya); }
            catch { continue; }

            for (var i = 0; i < satirlar.Length; i++)
            {
                foreach (var (ad, deger) in sirlar)
                {
                    if (satirlar[i].Contains(deger, StringComparison.Ordinal))
                    {
                        // YALNIZ AD VE KONUM — DEĞER ASLA.
                        bulgular.Add($"{goreli}:{i + 1}  ->  GERÇEK ÜRETİM SIRRI: {ad}");
                    }
                }
            }
        }

        Assert.True(
            bulgular.Count == 0,
            "╔══════════════════════════════════════════════════════╗\n" +
            "║  GERÇEK ÜRETİM SIRRI KAYNAKTA BULUNDU                ║\n" +
            "╚══════════════════════════════════════════════════════╝\n\n  " +
            string.Join("\n  ", bulgular) +
            "\n\nBU BİR 'SIR BENZERİ DİZGİ' DEĞİL: değer, canlıda " +
            "kullanılan sırla BİREBİR AYNI.\n\n" +
            "DOSYADAN SİLMEK YETMEZ — commit edildiyse git geçmişinde " +
            "durur. Sır DÖNDÜRÜLMELİ (yeni değer üret, ortam değişkenini " +
            "güncelle, servisleri yeniden başlat; mevcut oturumlar düşer).");
    }

    [Fact]
    public void HenuzYokSirlar_Gerekceli_Ve_Ortamda_Yok()
    {
        /*
         * Bu liste bir MUAFİYET DEĞİL, bir KAYIT. İki şey sınanıyor:
         *
         * 1. Her girdinin gerekçesi var (gerekçesiz istisna sessiz
         *    karardır).
         * 2. Listedeki bir sır ortamda ORTAYA ÇIKTIYSA test kırmızı
         *    verir — yani "henüz yok" iddiası sessizce eskiyemez.
         *    M3 geldiğinde VAPID anahtarı burada takılacak ve
         *    ZorunluSirlar'a taşınmaya zorlayacak.
         */
        foreach (var (ad, gerekce) in HenuzYokSirlar)
            Assert.False(string.IsNullOrWhiteSpace(gerekce), $"Gerekçesiz: {ad}");

        if (!File.Exists(OrtamDosyasi)) return;

        var ham = File.ReadAllLines(OrtamDosyasi)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0 && !x.StartsWith('#'))
            .Select(x => x.Split('=', 2)[0].Trim())
            .ToHashSet();

        var artikVar = HenuzYokSirlar.Keys.Where(ham.Contains).ToList();

        Assert.True(
            artikVar.Count == 0,
            "ARTIK VAR OLAN SIRLAR HÂLÂ 'HENÜZ YOK' LİSTESİNDE:\n  " +
            string.Join("\n  ", artikVar) +
            "\n\nBunlar `ZorunluSirlar`'a taşınmalı; aksi hâlde gerçek " +
            "sır katmanı onları TARAMIYOR ve kimse fark etmiyor.");
    }

    [Fact]
    public void KaynakKodda_TokenBicimindeDizgiOlmamali()
    {
        var kok = DepoKok();
        var bulgular = new List<string>();

        foreach (var dosya in TaranacakDosyalar(kok))
        {
            var goreli = Path.GetRelativePath(kok, dosya).Replace('\\', '/');

            if (DesenMuafYollar.Keys.Any(y => ("/" + goreli).Contains(y, StringComparison.Ordinal)))
                continue;

            string[] satirlar;
            try { satirlar = File.ReadAllLines(dosya); }
            catch { continue; }

            for (var i = 0; i < satirlar.Length; i++)
            {
                foreach (Match eslesme in DizgiSabiti.Matches(satirlar[i]))
                {
                    var deger = eslesme.Groups[1].Value;

                    /*
                     * UYDURMA TEST VERİSİ AÇIK ÖNEKLE İŞARETLİ.
                     *
                     * Önek RASTLANTIYA BIRAKILMIYOR, elle konuyor:
                     * gerçek bir tokenın bu karakterlerle başlaması
                     * pratikte imkânsız.
                     *
                     * TEK ÖNEK, İKİ DEĞİL: "SAHTE-" diye ikinci bir
                     * işaret açmak, aynı sözü iki dille söylemek
                     * olurdu — ve bu depoda iki kopya zamanla ayrışıyor.
                     */
                    if (deger.StartsWith("TEST-", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var harfVar = deger.Any(char.IsLetter);
                    var rakamVar = deger.Any(char.IsDigit);
                    if (!harfVar || !rakamVar) continue;

                    // Hex özet (yalnız 0-9a-f) token değil.
                    if (deger.All(c => char.IsDigit(c) || (c >= 'a' && c <= 'f')))
                        continue;

                    // Gömülü küçük ikili veri (1x1 PNG, PDF başlığı).
                    if (deger.StartsWith("iVBORw0KGgo", StringComparison.Ordinal) ||
                        deger.StartsWith("JVBERi0", StringComparison.Ordinal) ||
                        deger.StartsWith("data:", StringComparison.Ordinal))
                        continue;

                    bulgular.Add($"{goreli}:{i + 1}  ->  {deger}");
                }
            }
        }

        Assert.True(
            bulgular.Count == 0,
            "KAYNAKTA SIR BİÇİMİNDE DİZGİ BULUNDU:\n  " +
            string.Join("\n  ", bulgular) +
            "\n\nGerçek bir token, anahtar ya da parola kaynağa " +
            "yazılmamalı: commit edilir, push edilir ve git geçmişinden " +
            "silinemez. Test verisi gerekiyorsa \"TEST-\" önekiyle " +
            "UYDURMA bir değer üretin — biçimin doğru olması yeter.");
    }

    /// <summary>
    /// Bir koşudan, kayıtlı uzunluklara uyan adayları çıkarır.
    ///
    /// `ANAHTAR=değer` biçiminde yazılmış bir sırrı da yakalamak için
    /// `=` sonrası ayrıca aday sayılıyor.
    /// </summary>
    private static IEnumerable<string> Adaylar(string kosu, HashSet<int> uzunluklar)
    {
        if (uzunluklar.Contains(kosu.Length)) yield return kosu;

        var esittenSonra = kosu.LastIndexOf('=');
        if (esittenSonra >= 0 && esittenSonra < kosu.Length - 1)
        {
            var kuyruk = kosu[(esittenSonra + 1)..];
            if (uzunluklar.Contains(kuyruk.Length)) yield return kuyruk;
        }
    }
}
