using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// DERLEME KOŞUCUSU NÖBETİ — ÜÇ KAPI DA YERİNDE KALSIN.
///
/// 2026-08-26: bu makinede bir oturumda ÜÇ OOM yaşandı. Sebep, kesilen
/// bir derlemenin ardında bıraktığı Roslyn süreçleriydi — `csc.dll`
/// PPID=1 ile 3,9 GB, `VBCSCompiler` (kalıcı derleyici sunucusu)
/// 2,9 GB. İkinci derleme aynı obj/ kilidinde buluşunca 8 GB'lık makine
/// tükendi. Canlı uygulama ile test koşusu AYNI makinede.
///
/// `scripts/derleme-kos.sh` üç kapıyı koyuyor: tek örnek (systemd
/// scope), süreç ağacı (cgroup), bellek tavanı (MemoryMax). Üçü de
/// sondayla kanıtlandı.
///
/// BU TESTİN İŞİ: kapıların sessizce kaldırılmasını yakalamak. Kabuk
/// betiği testsiz bir yüzey — "şu tavanı geçici olarak kaldıralım"
/// düzenlemesi hiçbir yerde kırmızıya dönmezdi. Aynı disiplin yedek
/// betiğinde de uygulanıyor (bkz. <see cref="BackupScriptGuardTests"/>).
/// </summary>
public sealed class DerlemeKosucuGuardTests
{
    private static DirectoryInfo RepoKoku()
    {
        var dizin = new DirectoryInfo(AppContext.BaseDirectory);

        while (dizin is not null &&
               !Directory.Exists(Path.Combine(dizin.FullName, "scripts")))
        {
            dizin = dizin.Parent;
        }

        Assert.True(dizin is not null, "Repo kökü bulunamadı.");
        return dizin!;
    }

    private static string KosucuyuOku()
    {
        var yol = Path.Combine(RepoKoku().FullName, "scripts", "derleme-kos.sh");
        Assert.True(File.Exists(yol), $"Derleme koşucusu yok: {yol}");
        return File.ReadAllText(yol);
    }

    /// <summary>
    /// KOMUTA BAKIYORUZ, KELİMEYE DEĞİL (Kural 31).
    ///
    /// Yorum satırları atılıyor: bu dosyanın yorumları üç kapının
    /// adını da anlatıyor, yani ham metinde arama yapan bir nöbetçi
    /// kapılar SİLİNMİŞ olsa bile yeşil kalırdı.
    /// </summary>
    private static string KomutMetni(string betik)
    {
        var birlesik = betik.Replace("\\\n", " ");

        return string.Join("\n", birlesik.Split('\n')
            .Where(x => !x.TrimStart().StartsWith('#') && x.Trim().Length > 0));
    }

    [Fact]
    public void SurecAgaci_KendiCgroupunda()
    {
        var komutlar = KomutMetni(KosucuyuOku());

        Assert.Contains("systemd-run", komutlar);
        Assert.Contains("--scope", komutlar);

        // SABİT AD ŞART: adsız scope her koşuda yeni bir ad alır ve
        // "zaten koşan var mı" kapısı hiçbir şey yakalamaz.
        Assert.Contains("--unit=", komutlar);
    }

    [Fact]
    public void TekOrnekKapisi_Yerinde()
    {
        var komutlar = KomutMetni(KosucuyuOku());

        Assert.Contains("is-active", komutlar);

        // Kapı yalnız uyarmakla kalmayıp GERÇEKTEN durmalı.
        Assert.Contains("exit 75", komutlar);
    }

    [Fact]
    public void BellekTavani_Uygulaniyor()
    {
        var komutlar = KomutMetni(KosucuyuOku());
        Assert.Contains("MemoryMax=", komutlar);
    }

