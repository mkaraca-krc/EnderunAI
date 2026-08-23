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

    private async Task BirTurAsync(CancellationToken cancellationToken)
    {
        var mod = ModuOku();

        using var scope = services.CreateScope();

        // TERMİN UYARILARI MODDAN BAĞIMSIZ: bunlar uygulama içi
        // bildirim, e-posta değil. `off` yalnız E-POSTAYI kapatıyor.
        var terminTarayici = scope.ServiceProvider
            .GetRequiredService<TaskDueNotificationScanner>();

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
        var bekci = scope.ServiceProvider.GetRequiredService<ScopeDeferralWatchdog>();
        await bekci.CheckAsync(cancellationToken);

        if (mod == DailySummaryMode.Off)
        {
            logger.LogInformation(
                "Günlük özet KAPALI (DAILY_SUMMARY_MODE=off). " +
                "Termin uyarısı taraması koştu: {Sayi}.", uyari);
            return;
        }

        var ozet = scope.ServiceProvider.GetRequiredService<DailySummaryService>();

        var gonderilen = await ozet.RunAsync(
            mod, TestAlicilariOku(), cancellationToken);

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
        var ham = configuration["DAILY_SUMMARY_MODE"]?.Trim();

        return ham?.ToLowerInvariant() switch
        {
            "dryrun" => DailySummaryMode.DryRun,
            "test" => DailySummaryMode.Test,
            "on" => DailySummaryMode.On,
            _ => DailySummaryMode.Off
        };
    }

    private IReadOnlyCollection<string> TestAlicilariOku()
    {
        var ham = configuration["DAILY_SUMMARY_TEST_RECIPIENTS"];

        if (string.IsNullOrWhiteSpace(ham))
            return [];

        return ham
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
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
