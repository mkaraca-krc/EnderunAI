namespace EnderunAI.Api.Services.Notifications;

/// <summary>
/// Bildirimleri günde bir kez tazeler.
///
/// DESEN <see cref="Market.MarketDataBackgroundService"/> İLE AYNI:
/// açılışta kısa bir gecikme, sonra sabit aralıkla tur, her turda
/// hata yutulup bir sonrakine devam. Yeni bir zamanlama bağımlılığı
/// (Hangfire/Quartz) getirilmedi — bu ölçekte çalışan bir emsal
/// varken ikinci bir altyapı, ikinci bir bakım yükü demek.
///
/// UYGULAMANIN AYAĞA KALKMASI TARAMAYA BAĞLI DEĞİL: tarama hata
/// verse de API çalışır. Bildirim üretmek, uygulamayı ayakta tutmaktan
/// daha az önemlidir.
/// </summary>
public sealed class NotificationScanBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationScanBackgroundService> logger) : BackgroundService
{
    /// <summary>
    /// Açılışta kısa gecikme: uygulama sağlık kontrolünden geçmeden
    /// veritabanını tarayıp başlangıcı yavaşlatmasın.
    /// </summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Günde bir tur. Vade hatırlatması gün çözünürlüğünde; daha sık
    /// taramak aynı satırı boşuna güncellemekten başka bir şey
    /// yapmazdı.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

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

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();

                var scanner = scope.ServiceProvider
                    .GetRequiredService<NotificationScanner>();

                var report = await scanner.RunAsync(DateTime.UtcNow, stoppingToken);

                logger.LogInformation(
                    "Bildirim taraması: {Created} yeni, {Updated} güncel, " +
                    "{Closed} kapandı ({CompanyCount} şirket).",
                    report.Created, report.Updated, report.Closed,
                    report.CompanyCount);

                if (report.HasErrors)
                {
                    // Kaynak bazındaki hatalar tarayıcıda zaten
                    // günlüğe yazıldı; burada tur düzeyinde özet.
                    logger.LogWarning(
                        "Bildirim taramasında {Count} kaynak hata verdi.",
                        report.Sources.Count(x => x.Error is not null));
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Arka plan işi hiçbir hatada ölmez; bir sonraki turda
                // tekrar dener.
                logger.LogError(ex, "Bildirim taraması başarısız oldu.");
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
}
