using EnderunAI.Api.Data;
using EnderunAI.Api.Models.Market;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Market;

/// <summary>
/// Kur arşivinin okuma ve tazeleme yüzü.
///
/// Tek kuralı var: <b>kur uydurulmaz</b>. Arşivde kayıt yoksa null döner,
/// ara değer hesaplanmaz, "yaklaşık şu olur" denmez. Dövizli fiş kesecek
/// tarafın buna bakıp işlemi reddetmesi beklenir — yanlış kurla kesilmiş
/// bir fiş, hiç kesilmemiş fişten çok daha pahalıya patlar.
///
/// Sistemin ilgilendiği para birimleri sabit tutuldu: TCMB bülteninde 20+
/// döviz var, hepsini saklamak arşivi gereksiz şişirir.
/// </summary>
public sealed class ExchangeRateService(
    AppDbContext db,
    ITcmbRateClient client,
    ILogger<ExchangeRateService> logger) : IExchangeRateService
{
    /// <summary>Arşivlenen para birimleri.</summary>
    public static readonly string[] TrackedCurrencies = ["USD", "EUR", "GBP"];

    /// <summary>
    /// Arşivin bu kadar gün güncellenmemesi "güncellenemedi" uyarısı
    /// doğurur. 4 gün: hafta sonu (2 gün) + araya giren bir tatil payı.
    /// </summary>
    private const int StaleThresholdDays = 4;

    public async Task<ExchangeRateLookup?> GetAsync(
        string currencyCode, DateTime date, CancellationToken cancellationToken = default)
    {
        var code = Normalize(currencyCode);
        var target = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);

        // Yerel para birimi: kur kavramı yok, çevrim yapılmaz.
        if (code is "TRY")
        {
            return new ExchangeRateLookup(
                "TRY", target, target, 1m, 1m, "Yerel para birimi");
        }

        var rate = await db.ExchangeRates
            .AsNoTracking()
            .Where(x => x.CurrencyCode == code && x.RateDate <= target)
            .OrderByDescending(x => x.RateDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (rate is null)
            return null;

        return new ExchangeRateLookup(
            rate.CurrencyCode,
            target,
            rate.RateDate,
            rate.ForexBuying,
            rate.ForexSelling,
            rate.Source);
    }

    public async Task<IReadOnlyList<ExchangeRate>> GetRangeAsync(
        string currencyCode,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        var code = Normalize(currencyCode);
        var start = DateTime.SpecifyKind(from.Date, DateTimeKind.Utc);
        var end = DateTime.SpecifyKind(to.Date, DateTimeKind.Utc);

        return await db.ExchangeRates
            .AsNoTracking()
            .Where(x => x.CurrencyCode == code && x.RateDate >= start && x.RateDate <= end)
            .OrderBy(x => x.RateDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<ExchangeRateFreshness> GetFreshnessAsync(
        CancellationToken cancellationToken = default)
    {
        var latest = await db.ExchangeRates
            .AsNoTracking()
            .Where(x => x.CurrencyCode == "USD")
            .OrderByDescending(x => x.RateDate)
            .Select(x => (DateTime?)x.RateDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is null)
        {
            return new ExchangeRateFreshness(
                null, null, true,
                "Kur arşivi boş — TCMB'ye hiç ulaşılamadı. Dövizli işlem yapılamaz.");
        }

        var days = (DateTime.UtcNow.Date - latest.Value.Date).Days;
        var isStale = days >= StaleThresholdDays;

        return new ExchangeRateFreshness(
            latest,
            days,
            isStale,
            isStale
                ? $"Kurlar {latest:dd.MM.yyyy} tarihinden beri güncellenemedi " +
                  $"({days} gün). Gösterilen tutarlar son bilinen kurla hesaplandı."
                : null);
    }

    public async Task<ExchangeRateRefreshResult> RefreshAsync(
        DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        var start = DateTime.SpecifyKind(from.Date, DateTimeKind.Utc);
        var end = DateTime.SpecifyKind(to.Date, DateTimeKind.Utc);

        if (end < start)
            (start, end) = (end, start);

        var existing = await db.ExchangeRates
            .AsNoTracking()
            .Where(x => x.RateDate >= start && x.RateDate <= end && x.CurrencyCode == "USD")
            .Select(x => x.RateDate)
            .ToListAsync(cancellationToken);

        var present = existing.ToHashSet();

        var fetched = 0;
        var skipped = 0;
        var unavailable = 0;
        var errors = new List<string>();

        for (var day = start; day <= end; day = day.AddDays(1))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (present.Contains(day))
            {
                skipped++;
                continue;
            }

            // Hafta sonu bülteni hiç yayımlanmaz; boşuna istek atılmaz.
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                unavailable++;
                continue;
            }

            var (bulletin, error) = await client.GetBulletinAsync(day, cancellationToken);

            if (error is not null)
            {
                errors.Add(error);
                continue;
            }

            if (bulletin is null)
            {
                unavailable++;
                continue;
            }

            // Bültenin kendi tarihi esas alınır: TCMB istenen gün yerine
            // en son bülteni döndürebiliyor, o zaman yanlış güne yazılırdı.
            if (present.Contains(bulletin.RateDate))
            {
                skipped++;
                continue;
            }

            var stored = await StoreAsync(bulletin, cancellationToken);
            if (stored)
            {
                present.Add(bulletin.RateDate);
                fetched++;
            }
            else
            {
                skipped++;
            }
        }

        if (fetched > 0)
            await db.SaveChangesAsync(cancellationToken);

        var message = fetched > 0
            ? $"{fetched} günlük kur çekildi."
            : errors.Count > 0
                ? "Kurlar güncellenemedi; arşivdeki son kurlar geçerli."
                : "Kur arşivi zaten güncel.";

        if (errors.Count > 0)
            logger.LogWarning("Kur güncellemede {Count} hata: {First}", errors.Count, errors[0]);

        return new ExchangeRateRefreshResult(fetched, skipped, unavailable, errors, message);
    }

    private async Task<bool> StoreAsync(
        TcmbBulletin bulletin, CancellationToken cancellationToken)
    {
        var alreadyStored = await db.ExchangeRates
            .AnyAsync(x => x.RateDate == bulletin.RateDate, cancellationToken);

        if (alreadyStored)
            return false;

        var any = false;

        foreach (var row in bulletin.Rows)
        {
            if (!TrackedCurrencies.Contains(row.CurrencyCode))
                continue;

            db.ExchangeRates.Add(new ExchangeRate
            {
                RateDate = bulletin.RateDate,
                CurrencyCode = row.CurrencyCode,
                Unit = 1,
                ForexBuying = row.ForexBuying,
                ForexSelling = row.ForexSelling,
                BanknoteBuying = row.BanknoteBuying,
                BanknoteSelling = row.BanknoteSelling,
                BulletinNumber = bulletin.BulletinNumber,
                Source = "TCMB",
                FetchedAtUtc = DateTime.UtcNow
            });

            any = true;
        }

        return any;
    }

    private static string Normalize(string? currencyCode) =>
        string.IsNullOrWhiteSpace(currencyCode)
            ? "TRY"
            : currencyCode.Trim().ToUpperInvariant();
}
