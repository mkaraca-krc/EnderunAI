using EnderunAI.Api.Models;

namespace EnderunAI.Api.Services.Engineering;

public sealed record PositionPriceRow(
    Guid Id,
    int Year,
    PositionPriceInstitution Institution,
    string InstitutionName,
    decimal UnitPrice,
    string CurrencyCode,
    DateTime? EffectiveFrom,
    string? SourceNote,
    DateTime CreatedAtUtc);

/// <summary>
/// Bir keşif kalemine uygulanacak fiyat ve nereden geldiği. Kaynağın
/// yanıtta dönmesi şart: aynı poz için üç farklı kurum fiyatı olabilir
/// ve hangisinin kullanıldığı bilinmeden rakam savunulamaz.
/// </summary>
public sealed record PositionPriceResolution(
    bool Found,
    decimal? UnitPrice,
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
    string? SourceNote);

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
