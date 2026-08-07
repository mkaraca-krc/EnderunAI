using EnderunAI.Api.Models;
using EnderunAI.Api.Formatting;

namespace EnderunAI.Api.Services.Hakedis;

/// <summary>
/// Bir kalemin sapmasının, sözleşme tipine göre ne anlama geldiği.
/// Renk kodu doğrudan buradan çıkar.
/// </summary>
public enum DeviationImpact
{
    /// <summary>Sapma yok veya ihmal edilebilir.</summary>
    None = 0,

    /// <summary>
    /// Birim fiyatlıda keşif üstü gerçekleşme: yapılan iş kadar ödendiği
    /// için ilave hakediş fırsatı (yeşil).
    /// </summary>
    Opportunity = 1,

    /// <summary>
    /// Anahtar teslimde keşif üstü gerçekleşme: bedel sabit olduğu için
    /// doğrudan kâr erozyonu (kırmızı).
    /// </summary>
    ProfitErosion = 2,

    /// <summary>
    /// Anahtar teslimde keşif altı gerçekleşme: tasarruf (yeşil).
    /// </summary>
    Saving = 3,

    /// <summary>
    /// Birim fiyatlıda keşif altı gerçekleşme: yalnızca bilgi — hak
    /// ediş de o kadar az olur, kâr etkisi yok (gri).
    /// </summary>
    Information = 4,

    /// <summary>
    /// Sözleşme tipi belirlenmemiş; sapma yorumlanmaz. Yanlış varsayım
    /// yanlış alarm üretirdi.
    /// </summary>
    Undetermined = 5
}

/// <summary>Takip tablosunun tek bir kalemi.</summary>
public sealed record TrackingItemInput(
    string PositionCode,
    string Description,
    string Unit,
    Guid? SectionId,
    string? SectionName,
    decimal ContractQuantity,
    decimal RealizedQuantity,
    decimal UnitPrice,
    /// <summary>Stoktan bu projeye çıkılan miktar; eşleşme yoksa null.</summary>
    decimal? IssuedStockQuantity,
    ProjectContractType EffectiveContractType);

public sealed record TrackingItemResult(
    string PositionCode,
    string Description,
    string Unit,
    Guid? SectionId,
    string? SectionName,
    decimal ContractQuantity,
    decimal RealizedQuantity,
    decimal RemainingQuantity,
    decimal DeviationQuantity,
    decimal DeviationRate,
    decimal ContractAmount,
    decimal RealizedAmount,
    decimal DeviationAmount,
    decimal? IssuedStockQuantity,
    ProjectContractType EffectiveContractType,
    DeviationImpact Impact,
    bool ExceedsWarningThreshold);

public sealed record TrackingTotals(
    decimal ContractAmount,
    decimal RealizedAmount,
    /// <summary>Keşif üstü gerçekleşmelerin toplam TL etkisi (pozitif).</summary>
    decimal OverrunAmount,
    /// <summary>Keşif altı kalmaların toplam TL etkisi (pozitif).</summary>
    decimal UnderrunAmount,
    decimal NetDeviationAmount,
    /// <summary>Fiziksel gerçekleşme oranı (%): Σ(gerçekleşen×BF) ÷ Σ(sözleşme×BF).</summary>
    decimal PhysicalCompletionRate,
    int ItemCount,
    int WarningItemCount);

/// <summary>Kâr tahmini; üretilemiyorsa <see cref="IsReliable"/> false.</summary>
public sealed record ProfitEstimate(
    bool IsReliable,
    string? UnreliableReason,
    decimal ContractAmount,
    decimal ActualCost,
    decimal PhysicalCompletionRate,
    decimal EstimatedTotalCost,
    decimal EstimatedProfit,
    decimal EstimatedProfitRate);

/// <summary>
/// Keşif–gerçekleşen karşılaştırması. Veritabanına ve zamana bağlı
/// değil; aynı girdi hep aynı çıktıyı verir (bordro, tazminat ve hakediş
/// motorlarıyla aynı desen).
///
/// İşin özü tek cümlede: aynı sapma birim fiyatlı işte fırsat, anahtar
/// teslimde zarardır. Renk ve alarm bu ayrımdan çıkar.
/// </summary>
public static class ProgressTrackingCalculator
{
    /// <summary>
    /// Kalem uyarı eşiği: gerçekleşen sözleşmenin bu oranını aşarsa
    /// dashboard ve brifing uyarısı üretilir.
    /// </summary>
    public const decimal ItemWarningThresholdRate = 110m;