    /// <summary>
    /// KALICI DERLEYİCİ SUNUCUSU DOĞMASIN. 2,9 GB'lık yetimin kaynağı
    /// buydu; iki ayardan biri kalkarsa sunucu geri gelir.
    /// </summary>
    [Fact]
    public void KaliciDerleyiciSunucusu_Kapali()
    {
        var komutlar = KomutMetni(KosucuyuOku());

        Assert.Contains("MSBUILDDISABLENODEREUSE=1", komutlar);

        // KUŞAK: ortam değişkeni.
        Assert.Contains("--setenv=UseSharedCompilation=false", komutlar);

        // KUŞAK 2: AÇIK MSBuild ÖZELLİĞİ. Yalnız ortam değişkeni
        // bırakılsaydı, proje dosyasında ya da Directory.Build.props
        // içinde tanımlı açık bir özellik onu EZER ve kimse fark
        // etmez — ortam değişkeni MSBuild'de en zayıf kaynaktır.
        Assert.Contains("-p:UseSharedCompilation=false", komutlar);
    }

    /// <summary>
    /// ASKI — KOŞU BİTİNCE DERLEYİCİ SUNUCUSU KAPATILIR (R1).
    ///
    /// `VBCSCompiler` derleme bitince ÖLMEZ ve PPID=1'e bağlanır;
    /// süreç ağacını öldürmek onu temizlemez. Kuşak (paylaşımlı
    /// derlemenin kapalı olması) yeterli sanılabilir ama bir yol
    /// onu ezerse geriye 2,9 GB'lık bir süreç kalır.
    /// </summary>
    [Fact]
    public void KosuSonunda_DerleyiciSunucusuKapatiliyor()
    {
        var komutlar = KomutMetni(KosucuyuOku());

        Assert.Contains("build-server shutdown", komutlar);
    }

    /// <summary>
    /// KAPATMA, DERLEMENİN SONUCUNU YUTMAMALI.
    ///
    /// `build-server shutdown` en sonda çalışıyor; çıkış kodu
    /// saklanıp geri verilmezse başarısız bir derleme BAŞARILI
    /// görünür ve safe-deploy bozuk kodu yayınlar. Bu, koşucunun
    /// kendisinin üretebileceği en pahalı hata.
    /// </summary>
    [Fact]
    public void CikisKodu_Korunuyor()
    {
        var komutlar = KomutMetni(KosucuyuOku());

        Assert.Contains("cikis=$?", komutlar);
        Assert.Contains("exit \"$cikis\"", komutlar);
    }

    /// <summary>
    /// SAFE-DEPLOY DOĞRUDAN `dotnet test` ÇAĞIRMAMALI.
    ///
    /// Koşucu var ama yayın onu atlıyorsa koruma yok demektir — ve bu,
    /// koşucuya bakarak anlaşılamaz.
    /// </summary>
    [Fact]
    public void SafeDeploy_TestiKosucuUzerindenCagirir()
    {
        var yol = Path.Combine(
            RepoKoku().FullName, "deploy", "scripts", "safe-deploy.sh");

        Assert.True(File.Exists(yol), $"safe-deploy yok: {yol}");

        var komutlar = KomutMetni(File.ReadAllText(yol));

        var dogrudanCagrilar = komutlar.Split('\n')
            .Where(x => x.Contains("dotnet test"))
            .Where(x => !x.Contains("derleme-kos.sh"))
            .ToArray();

        Assert.True(dogrudanCagrilar.Length == 0,
            "Koşucuyu atlayan `dotnet test` çağrısı: " +
            string.Join(" | ", dogrudanCagrilar));
    }

    /// <summary>
    /// CANLIYI KORUYAN AYAR REPODA DA DURSUN.
    ///
    /// /etc altındaki drop-in makine yeniden kurulduğunda kaybolur ve
    /// kaybolduğu FARK EDİLMEZ — koruma sessizce yok olur.
    /// </summary>
    [Fact]
    public void OomKorumasi_RepodaKopyasiVar()
    {
        var yol = Path.Combine(
            RepoKoku().FullName, "deploy", "systemd", "oom-korumasi.conf");

        Assert.True(File.Exists(yol), $"OOM koruma drop-in'i repoda yok: {yol}");
        Assert.Contains("OOMScoreAdjust=-500", File.ReadAllText(yol));
    }
}
