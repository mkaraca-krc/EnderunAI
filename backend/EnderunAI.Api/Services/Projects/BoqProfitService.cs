using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Engineering;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Projects;

/// <summary>Kurum bazlı referans fiyat — kütüphaneden.</summary>
public sealed record ReferencePrice(
    string InstitutionName,
    decimal? UnitPrice,
    decimal? MaterialPrice,
    decimal? LaborPrice,
    int? Year);

/// <summary>
/// Şirketin bu poz için GEÇMİŞ projelerde gerçekleşen birim maliyeti.
///
/// Yalnızca poza AÇIKÇA ETİKETLENMİŞ maliyetten hesaplanır; dağıtılmış
/// tutarlar girmez. Dağıtılmış rakamlardan ortalama almak, tahminlerin
/// ortalamasını gerçek gibi göstermek olurdu.
/// </summary>
public sealed record CompanyActualAverage(
    bool HasEnoughData,
    int ProjectCount,
    decimal? AverageUnitCost,
    decimal? MinUnitCost,
    decimal? MaxUnitCost,
    string Explanation);

/// <summary>Bir icmal satırının dört fiyatı ve kârı.</summary>
public sealed record BoqLineProfit(
    Guid BoqItemId,
    string PositionCode,
    string Description,
    string Unit,
    Guid? SectionId,
    decimal ContractQuantity,
    /// <summary>1 — Sözleşme (işverenle anlaşılan) = gelir.</summary>
    decimal ContractUnitPrice,
    decimal ContractMaterialUnitPrice,
    decimal ContractLaborUnitPrice,
    decimal ContractTotal,
    /// <summary>2 — Kütüphane referansı; ÇŞB ve TEDAŞ yan yana.</summary>
    IReadOnlyList<ReferencePrice> References,
    /// <summary>3 — Şirketin geçmiş projelerdeki gerçekleşmesi.</summary>
    CompanyActualAverage CompanyAverage,
    /// <summary>4 — Bu projedeki anlık maliyet.</summary>
    decimal MeasuredCost,
    decimal AllocatedCost,
    decimal ActualCost,
    decimal MeasuredRatio,
    /// <summary>Sözleşme − anlık maliyet.</summary>
    decimal Profit,
    decimal? ProfitMarginPercent);

public sealed record ProjectProfitBreakdown(
    Guid ProjectId,
    bool IncludesExtraPayments,
    decimal ContractTotal,
    decimal ActualCostTotal,
    decimal MeasuredCostTotal,
    decimal AllocatedCostTotal,
    decimal UnassignedCost,
    decimal Profit,
    decimal? ProfitMarginPercent,
    IReadOnlyList<BoqLineProfit> Lines,
    IReadOnlyList<string> Assumptions);

