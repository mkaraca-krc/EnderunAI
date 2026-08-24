using EnderunAI.Api.Services.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EnderunAI.Api.Tests;

/// <summary>
/// BAYRAK NE KESER, NE KESMEZ.
///
/// `DAILY_SUMMARY_MODE` bir ANA ŞALTER DEĞİL, GÖNDERİM KAPISIDIR.
/// `kapali` modda bile termin taraması ve G3 erteleme nöbetçisi
/// koşar; kesilen yalnızca özet/e-posta yoludur.
///
/// BU TEST NEDEN VAR: bu kural bir süre yalnızca YORUMDA durdu ve
/// yorum koddaki davranışla çelişiyordu ("Tarama HİÇ KOŞMAZ").
/// `git log`: ikisi de aynı commit'te (9212d291) doğmuş — regresyon
/// değil, doğuştan tutarsızlık; hiçbir test onu tutmuyordu.
///
/// TEK KANIT ÇAĞRI SAYACIDIR. İlk sürümde ikinci bir "kanıt" daha
/// vardı: `DailySummaryService` kapsayıcıya hiç kaydedilmiyordu,
/// gönderim yoluna girilirse `GetRequiredService` fırlasın diye.
/// O kanıt GEÇERSİZ ilan edildi — üretim zincirinde
/// `DailySummaryBackgroundService.ExecuteAsync:38` turu
/// `catch (Exception)` ile sarıyor. Fırlatmaya dayanan kanıt yalnızca
/// bu test o sarmalayıcıyı atladığı için çalışıyordu; biri testi
/// `ExecuteAsync` üzerinden koşturmaya çevirse sessizce buharlaşırdı.
/// Sayaç yutulamaz. (DURUM.md §5 kural 23)
/// </summary>
public sealed class DailySummaryModeGatingTests
{
    private sealed class SayanTarayici : ITaskDueNotificationScanner
    {
        public int CagriSayisi { get; private set; }

        public Task<int> ScanAsync(CancellationToken cancellationToken)
        {
            CagriSayisi++;
            return Task.FromResult(0);
        }
    }

    private sealed class SayanBekci : IScopeDeferralWatchdog
    {
        public int CagriSayisi { get; private set; }

        public Task CheckAsync(CancellationToken cancellationToken)
        {
            CagriSayisi++;
            return Task.CompletedTask;
        }
    }

    private sealed class SayanOzet : IDailySummaryRunner
    {
        public int CagriSayisi { get; private set; }

        public DailySummaryMode? GelenMod { get; private set; }

        public Task<int> RunAsync(DailySummaryMode mode, CancellationToken cancellationToken)
        {
            CagriSayisi++;
            GelenMod = mode;
            return Task.FromResult(0);
        }
    }

    private sealed record Kurulum(
        DailySummaryBackgroundService Servis,
        SayanTarayici Tarayici,
        SayanBekci Bekci,
        SayanOzet Ozet);

