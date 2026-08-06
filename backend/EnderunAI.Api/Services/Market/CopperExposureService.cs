using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Market;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Market;

/// <summary>
/// Bakır + kur hareketinin projelerin kalan işine etkisi.
///
/// Hesap tek satır: kalan ton × (bugünkü TL/ton − taban TL/ton). Asıl
/// iş, bu farkın emtia ve kur bileşenlerine dürüst ayrılmasında:
///
///   ΔTL = (P1 − P0)·R0  +  P0·(R1 − R0)  +  (P1 − P0)·(R1 − R0)
///          └ bakır ┘        └── kur ──┘      └─ birleşik artık ─┘
///
/// Artık üçüncü terim bir yere sessizce eklenmez; küçük olduğu sürece
/// ihmal edilebilir görünür ama iki değişken de büyük oynadığında
/// yanıltır.
///
/// Tonaj bilinmiyorsa etki SIFIR değil BOŞ döner. Sıfır, "bakır riski
/// yok" demektir ve bu yanlış bir güven verir.
/// </summary>
public sealed class CopperExposureService(
    AppDbContext db,
    ICommodityPriceService commodityPrices,
    ILogger<CopperExposureService> logger) : ICopperExposureService
{
    public async Task<ProjectCopperImpact?> GetForProjectAsync(
        Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == projectId, cancellationToken);

        return project is null
            ? null
            : await BuildAsync(project, cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectCopperImpact>> GetPortfolioAsync(
        Guid? companyId, CancellationToken cancellationToken = default)
    {
        var query = db.Projects.AsNoTracking().Where(x => !x.IsArchived);

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        // Kapanmış ve iptal edilmiş projede "kalan iş" yok.
        var projects = await query
            .Where(x => x.Status == ProjectStatus.Active || x.Status == ProjectStatus.Kesif)
            .OrderBy(x => x.Code)
            .ToListAsync(cancellationToken);

        var results = new List<ProjectCopperImpact>(projects.Count);

        foreach (var project in projects)
            results.Add(await BuildAsync(project, cancellationToken));

        return results;
    }

    public async Task<ProjectCopperImpact?> SaveExposureAsync(
        Guid projectId, CopperExposureInput input, CancellationToken cancellationToken = default)
    {
        var project = await db.Projects
            .FirstOrDefaultAsync(x => x.Id == projectId, cancellationToken);

        if (project is null)
            return null;

        if (input.RemainingTons is < 0)
            throw new ArgumentException("Kalan bakır tonajı negatif olamaz.");

        var exposure = await db.ProjectCopperExposures
            .FirstOrDefaultAsync(x => x.ProjectId == projectId, cancellationToken);

        if (exposure is null)
        {
            exposure = new ProjectCopperExposure { ProjectId = projectId };
            db.ProjectCopperExposures.Add(exposure);
        }

        exposure.RemainingTons = input.RemainingTons;
        exposure.BaselineDate = input.BaselineDate.HasValue
            ? DateTime.SpecifyKind(input.BaselineDate.Value.Date, DateTimeKind.Utc)
            : null;
        exposure.Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        exposure.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return await BuildAsync(project, cancellationToken);
    }

    private async Task<ProjectCopperImpact> BuildAsync(
        Project project, CancellationToken cancellationToken)
    {
        var assumptions = new List<string>();

        var exposure = await db.ProjectCopperExposures
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProjectId == project.Id, cancellationToken);

        var (tons, tonnageSource) = await ResolveTonnageAsync(
            project.Id, exposure, assumptions, cancellationToken);

        var (baselineDate, baselineReason) = ResolveBaseline(project, exposure);

        var current = await commodityPrices.GetPriceAsync(
            Commodity.Copper, DateTime.UtcNow.Date, cancellationToken);

        var baseline = baselineDate is null
            ? null
            : await commodityPrices.GetPriceAsync(
                Commodity.Copper, baselineDate.Value, cancellationToken);

        var isCostRisk = project.ContractType == ProjectContractType.LumpSum;

        if (!isCostRisk)
        {
            assumptions.Add(
                project.ContractType == ProjectContractType.UnitPrice
                    ? "Birim fiyatlı sözleşme: yapılan iş kadar ödendiğinden etki bilgi amaçlıdır."
                    : "Sözleşme tipi belirlenmedi; etkinin kâra yansıması yorumlanamaz.");
        }

        if (current is null)
            assumptions.Add("Güncel bakır fiyatı arşivde yok; etki hesaplanamadı.");

        if (baseline is null && baselineDate is not null)
            assumptions.Add($"{baselineDate:dd.MM.yyyy} tarihine bakır fiyatı bulunamadı.");

        // Bileşenlerin hepsi varsa etki hesaplanır; biri bile eksikse
        // tahmini bir sayı üretilmez.
        decimal? copperEffect = null;
        decimal? fxEffect = null;
        decimal? combinedEffect = null;
        decimal? totalEffect = null;
        decimal? copperChange = null;
        decimal? fxChange = null;

        if (current is not null && baseline is not null)
        {
            copperChange = Percent(baseline.PriceUsdPerTon, current.PriceUsdPerTon);

            if (baseline.UsdRate is { } r0 && current.UsdRate is { } r1)
            {
                fxChange = Percent(r0, r1);

                if (tons is { } t)
                {
                    var priceDelta = current.PriceUsdPerTon - baseline.PriceUsdPerTon;
                    var rateDelta = r1 - r0;

                    copperEffect = decimal.Round(t * priceDelta * r0, 2);
                    fxEffect = decimal.Round(t * baseline.PriceUsdPerTon * rateDelta, 2);
                    combinedEffect = decimal.Round(t * priceDelta * rateDelta, 2);
                    totalEffect = copperEffect + fxEffect + combinedEffect;
                }
            }
            else
            {
                assumptions.Add(
                    "Taban veya güncel gün için TCMB kuru yok; TL etkisi hesaplanamadı.");
            }
        }

        if (tons is null)
        {
            assumptions.Add(
                "Kalan bakır tonajı bilinmiyor. Proje bazında elle girin veya icmal " +
                "kalemlerine birim başına bakır (kg) katsayısı tanımlayın.");
        }

        return new ProjectCopperImpact(
            project.Id,
            project.Code,
            project.Name,
            (int)project.ContractType,
            ContractTypeName(project.ContractType),
            isCostRisk,
            tonnageSource,
            TonnageSourceName(tonnageSource),
            tons,
            baseline?.PriceDate ?? baselineDate,
            baselineReason,
            baseline?.PriceUsdPerTon,
            baseline?.UsdRate,
            current?.PriceUsdPerTon,
            current?.UsdRate,
            copperChange,
            fxChange,
            copperEffect,
            fxEffect,
            combinedEffect,
            totalEffect,
            assumptions);
    }

    /// <summary>
    /// Kalan tonaj: önce elle girilen değer, sonra icmal katsayıları.
    /// İkisi de yoksa null — sıfır DEĞİL.
    /// </summary>
    private async Task<(decimal? Tons, CopperTonnageSource Source)> ResolveTonnageAsync(
        Guid projectId,
        ProjectCopperExposure? exposure,
        List<string> assumptions,
        CancellationToken cancellationToken)
    {
        if (exposure?.RemainingTons is { } manual)
            return (manual, CopperTonnageSource.Manual);

        // Yürürlükteki keşfin bakır katsayısı tanımlı kalemleri.
        var items = await db.ProjectBoqItems
            .AsNoTracking()
            .Where(x => x.ProjectBoq.ProjectId == projectId
                        && x.ProjectBoq.IsCurrentRevision
                        && (x.CopperKgPerUnit != null
                            || (x.InventoryItem != null && x.InventoryItem.CopperKgPerUnit != null)))
            .Select(x => new
            {
                x.Id,
                x.ContractQuantity,
                Coefficient = x.CopperKgPerUnit ?? x.InventoryItem!.CopperKgPerUnit
            })
            .ToListAsync(cancellationToken);

        if (items.Count == 0)
            return (null, CopperTonnageSource.Unknown);

        var boqItemIds = items.Select(x => x.Id).ToList();

        // Kalan miktar: onaylı metrajın kalem bazındaki kalanı. Metraj
        // yoksa kalemin tamamı kalmış sayılır. Global ilerleme oranı
        // kullanılmıyor — kablo kalemi projenin geneliyle aynı hızda
        // ilerlemek zorunda değil.
        var remainingByItem = await db.ProjectMeasurementItems
            .AsNoTracking()
            .Where(x => boqItemIds.Contains(x.ProjectBoqItemId)
                        && x.ProjectMeasurement.ProjectId == projectId
                        && x.ProjectMeasurement.Status == ProjectMeasurementStatus.Approved)
            .GroupBy(x => x.ProjectBoqItemId)
            .Select(g => new
            {
                BoqItemId = g.Key,
                Remaining = g
                    .OrderByDescending(x => x.ProjectMeasurement.MeasurementDate)
                    .Select(x => x.RemainingQuantity)
                    .First()
            })
            .ToDictionaryAsync(x => x.BoqItemId, x => x.Remaining, cancellationToken);

        var kilograms = 0m;

        foreach (var item in items)
        {
            var remaining = remainingByItem.TryGetValue(item.Id, out var measured)
                ? measured
                : item.ContractQuantity;

            if (remaining <= 0 || item.Coefficient is not { } coefficient)
                continue;

            kilograms += remaining * coefficient;
        }

        if (kilograms <= 0)
            return (0m, CopperTonnageSource.BillOfQuantities);

        assumptions.Add(
            $"Tonaj icmaldeki {items.Count} kalemin bakır katsayısından türetildi; " +
            "katsayısı olmayan kalemler hesaba girmedi.");

        return (decimal.Round(kilograms / 1000m, 3), CopperTonnageSource.BillOfQuantities);
    }

    private static (DateTime? Date, string? Reason) ResolveBaseline(
        Project project, ProjectCopperExposure? exposure)
    {
        if (exposure?.BaselineDate is { } explicitDate)
            return (explicitDate, "Elle seçilen taban tarih");

        if (project.ContractDate is { } contractDate)
        {
            return (
                DateTime.SpecifyKind(contractDate.Date, DateTimeKind.Utc),
                "Sözleşme tarihi");
        }

        return (null, "Taban tarih yok — sözleşme tarihi girilmemiş ve elle seçilmemiş");
    }

    private static decimal? Percent(decimal from, decimal to) =>
        from == 0 ? null : decimal.Round((to - from) / from * 100m, 2);

    private static string ContractTypeName(ProjectContractType type) => type switch
    {
        ProjectContractType.LumpSum => "Anahtar teslim (götürü)",
        ProjectContractType.UnitPrice => "Birim fiyatlı",
        ProjectContractType.Mixed => "Karma",
        _ => "Belirlenmedi"
    };

    private static string TonnageSourceName(CopperTonnageSource source) => source switch
    {
        CopperTonnageSource.Manual => "Elle girildi",
        CopperTonnageSource.BillOfQuantities => "İcmalden türetildi",
        _ => "Bilinmiyor"
    };
}
