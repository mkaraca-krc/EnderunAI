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
/// koşar; kesilen yalnızca e-postadır.
///
/// BU TEST NEDEN VAR: bu kural bir kez yalnızca YORUMDA duruyordu
/// ve yorum koddaki davranışla çelişiyordu ("Tarama HİÇ KOŞMAZ").
/// Çelişki, ikisi de aynı commit'te (9212d291) doğduğu için bir
/// regresyon değil, doğuştan bir tutarsızlıktı — yani hiçbir test
/// onu tutmuyordu. Artık iddia yorumda değil burada.
///
/// NEDEN ÇAĞRI SAYACI, NEDEN ETKİ DEĞİL: tur `ExecuteAsync` içinde
/// `catch (Exception)` ile sarılı ("bir günün hatası ertesi günü
/// kaybettirmesin"). Yutulan bir hata, etkiye bakan bir testi
/// sessizce yanıltabilirdi. Sayaç yutulamaz. (DURUM.md §5 kural 23)
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

    /// <summary>
    /// KASITLI OLARAK EKSİK KAPSAYICI.
    ///
    /// `DailySummaryService` BİLEREK kaydedilmedi. Böylece gönderim
    /// yoluna girilirse `GetRequiredService` fırlatır — yani
    /// "e-posta yolu hiç açılmadı" iddiası, sayacın yanında ikinci
    /// bir kanıtla daha korunur.
    /// </summary>
    private static (DailySummaryBackgroundService Servis,
                    SayanTarayici Tarayici,
                    SayanBekci Bekci) Kur(string? modDegeri)
    {
        var tarayici = new SayanTarayici();
        var bekci = new SayanBekci();

        var koleksiyon = new ServiceCollection();
        koleksiyon.AddScoped<ITaskDueNotificationScanner>(_ => tarayici);
        koleksiyon.AddScoped<IScopeDeferralWatchdog>(_ => bekci);

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

        return (servis, tarayici, bekci);
    }

    [Theory]
    [InlineData("kapali")]
    [InlineData("off")]      // eski İngilizce değer
    [InlineData("saçmalık")] // tanınmayan değer
    [InlineData(null)]       // değişken tanımsız
    public async Task Kapali_TaramaVeBekciyiYineDeKosturur(string? modDegeri)
    {
        var (servis, tarayici, bekci) = Kur(modDegeri);

        // Gönderim yoluna girilseydi `DailySummaryService` çözülemez
        // ve burası fırlardı.
        await servis.BirTurAsync(CancellationToken.None);

        Assert.Equal(1, tarayici.CagriSayisi);
        Assert.Equal(1, bekci.CagriSayisi);
    }

    /// <summary>
    /// Ters yön: mod kapalı DEĞİLSE gönderim yolu gerçekten
    /// aranıyor. Bu olmasaydı yukarıdaki test, bayrak tamamen
    /// işlevsizleşse bile yeşil kalırdı.
    /// </summary>
    [Theory]
    [InlineData("dryrun")]
    [InlineData("acik")]
    [InlineData("on")]
    public async Task KapaliDegilse_GonderimYoluAranir(string modDegeri)
    {
        var (servis, tarayici, bekci) = Kur(modDegeri);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => servis.BirTurAsync(CancellationToken.None));

        // Tarama ve nöbetçi, gönderim yolundan ÖNCE koşmuş olmalı.
        Assert.Equal(1, tarayici.CagriSayisi);
        Assert.Equal(1, bekci.CagriSayisi);
    }
}
