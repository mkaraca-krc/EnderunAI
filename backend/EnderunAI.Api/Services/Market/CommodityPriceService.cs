using EnderunAI.Api.Data;
using EnderunAI.Api.Models.Market;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Market;

/// <summary>
/// Emtia fiyat arşivi.
///
/// Kur arşivinden bir farkı var: borsanın günü kapanmadan çekilen bar
/// gün içinde değişir. O yüzden saklama "varsa atla" değil, "değiştiyse
/// güncelle" mantığıyla çalışır — aksi halde arşivde sabahın fiyatı
/// donar kalır.
///
/// TL karşılığı fiyatın KENDİ günündeki TCMB döviz alışıyla hesaplanır.
/// Kur o güne bulunamazsa TL karşılığı boş bırakılır; bugünkü kurla
/// geçmiş bir fiyatı çarpmak ne emtia ne kur hareketini gösteren
/// üçüncü bir sayı üretir.
/// </summary>
public sealed class CommodityPriceService(
    AppDbContext db,
    ICommodityPriceSource source,
    IExchangeRateService exchangeRates,
    ILogger<CommodityPriceService> logger) : ICommodityPriceService
{
    /// <summary>Arşiv bu kadar gün güncellenmemişse uyarı çıkar.</summary>
    private const int StaleThresholdDays = 4;

    public async Task<CommodityPrice?> GetPriceAsync(
        Commodity commodity, DateTime date, CancellationToken cancellationToken = default)
    {
        var target = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);

        return await db.CommodityPrices
            .AsNoTracking()
            .Where(x => x.Commodity == commodity && x.PriceDate <= target)
            .OrderByDescending(x => x.PriceDate)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<CommoditySummary> GetSummaryAsync(
        Commodity commodity, int days, CancellationToken cancellationToken = default)
    {
        var window = days is > 0 and <= 365 ? days : 30;
        var from = DateTime.UtcNow.Date.AddDays(-window);

        var trend = await db.CommodityPrices
            .AsNoTracking()
            .Where(x => x.Commodity == commodity && x.PriceDate >= from)
            .OrderBy(x => x.PriceDate)
            .Select(x => new CommodityPricePoint(
                x.PriceDate, x.PriceUsdPerTon, x.PriceTryPerTon, x.UsdRate))
            .ToListAsync(cancellationToken);

        var latest = trend.Count > 0 ? trend[^1] : null;
        var earliest = trend.Count > 1 ? trend[0] : null;

        if (latest is null)
        {
            return new CommoditySummary(
                commodity,
                source.DisplayName,
                source.Symbol,
                source.Kind == CommodityPriceSourceKind.Lme,
                null, null, null, null, null, null, null, null,
                IsStale: true,
                Warning: "Emtia fiyat arşivi boş — kaynağa hiç ulaşılamadı.",
                Trend: trend);
        }

        var staleDays = (DateTime.UtcNow.Date - latest.PriceDate.Date).Days;
        var isStale = staleDays >= StaleThresholdDays;

        return new CommoditySummary(
            commodity,
            source.DisplayName,
            source.Symbol,
            source.Kind == CommodityPriceSourceKind.Lme,
            latest.PriceDate,
            latest.PriceUsdPerTon,
            latest.PriceTryPerTon,
            latest.UsdRate,
            Percent(earliest?.PriceUsdPerTon, latest.PriceUsdPerTon),
            Percent(earliest?.PriceTryPerTon, latest.PriceTryPerTon),
            earliest?.PriceUsdPerTon,
            earliest?.PriceDate,
            isStale,
            isStale
                ? $"Emtia fiyatı {latest.PriceDate:dd.MM.yyyy} tarihinden beri " +
                  $"güncellenemedi ({staleDays} gün)."
                : null,
            trend);
    }

    public async Task<CommodityRefreshResult> RefreshAsync(
        int days, CancellationToken cancellationToken = default)
    {
        var window = days is > 0 and <= 365 ? days : 30;
        var fetch = await source.GetDailyPricesAsync(window, cancellationToken);

        if (fetch.Quotes.Count == 0)
        {
            var reason = fetch.Error ?? "Kaynaktan fiyat gelmedi.";
            logger.LogWarning("Emtia fiyatı güncellenemedi: {Reason}", reason);

            return new CommodityRefreshResult(
                0, 0, source.DisplayName,
                "Emtia fiyatı güncellenemedi; arşivdeki son fiyat geçerli.",
                [reason]);
        }

        var dates = fetch.Quotes.Select(x => x.PriceDate).ToList();

        var existing = await db.CommodityPrices
            .Where(x => x.Commodity == Commodity.Copper && dates.Contains(x.PriceDate))
            .ToListAsync(cancellationToken);

        var byDate = existing.ToDictionary(x => x.PriceDate);

        var stored = 0;
        var updated = 0;

        foreach (var quote in fetch.Quotes)
        {
            var rate = await exchangeRates.GetAsync("USD", quote.PriceDate, cancellationToken);

            // Kur yalnızca fiyat gününe veya öncesine aitse kullanılır;
            // GetAsync zaten ileriye bakmaz.
            var usdRate = rate?.ForexBuying;
            var tryPerTon = usdRate is null
                ? (decimal?)null
                : decimal.Round(quote.Price * usdRate.Value, 2);

            if (byDate.TryGetValue(quote.PriceDate, out var row))
            {
                if (row.PriceUsdPerTon == quote.Price
                    && row.UsdRate == usdRate
                    && row.PriceTryPerTon == tryPerTon)
                {
                    continue;
                }

                row.PriceUsdPerTon = quote.Price;
                row.UsdRate = usdRate;
                row.PriceTryPerTon = tryPerTon;
                row.SourceKind = source.Kind;
                row.SourceSymbol = source.Symbol;
                row.FetchedAtUtc = DateTime.UtcNow;
                row.UpdatedAtUtc = DateTime.UtcNow;

                updated++;
                continue;
            }

            db.CommodityPrices.Add(new CommodityPrice
            {
                PriceDate = quote.PriceDate,
                Commodity = Commodity.Copper,
                SourceKind = source.Kind,
                SourceSymbol = source.Symbol,
                PriceUsdPerTon = quote.Price,
                UsdRate = usdRate,
                PriceTryPerTon = tryPerTon,
                FetchedAtUtc = DateTime.UtcNow
            });

            stored++;
        }

        if (stored > 0 || updated > 0)
            await db.SaveChangesAsync(cancellationToken);

        var message = stored > 0 || updated > 0
            ? $"{stored} yeni, {updated} güncellenen gün ({source.DisplayName})."
            : "Emtia arşivi zaten güncel.";

        return new CommodityRefreshResult(
            stored, updated, source.DisplayName, message,
            fetch.Error is null ? [] : [fetch.Error]);
    }

    private static decimal? Percent(decimal? from, decimal? to)
    {
        if (from is null or 0 || to is null)
            return null;

        return decimal.Round((to.Value - from.Value) / from.Value * 100m, 2);
    }
}