public interface IBoqProfitService
{
    Task<ProjectProfitBreakdown?> GetAsync(
        Guid projectId, int? referenceYear = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// İcmal satırı bazında dört fiyat ve kâr.
///
/// Kâr her zaman SÖZLEŞME eksi ANLIK MALİYET'tir; referans ve şirket
/// ortalaması karara yardımcı bilgilerdir, kâra girmez. Referansı
/// maliyet yerine koymak, henüz gerçekleşmemiş bir rakamı gerçekleşmiş
/// gibi gösterirdi.
/// </summary>
public sealed class BoqProfitService(
    AppDbContext db,
    IBoqItemCostService costs,
    IPositionPriceService prices) : IBoqProfitService
{
    /// <summary>
    /// Şirket ortalaması için gereken en az farklı proje sayısı. Tek
    /// projenin özel koşulları (zor saha, acele iş) şirket standardı
    /// gibi görünmemeli.
    /// </summary>
    private const int MinimumProjectsForAverage = 2;

    private static readonly PositionPriceInstitution[] ReferenceInstitutions =
    [
        PositionPriceInstitution.Csb,
        PositionPriceInstitution.Tedas
    ];

    public async Task<ProjectProfitBreakdown?> GetAsync(
        Guid projectId,
        int? referenceYear = null,
        CancellationToken cancellationToken = default)
    {
        var projectExists = await db.Projects
            .AnyAsync(x => x.Id == projectId, cancellationToken);

        if (!projectExists)
            return null;

        var items = await db.ProjectBoqItems
            .AsNoTracking()
            .Where(x => x.ProjectBoq.ProjectId == projectId && x.ProjectBoq.IsCurrentRevision)
            .OrderBy(x => x.LineNumber)
            .Select(x => new
            {
                x.Id,
                x.EngineeringPositionId,
                x.ProjectHakedisSectionId,
                x.PositionCode,
                x.Description,
                x.Unit,
                x.ContractQuantity,
                x.UnitPrice,
                x.MaterialUnitPrice,
                x.LaborUnitPrice,
                x.TotalAmount
            })
            .ToListAsync(cancellationToken);

        var snapshot = await costs.GetAsync(projectId, cancellationToken);
        var assumptions = new List<string>(snapshot.Assumptions);

        var averages = await BuildCompanyAveragesAsync(
            projectId,
            items.Where(x => x.EngineeringPositionId.HasValue)
                .Select(x => x.EngineeringPositionId!.Value)
                .Distinct()
                .ToList(),
            cancellationToken);

        var lines = new List<BoqLineProfit>(items.Count);

        foreach (var item in items)
        {
            var cost = snapshot.ByBoqItem.TryGetValue(item.Id, out var found)
                ? found
                : new BoqItemCost(item.Id, 0, 0, 0, 0, 0, 0);

            var references = new List<ReferencePrice>();

            if (item.EngineeringPositionId is Guid positionId)
            {
                foreach (var institution in ReferenceInstitutions)
                {
                    var resolution = await prices.ResolveAsync(
                        positionId, referenceYear, institution, cancellationToken);

                    if (!resolution.Found)
                        continue;

                    references.Add(new ReferencePrice(
                        resolution.InstitutionName ?? "—",
                        resolution.UnitPrice,
                        resolution.MaterialPrice,
                        resolution.LaborPrice,
                        resolution.Year));
                }
            }

            var profit = item.TotalAmount - cost.Total;

            lines.Add(new BoqLineProfit(
                item.Id,
                item.PositionCode,
                item.Description,
                item.Unit,
                item.ProjectHakedisSectionId,
                item.ContractQuantity,
                item.UnitPrice,
                item.MaterialUnitPrice,
                item.LaborUnitPrice,
                item.TotalAmount,
                references,
                item.EngineeringPositionId is Guid id
                    && averages.TryGetValue(id, out var average)
                        ? average
                        : NoData("Bu poz kütüphaneye bağlı değil."),
                cost.MeasuredTotal,
                cost.AllocatedTotal,
                cost.Total,
                cost.MeasuredRatio,
                decimal.Round(profit, 2),
                Percent(item.TotalAmount, profit)));
        }

        var contractTotal = lines.Sum(x => x.ContractTotal);
        var actualTotal = lines.Sum(x => x.ActualCost);
        var totalProfit = contractTotal - actualTotal;

        if (snapshot.UnassignedCost != 0)
        {
            assumptions.Add(
                "Poza bağlanamayan maliyet satır kârlarına yansımıyor; proje toplam " +
                "kârı bu tutar kadar iyimser görünebilir.");
        }

        return new ProjectProfitBreakdown(
            projectId,
            snapshot.IncludesExtraPayments,
            decimal.Round(contractTotal, 2),
            decimal.Round(actualTotal, 2),
            decimal.Round(lines.Sum(x => x.MeasuredCost), 2),
            decimal.Round(lines.Sum(x => x.AllocatedCost), 2),
            snapshot.UnassignedCost,
            decimal.Round(totalProfit, 2),
            Percent(contractTotal, totalProfit),
            lines,
            assumptions);
    }

    /// <summary>
    /// Poz başına şirket gerçekleşme ortalaması.
    ///
    /// Kaynak: BAŞKA projelerdeki, poza açıkça etiketlenmiş maliyet ÷ o
    /// projede gerçekleşen metraj. Metrajı olmayan proje ortalamaya
    /// giremez — birim maliyet hesaplanamaz.
    /// </summary>
    private async Task<Dictionary<Guid, CompanyActualAverage>> BuildCompanyAveragesAsync(
        Guid currentProjectId,
        IReadOnlyList<Guid> positionIds,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, CompanyActualAverage>();

        if (positionIds.Count == 0)
            return result;

        // Bu pozlara bağlı, BAŞKA projelerdeki icmal satırları.
        var otherItems = await db.ProjectBoqItems
            .AsNoTracking()
            .Where(x => x.EngineeringPositionId != null
                        && positionIds.Contains(x.EngineeringPositionId!.Value)
                        && x.ProjectBoq.ProjectId != currentProjectId)
            .Select(x => new
            {
                x.Id,
                PositionId = x.EngineeringPositionId!.Value,
                ProjectId = x.ProjectBoq.ProjectId
            })
            .ToListAsync(cancellationToken);

        if (otherItems.Count == 0)
        {
            foreach (var positionId in positionIds)
            {
                result[positionId] = NoData(
                    "Bu poz başka projede kullanılmamış; henüz şirket verisi yok.");
            }

            return result;
        }

        var itemIds = otherItems.Select(x => x.Id).ToList();

        // Yalnızca ETİKETLİ maliyet: dağıtılmış tutarlar ortalamaya girmez.
        var materialCosts = await db.ProjectCostTransactions
            .AsNoTracking()
            .Where(x => x.ProjectBoqItemId != null && itemIds.Contains(x.ProjectBoqItemId!.Value))
            .GroupBy(x => x.ProjectBoqItemId!.Value)
            .Select(g => new { BoqItemId = g.Key, Amount = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.BoqItemId, x => x.Amount, cancellationToken);

        var laborCosts = await db.HrProjectLaborCosts
            .AsNoTracking()
            .Where(x => x.ProjectBoqItemId != null && itemIds.Contains(x.ProjectBoqItemId!.Value))
            .GroupBy(x => x.ProjectBoqItemId!.Value)
            .Select(g => new { BoqItemId = g.Key, Amount = g.Sum(x => x.TotalLaborCost) })
            .ToDictionaryAsync(x => x.BoqItemId, x => x.Amount, cancellationToken);

        // Gerçekleşen metraj: onaylı metrajın kümülatifi.
        var quantities = await db.ProjectMeasurementItems
            .AsNoTracking()
            .Where(x => itemIds.Contains(x.ProjectBoqItemId)
                        && x.ProjectMeasurement.Status == ProjectMeasurementStatus.Approved)
            .GroupBy(x => x.ProjectBoqItemId)
            .Select(g => new
            {
                BoqItemId = g.Key,
                Quantity = g.Max(x => x.CumulativeQuantity)
            })
            .ToDictionaryAsync(x => x.BoqItemId, x => x.Quantity, cancellationToken);

        foreach (var positionId in positionIds)
        {
            var samples = new List<(Guid ProjectId, decimal UnitCost)>();

            foreach (var item in otherItems.Where(x => x.PositionId == positionId))
            {
                var cost =
                    materialCosts.GetValueOrDefault(item.Id)
                    + laborCosts.GetValueOrDefault(item.Id);

                if (cost <= 0)
                    continue;

                if (!quantities.TryGetValue(item.Id, out var quantity) || quantity <= 0)
                    continue;

                samples.Add((item.ProjectId, decimal.Round(cost / quantity, 4)));
            }

            var projectCount = samples.Select(x => x.ProjectId).Distinct().Count();

            if (projectCount < MinimumProjectsForAverage)
            {
                result[positionId] = NoData(
                    projectCount == 0
                        ? "Bu poza etiketlenmiş gerçekleşme yok; ölçüm yetersiz, " +
                          "referans fiyat kullanılıyor."
                        : $"Yalnızca {projectCount} projede veri var; en az " +
                          $"{MinimumProjectsForAverage} proje gerekiyor.");

                continue;
            }

            var values = samples.Select(x => x.UnitCost).ToList();

            result[positionId] = new CompanyActualAverage(
                true,
                projectCount,
                decimal.Round(values.Average(), 2),
                values.Min(),
                values.Max(),
                $"{projectCount} projede gerçekleşen etiketli maliyetten hesaplandı.");
        }

        return result;
    }

    private static CompanyActualAverage NoData(string explanation) =>
        new(false, 0, null, null, null, explanation);

    private static decimal? Percent(decimal basis, decimal value) =>
        basis == 0 ? null : decimal.Round(value / basis * 100m, 2);
}
