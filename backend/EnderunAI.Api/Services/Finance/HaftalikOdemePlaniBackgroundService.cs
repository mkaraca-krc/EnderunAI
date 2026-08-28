using EnderunAI.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Finance;

/// <summary>
/// PAZARTESİ SABAHI HAFTANIN TASLAK PLANINI OLUŞTURUR (ÖP/1a · D1).
///
/// DESEN `DailySummaryBackgroundService`TEN ALINDI: bir sonraki hedef
/// ana kadar uyu, uyan, işi yap, tekrar uyu. Tek fark hedefin
/// HAFTALIK olması — `DayOfWeek.Monday` kontrolü eklendi.
///
/// D1 ELLE TETİKLENMİYOR: altyapı zaten vardı, yalnız haftalık
/// varyantı yazılmamıştı.
///
/// ZATEN VARSA YENİDEN OLUŞTURMAZ: `HaftalikTaslakOlusturAsync` aynı
/// hafta için ikinci plan açmıyor, mevcut olanı döndürüyor. Servis
/// yeniden başlatıldığında ya da uyanma iki kez tetiklendiğinde
/// mükerrer plan oluşmasın diye — kısmi benzersiz indeks de bunu
/// veritabanı düzeyinde engelliyor.
/// </summary>
public sealed class HaftalikOdemePlaniBackgroundService(
    IServiceProvider serviceProvider,
    ILogger<HaftalikOdemePlaniBackgroundService> logger) : BackgroundService
{
    /// <summary>Pazartesi sabahı 05:00 UTC — günlük özetten (04:00) sonra.</summary>
    public const int OlusturmaSaatiUtc = 5;

    private static readonly TimeSpan BaslangicGecikmesi = TimeSpan.FromMinutes(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(BaslangicGecikmesi, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TurAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // TUR DÜŞERSE SERVİS ÖLMEZ: bir haftanın taslağı
                // oluşmazsa muhasebeci elle açabilir; servisin ölmesi
                // ise sonraki HER haftayı kaybettirir.
                logger.LogError(ex,
                    "Haftalık ödeme planı taslağı oluşturulamadı.");
            }

            await Task.Delay(SonrakiTuraKalan(), stoppingToken);
        }
    }

    private async Task TurAsync(CancellationToken stoppingToken)
    {
        if (DateTime.UtcNow.DayOfWeek != DayOfWeek.Monday) return;

        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<OdemePlaniService>();

        var sirketler = await db.Companies
            .Where(x => x.IsActive && !x.IsDeleted)
            .Select(x => x.Id)
            .ToListAsync(stoppingToken);

        foreach (var sirketId in sirketler)
        {
            // OLUŞTURAN KİŞİ YOK: otomatik taslakta `HazirlayanUserId`
            // boş kalıyor ve muhasebeci ilk dokunuşta sahipleniyor.
            // Sistem kullanıcısı uydurmak, K4'ün "hazırlayan" tarafını
            // bir hayalete bağlamak olurdu.
            await service.HaftalikTaslakOlusturAsync(
                sirketId, DateTime.UtcNow, kullaniciId: null, stoppingToken);
        }

        logger.LogInformation(
            "Haftalık ödeme planı taslağı hazır: {Sayi} şirket.", sirketler.Count);
    }

    /// <summary>Bir sonraki PAZARTESİ 05:00 UTC'ye kalan süre.</summary>
    private static TimeSpan SonrakiTuraKalan() => SonrakiTuraKalan(DateTime.UtcNow);

    /// <summary>
    /// SAF HÂLİ — zaman DIŞARIDAN veriliyor.
    ///
    /// `public`: "pazartesi 05:00 hesabı doğru mu" sorusu, arka plan
    /// servisini çalıştırmadan ve saati beklemeden test edilebilsin
    /// diye. Zamanı içeriden okuyan bir hesap ancak pazartesi sabahı
    /// sınanabilirdi — yani hiç sınanmazdı.
    /// </summary>
    public static TimeSpan SonrakiTuraKalan(DateTime simdiUtc)
    {
        var hedef = new DateTime(
            simdiUtc.Year, simdiUtc.Month, simdiUtc.Day,
            OlusturmaSaatiUtc, 0, 0, DateTimeKind.Utc);

        // Bu haftanın pazartesisine git, sonra gerekiyorsa bir hafta ekle.
        var pazartesiyeKalan = ((int)DayOfWeek.Monday - (int)simdiUtc.DayOfWeek + 7) % 7;
        hedef = hedef.AddDays(pazartesiyeKalan);

        if (hedef <= simdiUtc) hedef = hedef.AddDays(7);

        return hedef - simdiUtc;
    }
}
