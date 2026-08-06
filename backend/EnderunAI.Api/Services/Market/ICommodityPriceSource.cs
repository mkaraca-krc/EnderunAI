using EnderunAI.Api.Models.Market;

namespace EnderunAI.Api.Services.Market;

public sealed record CommodityFetchResult(
    IReadOnlyList<CommodityQuote> Quotes,
    string? Error);

/// <summary>
/// Emtia fiyatı kaynağı. Birden fazla uygulaması vardır ve hangisinin
/// devrede olduğu ekranda daima yazar; kullanıcı LME mi COMEX mi
/// baktığını bilmeden karar veremez.
/// </summary>
public interface ICommodityPriceSource
{
    CommodityPriceSourceKind Kind { get; }

    /// <summary>Kaynaktaki sembol — mutabakat ve günlük için.</summary>
    string Symbol { get; }

    /// <summary>Ekranda gösterilecek insan okur etiket.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Son <paramref name="days"/> günün USD/ton kapanışlarını getirir.
    /// Kaynak erişilemezse istisna fırlatmaz; boş liste ve hata metni
    /// döner, çağıran arşivdeki son fiyatla devam eder.
    /// </summary>
    Task<CommodityFetchResult> GetDailyPricesAsync(
        int days, CancellationToken cancellationToken = default);
}
