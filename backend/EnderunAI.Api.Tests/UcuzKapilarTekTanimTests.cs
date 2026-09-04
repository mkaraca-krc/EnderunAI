using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// UCUZ KAPILAR — TEK TANIM, İKİ ÇAĞIRAN.
///
/// ── DOĞURAN OLAY (2026-09-04) ──
///
/// Kurumsal kimlik taraması PAROLA/1 yayınında bir buton rengini
/// yakaladı. Bulgu doğruydu, YERİ yanlıştı: iki tam turdan (~27 dk) ve
/// arka uç publish'inden SONRA geldi. Aynı bulgu sıra tersine
/// çevrilseydi 24 saniyede gelirdi.
///
/// Sıra düzeltildi ve kapılar iki yerde koşar oldu: yayın turunda ve
/// push öncesi kancada. İki yerde İKİ AYRI LİSTE olsaydı zamanla
/// ayrışırlardı — ve ayrışan her nokta, birinin sınamadığı bir
/// noktadır.
///
/// Bu, bu kod tabanının en sık hatası. Bir günde beş kez görüldü:
/// merkez kuralının PUT kopyası, `dotnet ef` çağrısının üç ayrı
/// ortamı, sır bekçisinin taranmayan yüzeyi, parola uzunluğunun iki
/// kopyası, parola yazmanın üç ayrı yolu.
/// </summary>
public sealed class UcuzKapilarTekTanimTests
{
    private static string DeployKok()
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

    private static string Oku(params string[] parcalar)
    {
        var yol = Path.Combine(new[] { DeployKok() }.Concat(parcalar).ToArray());
        Assert.True(File.Exists(yol), $"Dosya yok: {yol}");
        return File.ReadAllText(yol);
    }

    /// <summary>Yorum satırları soyulmuş kod.</summary>
    private static string Kod(params string[] parcalar) =>
        string.Join(
            "\n",
            Oku(parcalar).Split('\n').Where(s => !s.TrimStart().StartsWith('#')));

    [Fact]
    public void Dosyalar_Okunabiliyor_POZITIF_KONTROL()
    {
        Assert.Contains("KAPILAR=(", Kod("deploy", "scripts", "ucuz-kapilar.sh"));
        Assert.Contains("gocleri_dogrula", Kod("deploy", "scripts", "safe-deploy.sh"));
        Assert.Contains("ucuz-kapilar.sh", Kod("deploy", "git-hooks", "pre-push"));
    }

    [Fact]
    public void Her_Iki_Cagiran_Da_AYNI_Betigi_Kullaniyor()
    {
        Assert.Contains("ucuz-kapilar.sh", Kod("deploy", "scripts", "safe-deploy.sh"));
        Assert.Contains("ucuz-kapilar.sh", Kod("deploy", "git-hooks", "pre-push"));
    }

    [Fact]
    public void Kapi_Listesi_YALNIZ_TEK_Dosyada()
    {
        /*
         * ASIL İDDİA: liste ikinci bir yerde YENİDEN yazılmasın.
         *
         * Kapı komutları (kimlik taraması, tsc, build, sır bekçisi
         * filtresi) yalnız `ucuz-kapilar.sh` içinde geçmeli. Çağıranlar
         * betiği ÇAĞIRIR, kapıları tekrar etmez.
         */
        var imzalar = new[]
        {
            "kimlik-taramasi.mjs",
            "tsc --noEmit",
            "FullyQualifiedName~SecretInSourceGuardTests",
        };

        var cagiranlar = new[]
        {
            Path.Combine("deploy", "scripts", "safe-deploy.sh"),
            Path.Combine("deploy", "git-hooks", "pre-push"),
        };

        var bulgular = new List<string>();

        foreach (var cagiran in cagiranlar)
        {
            var kod = Kod(cagiran.Split(Path.DirectorySeparatorChar));

            foreach (var imza in imzalar)
            {
                if (kod.Contains(imza, StringComparison.Ordinal))
                    bulgular.Add($"{cagiran}  ->  {imza}");
            }
        }

        Assert.True(
            bulgular.Count == 0,
            "KAPI LİSTESİNİN İKİNCİ KOPYASI:\n  " +
            string.Join("\n  ", bulgular) +
            "\n\nKapılar yalnız `ucuz-kapilar.sh` içinde tanımlanır; " +
            "çağıranlar onu ÇAĞIRIR. İki liste zamanla ayrışır ve " +
            "ayrışan her nokta, birinin sınamadığı bir noktadır.");
    }

    [Fact]
    public void Ucuz_Kapilar_PAHALI_TURLARDAN_ONCE_Cagriliyor()
    {
        /*
         * SIRA DAVRANIŞIN PARÇASI. Kapı, arka uç turundan SONRA
         * çağrılsaydı hiçbir şeyi erken durdurmazdı — düzeltilen şeyin
         * ta kendisi buydu.
         */
        /*
         * ── KARŞILAŞTIRMA `main()` GÖVDESİNDE, TÜM DOSYADA DEĞİL ──
         *
         * İlk yazımım tüm dosyada ilk geçişleri karşılaştırıyordu ve
         * KIRMIZI verdi: `run_backend_tests` satır 363'te TANIMLANIYOR,
         * çağrısı ise 828'de. Yani test, tanımı çağrı sanmıştı.
         *
         * Kod doğruydu, ÖLÇÜM yanlıştı. Aranan şey "adı nerede geçiyor"
         * değil, "hangi sırayla ÇAĞRILIYOR".
         */
        var tam = Kod("deploy", "scripts", "safe-deploy.sh");

        var mainBas = tam.IndexOf("main()", StringComparison.Ordinal);
        Assert.True(mainBas >= 0, "safe-deploy.sh içinde main() bulunamadı.");

        var kod = tam[mainBas..];

        var ucuz = kod.IndexOf("ucuz-kapilar.sh", StringComparison.Ordinal);
        var arkaUc = kod.IndexOf("run_backend_tests", StringComparison.Ordinal);

        Assert.True(ucuz >= 0, "Ucuz kapılar main() içinde çağrılmıyor.");
        Assert.True(arkaUc >= 0, "Arka uç turu main() içinde çağrılmıyor.");
        Assert.True(
            ucuz < arkaUc,
            "Ucuz kapılar arka uç turundan SONRA çağrılıyor; bu sırayla " +
            "hiçbir şeyi erken durdurmaz.");
    }
}