    private static Kurulum Kur(string? modDegeri)
    {
        var tarayici = new SayanTarayici();
        var bekci = new SayanBekci();
        var ozet = new SayanOzet();

        var koleksiyon = new ServiceCollection();
        koleksiyon.AddScoped<ITaskDueNotificationScanner>(_ => tarayici);
        koleksiyon.AddScoped<IScopeDeferralWatchdog>(_ => bekci);
        koleksiyon.AddScoped<IDailySummaryRunner>(_ => ozet);

        var yapilandirma = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DAILY_SUMMARY_MODE"] = modDegeri
            })
            .Build();

        var servis = new DailySummaryBackgroundService(
            koleksiyon.BuildServiceProvider(),
            yapilandirma,
            NullLogger<DailySummaryBackgroundService>.Instance);

        return new Kurulum(servis, tarayici, bekci, ozet);
    }

    [Theory]
    [InlineData("kapali")]
    [InlineData("off")]      // eski İngilizce değer
    [InlineData("saçmalık")] // tanınmayan değer
    [InlineData(null)]       // değişken tanımsız
    public async Task Kapali_TaramaVeBekciyiYineDeKosturur(string? modDegeri)
    {
        var k = Kur(modDegeri);

        await k.Servis.BirTurAsync(CancellationToken.None);

        Assert.Equal(1, k.Tarayici.CagriSayisi);
        Assert.Equal(1, k.Bekci.CagriSayisi);

        // ÖZET YOLUNA HİÇ GİRİLMEDİ — sayaç, tek kanıt.
        Assert.Equal(0, k.Ozet.CagriSayisi);
    }

    /// <summary>
    /// Ters yön: mod kapalı DEĞİLSE özet yolu gerçekten koşuyor ve
    /// doğru mod aşağı geçiyor. Bu olmasaydı yukarıdaki test, bayrak
    /// tamamen işlevsizleşse bile yeşil kalırdı.
    /// </summary>
    [Theory]
    [InlineData("dryrun", DailySummaryMode.DryRun)]
    [InlineData("acik", DailySummaryMode.Acik)]
    [InlineData("on", DailySummaryMode.Acik)]
    public async Task KapaliDegilse_OzetYoluKosar(string modDegeri, DailySummaryMode beklenen)
    {
        var k = Kur(modDegeri);

        await k.Servis.BirTurAsync(CancellationToken.None);

        Assert.Equal(1, k.Ozet.CagriSayisi);
        Assert.Equal(beklenen, k.Ozet.GelenMod);

        // Tarama ve nöbetçi, özet yolundan bağımsız olarak yine koştu.
        Assert.Equal(1, k.Tarayici.CagriSayisi);
        Assert.Equal(1, k.Bekci.CagriSayisi);
    }

    // ---------------------------------------------------------------
    // MOD AYRIŞTIRMA EMNİYETİ
    // ---------------------------------------------------------------

    /// <summary>
    /// EN ÖNEMLİ KURAL: `Acik`'a YALNIZ açıkça "acik"/"on" yazılırsa
    /// düşülür. Yazım hatası, boş değer ya da tanımsız değişken
    /// gerçek insanlara e-posta göndermeye BAŞLATAMAZ.
    /// </summary>
    [Theory]
    [InlineData(null, DailySummaryMode.Kapali, false)]
    [InlineData("", DailySummaryMode.Kapali, true)]
    [InlineData("   ", DailySummaryMode.Kapali, true)]
    [InlineData("offf", DailySummaryMode.Kapali, true)]
    [InlineData("Off", DailySummaryMode.Kapali, false)]
    [InlineData("off", DailySummaryMode.Kapali, false)]
    [InlineData("kapali", DailySummaryMode.Kapali, false)]
    [InlineData("KAPALI", DailySummaryMode.Kapali, false)]
    [InlineData("DRYRUN", DailySummaryMode.DryRun, false)]
    [InlineData(" dryrun ", DailySummaryMode.DryRun, false)]
    [InlineData("dryrunn", DailySummaryMode.Kapali, true)]
    [InlineData("acik", DailySummaryMode.Acik, false)]
    [InlineData("ACIK", DailySummaryMode.Acik, false)]
    [InlineData("on", DailySummaryMode.Acik, false)]
    [InlineData("açık", DailySummaryMode.Kapali, true)]  // Türkçe harf: TANINMAZ
    [InlineData("true", DailySummaryMode.Kapali, true)]
    [InlineData("1", DailySummaryMode.Kapali, true)]
    [InlineData("enabled", DailySummaryMode.Kapali, true)]
    public void ModCozumle_EslemeTablosu(
        string? ham, DailySummaryMode beklenen, bool taninmamaliDeger)
    {
        var mod = DailySummaryBackgroundService.ModCozumle(ham, out var taninmadi);

        Assert.Equal(beklenen, mod);
        Assert.Equal(taninmamaliDeger, taninmadi);
    }

    /// <summary>
    /// KAPSAYICI KURAL, TEK TEK ÖRNEKTEN GÜÇLÜDÜR: "acik"/"on"
    /// DIŞINDA hiçbir değer Acik üretemez. Yukarıdaki tablo
    /// örnekleri sayar; bu, kuralı sayar.
    /// </summary>
    [Fact]
    public void ModCozumle_AcikDisindaHicbirDegerAcikUretmez()
    {
        string?[] adaylar =
        [
            null, "", " ", "off", "OFF", "kapali", "dryrun", "DryRun",
            "offf", "onn", "no", "yes", "true", "false", "1", "0",
            "enabled", "disabled", "açık", "aç1k", "ac1k", "an", "o n",
            "acikk", "aciik", "send", "mail", "prod", "production"
        ];

        foreach (var aday in adaylar)
        {
            var mod = DailySummaryBackgroundService.ModCozumle(aday, out _);

            Assert.True(
                mod != DailySummaryMode.Acik,
                $"\"{aday ?? "(null)"}\" değeri Acik'a düştü — gerçek " +
                "insanlara e-posta gönderilmesine yol açardı.");
        }
    }
}