    /// <summary>
    /// Bu oranın altında fiziksel gerçekleşmede kâr tahmini üretilmez;
    /// bölen küçüldükçe ekstrapolasyon anlamsızlaşır ve yanıltır.
    /// </summary>
    public const decimal MinimumCompletionForProfitEstimate = 10m;

    public static TrackingItemResult CalculateItem(TrackingItemInput input)
    {
        var contract = Round(input.ContractQuantity);
        var realized = Round(input.RealizedQuantity);
        var unitPrice = Round(input.UnitPrice);

        var deviation = Round(realized - contract);

        // Sözleşme miktarı sıfırken oran hesaplanamaz; kalem tamamen
        // keşif dışıdır (ilave iş).
        var deviationRate = contract > 0m
            ? Round(deviation / contract * 100m)
            : 0m;

        var impact = ResolveImpact(input.EffectiveContractType, deviation);

        return new TrackingItemResult(
            PositionCode: input.PositionCode,
            Description: input.Description,
            Unit: input.Unit,
            SectionId: input.SectionId,
            SectionName: input.SectionName,
            ContractQuantity: contract,
            RealizedQuantity: realized,
            RemainingQuantity: Round(contract - realized),
            DeviationQuantity: deviation,
            DeviationRate: deviationRate,
            ContractAmount: Round(contract * unitPrice),
            RealizedAmount: Round(realized * unitPrice),
            DeviationAmount: Round(deviation * unitPrice),
            IssuedStockQuantity: input.IssuedStockQuantity,
            EffectiveContractType: input.EffectiveContractType,
            Impact: impact,
            // Sözleşme miktarı olmayan kalemde oran yok; eşik de yok.
            ExceedsWarningThreshold:
                contract > 0m && realized > contract * ItemWarningThresholdRate / 100m);
    }

    /// <summary>
    /// Sapmanın anlamı. Tablo:
    ///   birim fiyatlı + artış  → fırsat
    ///   birim fiyatlı + azalış → bilgi
    ///   anahtar teslim + artış → kâr erozyonu
    ///   anahtar teslim + azalış → tasarruf
    /// </summary>
    private static DeviationImpact ResolveImpact(
        ProjectContractType contractType, decimal deviation)
    {
        if (contractType == ProjectContractType.Undetermined)
            return DeviationImpact.Undetermined;

        if (deviation == 0m)
            return DeviationImpact.None;

        return contractType switch
        {
            ProjectContractType.UnitPrice => deviation > 0m
                ? DeviationImpact.Opportunity
                : DeviationImpact.Information,

            ProjectContractType.LumpSum => deviation > 0m
                ? DeviationImpact.ProfitErosion
                : DeviationImpact.Saving,

            // Karma projede tip bölüm bazında çözülür; buraya Mixed
            // gelmesi kalemin bölümünün tipi belirlenmemiş demektir.
            _ => DeviationImpact.Undetermined
        };
    }

    /// <summary>
    /// Karma projede kalemin geçerli tipi: bölümün tipi varsa o,
    /// yoksa projenin tipi. Karma olmayan projede bölüm tipi yok sayılır
    /// — aksi halde tek bir bölüm ayarı tüm projeyi yanlış yorumlatırdı.
    /// </summary>
    public static ProjectContractType ResolveEffectiveContractType(
        ProjectContractType projectType, ProjectContractType? sectionType)
    {
        if (projectType != ProjectContractType.Mixed)
            return projectType;

        return sectionType ?? ProjectContractType.Undetermined;
    }

    public static TrackingTotals CalculateTotals(IReadOnlyList<TrackingItemResult> items)
    {
        var contractAmount = Round(items.Sum(x => x.ContractAmount));
        var realizedAmount = Round(items.Sum(x => x.RealizedAmount));

        var overrun = Round(items.Where(x => x.DeviationAmount > 0m)
            .Sum(x => x.DeviationAmount));

        var underrun = Round(Math.Abs(items.Where(x => x.DeviationAmount < 0m)
            .Sum(x => x.DeviationAmount)));

        return new TrackingTotals(
            ContractAmount: contractAmount,
            RealizedAmount: realizedAmount,
            OverrunAmount: overrun,
            UnderrunAmount: underrun,
            NetDeviationAmount: Round(overrun - underrun),
            PhysicalCompletionRate: contractAmount > 0m
                ? Round(realizedAmount / contractAmount * 100m)
                : 0m,
            ItemCount: items.Count,
            WarningItemCount: items.Count(x => x.ExceedsWarningThreshold));
    }

