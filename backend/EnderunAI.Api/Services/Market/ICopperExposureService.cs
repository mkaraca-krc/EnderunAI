namespace EnderunAI.Api.Services.Market;

/// <summary>Kalan tonajın nereden geldiği — ekranda daima yazar.</summary>
public enum CopperTonnageSource
{
    /// <summary>Ne elle girildi ne icmalden türetilebildi.</summary>
    Unknown = 0,

    /// <summary>Kullanıcı elle girdi.</summary>
    Manual = 1,

    /// <summary>İcmal kalemlerindeki bakır katsayılarından toplandı.</summary>
    BillOfQuantities = 2
}

/// <summary>
/// Bakır ve kur hareketinin projenin kalan işine tahmini etkisi.
///
/// Üç bileşen AYRI verilir ve toplamları TL etkisini verir:
/// - <paramref name="CopperEffect"/>: yalnız emtia hareketi, taban kurla
/// - <paramref name="FxEffect"/>: yalnız kur hareketi, taban fiyatla
/// - <paramref name="CombinedEffect"/>: ikisinin çarpım artığı
///
/// Artık ayrı gösterilir; birine sessizce eklenirse "bakır mı, kur mu"
/// sorusunun cevabı bozulur.
/// </summary>
public sealed record ProjectCopperImpact(
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    int ContractType,
    string ContractTypeName,
    /// <summary>Anahtar teslimde etki doğrudan kâr erozyonudur.</summary>
    bool IsCostRisk,
    CopperTonnageSource TonnageSource,
    string TonnageSourceName,
    decimal? RemainingTons,
    DateTime? BaselineDate,
    string? BaselineReason,
    decimal? BaselineUsdPerTon,
    decimal? BaselineUsdRate,
    decimal? CurrentUsdPerTon,
    decimal? CurrentUsdRate,
    decimal? CopperChangePercent,
    decimal? FxChangePercent,
    decimal? CopperEffect,
    decimal? FxEffect,
    decimal? CombinedEffect,
    decimal? TotalEffect,
    IReadOnlyList<string> Assumptions);

public sealed record CopperExposureInput(
    decimal? RemainingTons,
    DateTime? BaselineDate,
    string? Note);

public interface ICopperExposureService
{
    Task<ProjectCopperImpact?> GetForProjectAsync(
        Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Açık projelerin tamamı — dashboard ve brifing bunu okur.</summary>
    Task<IReadOnlyList<ProjectCopperImpact>> GetPortfolioAsync(
        Guid? companyId, CancellationToken cancellationToken = default);

    Task<ProjectCopperImpact?> SaveExposureAsync(
        Guid projectId, CopperExposureInput input, CancellationToken cancellationToken = default);
}
