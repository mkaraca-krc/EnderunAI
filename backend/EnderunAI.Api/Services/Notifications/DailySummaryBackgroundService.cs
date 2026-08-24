using Microsoft.Extensions.Options;

namespace EnderunAI.Api.Services.Notifications;

/// <summary>
/// GÜNLÜK ÖZET ZAMANLAYICISI — 04:00 UTC.
///
/// SAAT DİLİMİ AÇIKÇA YAZILI: sunucu `Etc/UTC` ve Türkiye sabit
/// UTC+3 (yaz saati uygulaması YOK). 07:00 Türkiye = 04:00 UTC.
/// Kodda "07:00" yazıp sunucunun UTC olduğunu unutmak, özetin sabah
/// 10'da gitmesi demekti.
///
/// TERMİN TARAYICISI DA BURADA: iki iş de günlük ve aynı anda
/// koşmaları sorun değil — ayrı bir zamanlayıcı, ayrı bir hata
/// yüzeyi olurdu.
/// </summary>
public sealed class DailySummaryBackgroundService(
    IServiceProvider services,
    IConfiguration configuration,
    ILogger<DailySummaryBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan BaslangicGecikmesi = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(BaslangicGecikmesi, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await BirTurAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                // TUR ÇÖKSE BİLE SERVİS AYAKTA KALIR: bir günün
                // hatası ertesi günü de kaybettirmemeli.
                logger.LogError(exception, "Günlük özet turu başarısız oldu.");
            }

            await Task.Delay(SonrakiTuraKalan(), stoppingToken);
        }
    }

    /// <summary>
    /// TEK TUR — testlerin doğrudan çağırdığı giriş noktası.
    /// Hangi adımın hangi modda koştuğu buradan çağrı sayacıyla
    /// doğrulanıyor (DURUM.md §5 kural 23).
    /// </summary>
    public async Task BirTurAsync(CancellationToken cancellationToken)
    {
        var ham = configuration["DAILY_SUMMARY_MODE"]?.Trim();
        var mod = ModuOku();

        using var scope = services.CreateScope();

        // TERMİN UYARILARI MODDAN BAĞIMSIZ: bunlar uygulama içi
        // bildirim, e-posta değil. `off` yalnız E-POSTAYI kapatıyor.
        var terminTarayici = scope.ServiceProvider
            .GetRequiredService<ITaskDueNotificationScanner>();

        var uyari = await terminTarayici.ScanAsync(cancellationToken);

        /*
         * G3 ERTELEME BEKÇİSİ — MODDAN BAĞIMSIZ.
         *
         * `DAILY_SUMMARY_MODE=off` yalnız E-POSTAYI kapatıyor. Bu
         * kontrol bir bildirim değil, bir GÜVENLİK UYARISI: kapsam
         * ertelemesinin gerekçesi ortadan kalktığında haber vermesi
         * gerekiyor ve bunun e-posta bayrağına bağlanması yanlış
         * olurdu.
         */
        var bekci = scope.ServiceProvider.GetRequiredService<IScopeDeferralWatchdog>();
        await bekci.CheckAsync(cancellationToken);

        if (mod == DailySummaryMode.Kapali)
        {
            /*
             * HAM DEĞER DE YAZILIYOR.
             *
             * Önce yorumlanmış değer sabit metin olarak yazılıyordu
             * ("=kapali"). Ortam değişkeninde `off` yazarken kayıtta
             * `kapali` görmek, teşhis sırasında yanlış dosyaya
             * baktırır. Ham değer sır değil; kapı adı.
             */
            logger.LogInformation(
                "Günlük özet KAPALI (DAILY_SUMMARY_MODE={Ham} → {Mod}). " +
                "Termin uyarısı taraması koştu: {Sayi}.",
                ham ?? "(tanımsız)", mod, uyari);
            return;
        }

        var ozet = scope.ServiceProvider.GetRequiredService<IDailySummaryRunner>();

        var gonderilen = await ozet.RunAsync(mod, cancellationToken);

        logger.LogInformation(
            "Günlük özet turu bitti. Mod={Mod} gonderilen={Gonderilen} " +
            "terminUyarisi={Uyari}", mod, gonderilen, uyari);
    }

    /// <summary>
    /// TANIMSIZ DEĞİŞKEN → `off`. Sessizce e-posta göndermeye
    /// başlamak, tanımsız bir değişkenin kabul edilebilir sonucu
    /// değil.
    /// </summary>
    private DailySummaryMode ModuOku()
    {
        var ham = configuration["DAILY_SUMMARY_MODE"];
        var mod = ModCozumle(ham, out var taninmadi);

        if (taninmadi)
        {
            /*
             * TANINMAYAN DEĞER SESSİZ KALMAZ.
             *
             * `Kapali`'ya düşmek doğru davranış, ama SESSİZCE düşmek
             * değil: `DAILY_SUMMARY_MODE=dryrunn` yazan kişi
             * özetin koştuğunu sanır ve haftalarca boş kayda bakar.
             * Ham değer sır değil, kapı adı — kayda yazılabilir.
             */
            logger.LogWarning(
                "DAILY_SUMMARY_MODE tanınmayan bir değer taşıyor: " +
                "\"{Ham}\". Güvenli tarafa düşüldü: {Mod}. Geçerli " +
                "değerler: kapali, dryrun, acik.",
                ham, mod);
        }

        return mod;
    }

    /// <summary>
    /// HAM DEĞER → MOD. Saf fonksiyon; testler doğrudan çağırıyor.
    ///
    /// EŞLEME TABLOSU (büyük/küçük harf ve baştaki/sondaki boşluk
    /// önemsiz):
    ///   "kapali", "off"        → <see cref="DailySummaryMode.Kapali"/>
    ///   "dryrun"               → <see cref="DailySummaryMode.DryRun"/>
    ///   "acik", "on"           → <see cref="DailySummaryMode.Acik"/>
    ///   null, "", "   "        → Kapali (tanımsız)
    ///   başka her şey          → Kapali + UYARI KAYDI
    ///
    /// EN ÖNEMLİ KURAL: <see cref="DailySummaryMode.Acik"/>'a YALNIZ
    /// açıkça "acik"/"on" yazılırsa düşülür. Hiçbir yazım hatası,
    /// hiçbir boş değer, hiçbir tanımsız değişken gerçek insanlara
    /// e-posta göndermeye başlatamaz. Varsayılanın yanlış tarafı
    /// geri alınamaz bir hata olurdu.
    ///
    /// `off`/`on` geriye uyum için duruyor: deploy sırasında ortam
    /// değişkeni eski değerde kalmışsa davranış SESSİZCE değişmesin.
    /// </summary>
    public static DailySummaryMode ModCozumle(string? ham, out bool taninmadi)
    {
        var temiz = ham?.Trim();

        if (string.IsNullOrEmpty(temiz))
        {
            // Tanımsız/boş: uyarı YAZILIR ama yalnızca değişken
            // TANIMLIYSA — hiç tanımlanmamış olmak beklenen durum.
            taninmadi = ham is not null;
            return DailySummaryMode.Kapali;
        }

        switch (temiz.ToLowerInvariant())
        {
            case "dryrun":
                taninmadi = false;
                return DailySummaryMode.DryRun;

            case "acik":
            case "on":
                taninmadi = false;
                return DailySummaryMode.Acik;

            case "kapali":
            case "off":
                taninmadi = false;
                return DailySummaryMode.Kapali;

            default:
                taninmadi = true;
                return DailySummaryMode.Kapali;
        }
    }


    /// <summary>Bir sonraki 04:00 UTC'ye kalan süre.</summary>
    private static TimeSpan SonrakiTuraKalan()
    {
        var simdi = DateTime.UtcNow;

        var hedef = new DateTime(
            simdi.Year, simdi.Month, simdi.Day,
            DailySummaryService.GonderimSaatiUtc, 0, 0, DateTimeKind.Utc);

        if (hedef <= simdi)
            hedef = hedef.AddDays(1);

        return hedef - simdi;
    }
}
