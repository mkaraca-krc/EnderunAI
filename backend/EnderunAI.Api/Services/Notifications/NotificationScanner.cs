using EnderunAI.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Notifications;

/// <summary>Bir kaynağın tur sonucu — günlüğe ve uca okunur çıktı.</summary>
public sealed record NotificationSourceReport(
    string Source,
    int Created,
    int Updated,
    int Closed,
    string? Error);

public sealed record NotificationScanReport(
    DateTime ScanTimeUtc,
    int CompanyCount,
    IReadOnlyList<NotificationSourceReport> Sources)
{
    public int Created => Sources.Sum(x => x.Created);
    public int Updated => Sources.Sum(x => x.Updated);
    public int Closed => Sources.Sum(x => x.Closed);
    public bool HasErrors => Sources.Any(x => x.Error is not null);
}

/// <summary>
/// Bütün kaynakları tarayıp bildirimleri tazeler.
///
/// KAYNAK BAŞINA YALITIM: bir kaynak hata verirse yalnız o kaynak
/// atlanır, tur devam eder. Hata bütün turu düşürseydi tek bozuk
/// sorgu yüzünden o gece hiçbir hatırlatma üretilmezdi. Hata
/// bastırılmıyor: rapora ve günlüğe yazılıyor.
///
/// HATA VEREN KAYNAK KAPATMA YAPMAZ: <see cref="NotificationStore"/>
/// yalnız başarıyla üretilen adaylarla çağrılıyor. Hata durumunda
/// boş liste geçilseydi, o kaynağın bütün açık bildirimleri "kaynak
/// kalktı" sayılıp sessizce kapanırdı.
/// </summary>
public sealed class NotificationScanner(
    AppDbContext db,
    IServiceScopeFactory scopeFactory,
    IEnumerable<INotificationSource> sources,
    ILogger<NotificationScanner> logger)
{
    public async Task<NotificationScanReport> RunAsync(
        DateTime nowUtc, CancellationToken cancellationToken)
    {
        var companyIds = await db.Companies
            .AsNoTracking()
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var reports = new List<NotificationSourceReport>();

        foreach (var source in sources)
        {
            var created = 0;
            var updated = 0;
            var closed = 0;
            string? error = null;

            foreach (var companyId in companyIds)
            {
                try
                {
                    // YAZMA HER TURDA TAZE BİR BAĞLAMDA.
                    //
                    // Kaynaklar tek bir DbContext'i paylaşsaydı, birinin
                    // yazamadığı bozuk kayıt izleyicide asılı kalır ve
                    // SONRAKİ kaynakların SaveChanges'i de düşerdi —
                    // tek bir hatalı kaynak bütün turu sessizce
                    // sakatlardı. Kaynaklar yalnız OKUYOR, o yüzden
                    // tazelenen tek şey depo.
                    using var scope = scopeFactory.CreateScope();

                    var store = scope.ServiceProvider
                        .GetRequiredService<NotificationStore>();

                    var context = new NotificationScanContext(companyId, nowUtc.Date);

                    var candidates = await source.BuildAsync(
                        context, cancellationToken);

                    // Türler ADAYLARDAN çıkarılmıyor: kaynak bu turda
                    // hiç aday üretmese bile kendi türlerini kapatmalı.
                    // Adaylardan çıkarılsaydı, çözülen son iş kapanmaz
                    // ve bildirim ilelebet açık kalırdı.
                    var ownedTypes = source.OwnedTypes;

                    var result = await store.ApplyAsync(
                        companyId, ownedTypes, candidates, nowUtc, cancellationToken);

                    created += result.Created;
                    updated += result.Updated;
                    closed += result.Closed;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Bir kaynağın bir şirketteki hatası turu düşürmez.
                    error = ex.Message;

                    logger.LogError(
                        ex,
                        "Bildirim kaynağı {Source} şirket {CompanyId} için başarısız oldu.",
                        source.Key, companyId);
                }
            }

            reports.Add(new NotificationSourceReport(
                source.Key, created, updated, closed, error));
        }

        return new NotificationScanReport(nowUtc, companyIds.Count, reports);
    }
}