    /// <summary>
    /// Güncel tahmini kâr (yalnızca anahtar teslimde anlamlı):
    ///
    ///   tahmini toplam maliyet = fiili maliyet ÷ fiziksel gerçekleşme
    ///   tahmini kâr = sözleşme bedeli − tahmini toplam maliyet
    ///
    /// Gerçekleşme düşükken bölen küçülür ve tahmin uçar; bu yüzden
    /// eşiğin altında tahmin ÜRETİLMEZ. Sayı üretip "güvenilmez" demek
    /// yerine hiç üretmemek doğru: ekranda görünen rakam karar
    /// değiştirir.
    /// </summary>
    public static ProfitEstimate EstimateProfit(
        decimal contractAmount,
        decimal actualCost,
        decimal physicalCompletionRate)
    {
        if (contractAmount <= 0m)
        {
            return Unreliable(
                "Projede sözleşme bedeli tanımlı değil.",
                contractAmount, actualCost, physicalCompletionRate);
        }

        if (physicalCompletionRate < MinimumCompletionForProfitEstimate)
        {
            return Unreliable(
                $"Fiziksel gerçekleşme %{TurkishFormat.Rate(physicalCompletionRate)}; " +
                $"%{TurkishFormat.Whole(MinimumCompletionForProfitEstimate)} altında maliyet " +
                "tahmini yanıltıcı olacağı için üretilmedi.",
                contractAmount, actualCost, physicalCompletionRate);
        }

        if (actualCost <= 0m)
        {
            return Unreliable(
                "Projeye henüz maliyet işlenmemiş.",
                contractAmount, actualCost, physicalCompletionRate);
        }

        var estimatedTotalCost = Round(actualCost / (physicalCompletionRate / 100m));
        var estimatedProfit = Round(contractAmount - estimatedTotalCost);

        return new ProfitEstimate(
            IsReliable: true,
            UnreliableReason: null,
            ContractAmount: contractAmount,
            ActualCost: actualCost,
            PhysicalCompletionRate: physicalCompletionRate,
            EstimatedTotalCost: estimatedTotalCost,
            EstimatedProfit: estimatedProfit,
            EstimatedProfitRate: Round(estimatedProfit / contractAmount * 100m));
    }

    private static ProfitEstimate Unreliable(
        string reason, decimal contractAmount, decimal actualCost, decimal completion) =>
        new(false, reason, contractAmount, actualCost, completion, 0m, 0m, 0m);

    /// <summary>
    /// Kâr erozyonuna fiilen giren tutar.
    ///
    /// Anahtar teslimde işveren tarafından ONAYLANMIŞ ek iş tahsil
    /// edilebilir; dolayısıyla net sapmadan düşülür. Onaysız ek iş
    /// düşülmez — düşülseydi tahsil edilemeyecek bir tutar kâr gibi
    /// görünür ve erozyon olduğundan küçük hesaplanırdı.
    /// </summary>
    public static decimal CalculateNetErosion(
        ProjectContractType contractType,
        decimal netDeviationAmount,
        decimal collectibleExtraWorkAmount)
    {
        if (contractType != ProjectContractType.LumpSum)
            return 0m;

        return Math.Max(0m, Round(netDeviationAmount - collectibleExtraWorkAmount));
    }

    /// <summary>
    /// Anahtar teslimde toplam sapma eşiği aşıldı mı — kâr erozyon
    /// alarmı. Yalnızca keşif ÜSTÜ net sapma alarm üretir; tasarruf
    /// alarm değildir.
    /// </summary>
    /// <param name="collectibleExtraWorkAmount">İşveren onaylı ek iş
    /// tutarı; erozyondan düşülür.</param>
    public static bool ShouldRaiseErosionAlarm(
        ProjectContractType contractType,
        decimal netDeviationAmount,
        decimal contractAmount,
        decimal thresholdRate,
        decimal collectibleExtraWorkAmount = 0m)
    {
        if (contractType != ProjectContractType.LumpSum)
            return false;

        if (contractAmount <= 0m)
            return false;

        var erosion = CalculateNetErosion(
            contractType, netDeviationAmount, collectibleExtraWorkAmount);

        if (erosion <= 0m)
            return false;

        return erosion / contractAmount * 100m > thresholdRate;
    }

    private static decimal Round(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
