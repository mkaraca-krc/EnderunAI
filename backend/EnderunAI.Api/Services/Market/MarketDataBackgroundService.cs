namespace EnderunAI.Api.Services.Market;

/// <summary>
/// Piyasa verisini (TCMB kurları) arka planda tazeler.
///
/// Açılışta son 90 günün eksiklerini tamamlar, sonra günde bir kez
/// çalışır. Dış kaynak çökerse iş durmaz: hata günlüğe yazılır, bir
/// sonraki turda yeniden denenir. Uygulamanın ayağa kalkması hiçbir
/// koşulda TCMB'ye bağlı değildir.
/// </summary>
public sealed class MarketDataBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<MarketDataBackgroundService> logger) : BackgroundService
{
    /// <summary>Açılışta tamamlanacak geçmiş gün sayısı.</summary>
    private const int BackfillDays = 90;

    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    /// <summary>
    /// Açılışta kısa bir gecikme: uygulama sağlık kontrolünden geçmeden
    /// dış ağa çıkıp başlangıcı yavaşlatmasın.
    /// </summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var backfilled = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(
                    backfilled ? 7 : BackfillDays, stoppingToken);

                backfilled = true;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Arka plan işi hiçbir hatada ölmez; bir sonraki turda tekrar dener.
                logger.LogError(ex, "Piyasa verisi güncellemesi başarısız oldu.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RunOnceAsync(int days, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var rates = scope.ServiceProvider.GetRequiredService<IExchangeRateService>();

        var today = DateTime.UtcNow.Date;
        var result = await rates.RefreshAsync(
            today.AddDays(-days), today, cancellationToken);

        logger.LogInformation(
            "Kur güncellemesi: {Message} (atlanan {Skipped}, bülten yok {Unavailable})",
            result.Message,
            result.AlreadyPresentDays,
            result.UnavailableDays);

        // Emtia kurdan SONRA çekilir: TL karşılığı fiyatın kendi
        // günündeki kurla hesaplanıyor, kur önce arşivde olmalı.
        // Emtia kaynağı çökerse kur güncellemesi boşa gitmesin diye
        // hata burada yutulur, bir sonraki turda yeniden denenir.
        try
        {
            var commodities = scope.ServiceProvider
                .GetRequiredService<ICommodityPriceService>();

            var commodityResult = await commodities.RefreshAsync(days, cancellationToken);

            logger.LogInformation(
                "Emtia güncellemesi: {Message}", commodityResult.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Emtia fiyatı güncellenemedi.");
        }
    }
}
