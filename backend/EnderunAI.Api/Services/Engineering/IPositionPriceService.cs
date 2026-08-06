using EnderunAI.Api.Models;

namespace EnderunAI.Api.Services.Engineering;

public sealed record PositionPriceRow(
    Guid Id,
    int Year,
    PositionPriceInstitution Institution,
    string InstitutionName,
    PositionPriceComponent Component,
    string ComponentName,
    decimal UnitPrice,
    string CurrencyCode,
    DateTime? EffectiveFrom,
    string? SourceNote,
    DateTime CreatedAtUtc);

/// <summary>
/// Çözümlenen fiyat ve bileşen dökümü. <paramref name="UnitPrice"/>
/// keşifte kullanılacak tutar; <paramref name="MaterialPrice"/> ve
/// <paramref name="LaborPrice"/> varsa malzeme/montaj ayrımını verir.
/// </summary>
public sealed record PositionPriceResolution(
    bool Found,
    decimal? UnitPrice,
    decimal? MaterialPrice,
    decimal? LaborPrice,
    string? CurrencyCode,
    int? Year,
    PositionPriceInstitution? Institution,
    string? InstitutionName,
    string? SourceNote,
    string Explanation);

public sealed record UpsertPositionPriceInput(
    int Year,
    PositionPriceInstitution Institution,
    decimal UnitPrice,
    string? CurrencyCode,
    DateTime? EffectiveFrom,
    string? SourceNote,
    PositionPriceComponent Component = PositionPriceComponent.Total);

public interface IPositionPriceService
{
    /// <summary>Pozun tüm fiyat geçmişi — en yeni yıl başta.</summary>
    Task<IReadOnlyList<PositionPriceRow>> GetHistoryAsync(
        Guid positionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Poza uygulanacak fiyatı çözer.
    ///
    /// <paramref name="year"/> verilirse O YILA ait fiyat aranır; daha
    /// eski bir yılın fiyatı otomatik kullanılmaz — 2025 keşfine 2019
    /// fiyatı koymak sessiz bir hata olurdu. Yıl verilmezse en yeni yıl
    /// alınır. Kurum verilirse yalnızca o kurum içinde aranır.
    /// </summary>
    Task<PositionPriceResolution> ResolveAsync(
        Guid positionId,
        int? year = null,
        PositionPriceInstitution? institution = null,
        CancellationToken cancellationToken = default);

    /// <summary>Kurum ve bileşen adlarının insan okur karşılıkları.</summary>
    static string ComponentNameOf(PositionPriceComponent component) => component switch
    {
        PositionPriceComponent.Material => "Malzeme",
        PositionPriceComponent.Labor => "Montaj",
        PositionPriceComponent.Dismantle => "Demontaj",
        PositionPriceComponent.RemountFromDismantled => "Demontajdan montaj",
        _ => "Toplam"
    };

    /// <summary>
    /// Fiyatı ekler; aynı (poz, yıl, kurum) varsa GÜNCELLER. Aynı
    /// kitabın iki kez yüklenmesi satır çoğaltmamalı.
    /// </summary>
    Task<PositionPriceRow> UpsertAsync(
        Guid positionId,
        UpsertPositionPriceInput input,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid priceId, CancellationToken cancellationToken = default);
}
