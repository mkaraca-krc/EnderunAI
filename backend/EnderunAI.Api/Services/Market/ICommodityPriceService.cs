using EnderunAI.Api.Models.Market;

namespace EnderunAI.Api.Services.Market;

public sealed record CommodityPricePoint(
    DateTime PriceDate,
    decimal PriceUsdPerTon,
    decimal? PriceTryPerTon,
    decimal? UsdRate);

/// <summary>
/// Emtia özeti: son fiyat, seçilen pencereye göre değişim ve verinin
/// tazeliği. Değişim USD ve TL için AYRI verilir; TL değişimi hem
/// emtia hem kur hareketini içerir ve ikisini karıştırmak "bakır mı
/// pahalandı, lira mı değer kaybetti" sorusunu cevapsız bırakır.
/// </summary>
public sealed record CommoditySummary(
    Commodity Commodity,
    string SourceLabel,
    string SourceSymbol,
    bool IsLme,
    DateTime? LatestDate,
    decimal? LatestUsdPerTon,
    decimal? LatestTryPerTon,
    decimal? UsdRate,
    decimal? ChangePercentUsd,
    decimal? ChangePercentTry,
    decimal? ComparedToUsdPerTon,
    DateTime? ComparedToDate,
    bool IsStale,
    string? Warning,
    IReadOnlyList<CommodityPricePoint> Trend);

public sealed record CommodityRefreshResult(
    int StoredDays,
    int UpdatedDays,
    string SourceLabel,
    string Message,
    IReadOnlyList<string> Errors);

public interface ICommodityPriceService
{
    /// <summary>
    /// Özet + trend. <paramref name="days"/> pencere uzunluğudur (7/30/90).
    /// Arşiv boşsa alanlar null döner; sıfır ya da tahmini fiyat üretilmez.
    /// </summary>
    Task<CommoditySummary> GetSummaryAsync(
        Commodity commodity, int days, CancellationToken cancellationToken = default);

    /// <summary>Aktif kaynaktan fiyatları çeker ve arşivi günceller.</summary>
    Task<CommodityRefreshResult> RefreshAsync(
        int days, CancellationToken cancellationToken = default);

    /// <summary>Bir tarihe uygulanacak fiyat — en yakın önceki işlem günü.</summary>
    Task<CommodityPrice?> GetPriceAsync(
        Commodity commodity, DateTime date, CancellationToken cancellationToken = default);
}
