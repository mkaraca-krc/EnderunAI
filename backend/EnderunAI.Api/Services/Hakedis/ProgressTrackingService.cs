using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Hakedis;

public sealed record ProgressTrackingView(
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    int ContractType,
    string ContractTypeName,
    decimal? ContractAmount,
    decimal DeviationAlertThresholdRate,
    /// <summary>Sözleşme metrajının kaynağı — kullanıcı neye baktığını bilmeli.</summary>
    string BaselineSource,
    IReadOnlyList<TrackingItemResult> Items,
    TrackingTotals Totals,
    ProfitEstimate ProfitEstimate,
    bool ErosionAlarm,
    IReadOnlyList<string> Warnings);

public interface IProgressTrackingService
{
    Task<ProgressTrackingView> BuildAsync(
        Guid projectId, CancellationToken cancellationToken);

    /// <summary>
    /// Uyarı üreten projeler — dashboard ve Hızır brifingi için.
    /// Sözleşme tipi belirlenmemiş projeler hiç değerlendirilmez.
    /// </summary>
    Task<IReadOnlyList<ProgressDeviationAlert>> GetAlertsAsync(
        IReadOnlyCollection<Guid>? projectIds,
        CancellationToken cancellationToken);
}

public sealed record ProgressDeviationAlert(
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    int ContractType,
    int ExceedingItemCount,
    decimal NetDeviationAmount,
    bool ErosionAlarm);

/// <summary>
/// Keşif–gerçekleşen takibi. Hesap
/// <see cref="ProgressTrackingCalculator"/> içinde; burası veriyi
/// toplayıp motora veriyor.
///
/// Sözleşme metrajı sırasıyla şuradan gelir:
///   1. Projenin sözleşme metrajı işaretli keşfi (IsContractBaseline)
///   2. Yoksa onaylı güncel keşif
///   3. O da yoksa hakediş satırlarındaki sözleşme miktarı
/// Üçüncü durumda hiç hakedişe girmemiş kalem görünmez; ekran bunu yazar.
/// </summary>
public sealed class ProgressTrackingService(AppDbContext db) : IProgressTrackingService
{
    public async Task<ProgressTrackingView> BuildAsync(
        Guid projectId, CancellationToken cancellationToken)
    {
        var project = await db.Projects
            .AsNoTracking()
            .Where(x => x.Id == projectId)
            .Select(x => new
            {
                x.Id,
                x.Code,
                x.Name,
                x.ContractType,
                x.ContractAmount,
                x.DeviationAlertThresholdRate
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Proje bulunamadı.");

        var warnings = new List<string>();

        if (project.ContractType == ProjectContractType.Undetermined)
        {
            warnings.Add(
                "Projenin sözleşme tipi belirlenmemiş. Sapmanın kâr etkisi " +
                "ancak tip seçilince hesaplanabilir — proje kartından seçin.");
        }

        var sectionTypes = await db.ProjectHakedisSections
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .ToDictionaryAsync(x => x.Id, x => new { x.Name, x.ContractType },
                cancellationToken);

        // --- Gerçekleşen: iptal edilmeyen hakedişlerin dönem miktarları ---
        var realizedByCode = await db.ProgressPaymentItems
            .AsNoTracking()
            .Where(x => x.ProgressPayment.ProjectId == projectId &&
                        x.ProgressPayment.Status != ProgressPaymentStatus.Cancelled)
            .GroupBy(x => x.PositionCode)
            .Select(g => new { Code = g.Key, Quantity = g.Sum(x => x.CurrentQuantity) })
            .ToDictionaryAsync(x => x.Code, x => x.Quantity, cancellationToken);

        var (baselineItems, baselineSource) =
            await LoadBaselineAsync(projectId, cancellationToken);

        if (baselineSource == "Hakediş satırları")
        {
            warnings.Add(
                "Projede sözleşme metrajı (keşif) tanımlı değil; karşılaştırma " +
                "hakediş satırlarındaki sözleşme miktarından yapılıyor. Hiç " +
                "hakedişe girmemiş kalemler bu listede görünmez.");
        }

        // --- Stok sarfı (kaleme stok eşlenmişse) ---
        var inventoryIds = baselineItems
            .Where(x => x.InventoryItemId is not null)
            .Select(x => x.InventoryItemId!.Value)
            .Distinct()
            .ToList();

        var issuedByInventory = inventoryIds.Count == 0
            ? []
            : await db.StockMovements
                .AsNoTracking()
                .Where(x => x.ProjectId == projectId &&
                            x.Type == StockMovementType.Issue &&
                            inventoryIds.Contains(x.InventoryItemId))
                .GroupBy(x => x.InventoryItemId)
                .Select(g => new { Id = g.Key, Quantity = g.Sum(x => x.Quantity) })
                .ToDictionaryAsync(x => x.Id, x => x.Quantity, cancellationToken);

        var items = new List<TrackingItemResult>();

        foreach (var item in baselineItems)
        {
            realizedByCode.TryGetValue(item.PositionCode, out var realized);

            string? sectionName = null;
            ProjectContractType? sectionType = null;

            if (item.SectionId is Guid sectionId &&
                sectionTypes.TryGetValue(sectionId, out var section))
            {
                sectionName = section.Name;
                sectionType = section.ContractType;
            }

            decimal? issuedStock = item.InventoryItemId is Guid inventoryId &&
                                   issuedByInventory.TryGetValue(inventoryId, out var issued)
                ? issued
                : null;

            items.Add(ProgressTrackingCalculator.CalculateItem(
                new TrackingItemInput(
                    PositionCode: item.PositionCode,
                    Description: item.Description,
                    Unit: item.Unit,
                    SectionId: item.SectionId,
                    SectionName: sectionName,
                    ContractQuantity: item.ContractQuantity,
                    RealizedQuantity: realized,
                    UnitPrice: item.UnitPrice,
                    IssuedStockQuantity: issuedStock,
                    EffectiveContractType:
                        ProgressTrackingCalculator.ResolveEffectiveContractType(
                            project.ContractType, sectionType))));
        }

        // Keşifte olmayan ama hakedişe girmiş kalemler: tamamen keşif
        // dışı iş. Görünmezlerse sapma eksik hesaplanırdı.
        var baselineCodes = baselineItems
            .Select(x => x.PositionCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var extra in realizedByCode.Where(x => !baselineCodes.Contains(x.Key)))
        {
            var unitPrice = await db.ProgressPaymentItems
                .AsNoTracking()
                .Where(x => x.ProgressPayment.ProjectId == projectId &&
                            x.PositionCode == extra.Key)
                .OrderByDescending(x => x.ProgressPayment.PeriodNumber)
                .Select(x => x.UnitPrice)
                .FirstOrDefaultAsync(cancellationToken);

            var description = await db.ProgressPaymentItems
                .AsNoTracking()
                .Where(x => x.ProgressPayment.ProjectId == projectId &&
                            x.PositionCode == extra.Key)
                .Select(x => new { x.Description, x.Unit })
                .FirstOrDefaultAsync(cancellationToken);

            items.Add(ProgressTrackingCalculator.CalculateItem(
                new TrackingItemInput(
                    PositionCode: extra.Key,
                    Description: (description?.Description ?? extra.Key) + " (keşif dışı)",
                    Unit: description?.Unit ?? "",
                    SectionId: null,
                    SectionName: null,
                    ContractQuantity: 0m,
                    RealizedQuantity: extra.Value,
                    UnitPrice: unitPrice,
                    IssuedStockQuantity: null,
                    EffectiveContractType: project.ContractType)));
        }

        var ordered = items
            .OrderBy(x => x.SectionName ?? "zzz")
            .ThenBy(x => x.PositionCode, StringComparer.CurrentCulture)
            .ToList();

        var totals = ProgressTrackingCalculator.CalculateTotals(ordered);

        var actualCost = await db.ProjectCostTransactions
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

        var profit = ProgressTrackingCalculator.EstimateProfit(
            project.ContractAmount ?? 0m, actualCost, totals.PhysicalCompletionRate);

        var erosionAlarm = ProgressTrackingCalculator.ShouldRaiseErosionAlarm(
            project.ContractType,
            totals.NetDeviationAmount,
            project.ContractAmount ?? 0m,
            project.DeviationAlertThresholdRate);

        if (erosionAlarm)
        {
            warnings.Add(
                $"Keşif üstü gerçekleşmelerin net etkisi " +
                $"({totals.NetDeviationAmount:N2} TL) sözleşme bedelinin " +
                $"%{project.DeviationAlertThresholdRate:N2} eşiğini aştı — " +
                "anahtar teslim projede bu doğrudan kâr kaybıdır.");
        }

        return new ProgressTrackingView(
            ProjectId: project.Id,
            ProjectCode: project.Code,
            ProjectName: project.Name,
            ContractType: (int)project.ContractType,
            ContractTypeName: ContractTypeName(project.ContractType),
            ContractAmount: project.ContractAmount,
            DeviationAlertThresholdRate: project.DeviationAlertThresholdRate,
            BaselineSource: baselineSource,
            Items: ordered,
            Totals: totals,
            ProfitEstimate: profit,
            ErosionAlarm: erosionAlarm,
            Warnings: warnings);
    }

    private sealed record BaselineItem(
        string PositionCode,
        string Description,
        string Unit,
        decimal ContractQuantity,
        decimal UnitPrice,
        Guid? SectionId,
        Guid? InventoryItemId);

    private async Task<(List<BaselineItem> Items, string Source)> LoadBaselineAsync(
        Guid projectId, CancellationToken cancellationToken)
    {
        var baseline = await db.ProjectBoqs
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.IsContractBaseline)
            .Select(x => new { x.Id, x.BoqNumber })
            .SingleOrDefaultAsync(cancellationToken);

        var source = "Sözleşme metrajı";

        if (baseline is null)
        {
            baseline = await db.ProjectBoqs
                .AsNoTracking()
                .Where(x => x.ProjectId == projectId &&
                            x.IsCurrentRevision &&
                            x.Status == ProjectBoqStatus.Approved)
                .OrderByDescending(x => x.RevisionNumber)
                .Select(x => new { x.Id, x.BoqNumber })
                .FirstOrDefaultAsync(cancellationToken);

            source = baseline is null ? "Hakediş satırları" : "Onaylı keşif";
        }

        if (baseline is not null)
        {
            var items = await db.ProjectBoqItems
                .AsNoTracking()
                .Where(x => x.ProjectBoqId == baseline.Id)
                .OrderBy(x => x.LineNumber)
                .Select(x => new BaselineItem(
                    x.PositionCode,
                    x.Description,
                    x.Unit,
                    x.ContractQuantity,
                    x.UnitPrice,
                    x.ProjectHakedisSectionId,
                    x.InventoryItemId))
                .ToListAsync(cancellationToken);

            return (items, $"{source} ({baseline.BoqNumber})");
        }

        // Keşif yoksa hakediş satırlarındaki sözleşme miktarı esas alınır.
        // Aynı poz farklı dönemlerde farklı sözleşme miktarı taşıyabilir;
        // en büyüğü alınır — sonradan artırılmış sözleşme miktarı esastır.
        var fromPayments = await db.ProgressPaymentItems
            .AsNoTracking()
            .Where(x => x.ProgressPayment.ProjectId == projectId &&
                        x.ProgressPayment.Status != ProgressPaymentStatus.Cancelled)
            .GroupBy(x => x.PositionCode)
            .Select(g => new BaselineItem(
                g.Key,
                g.First().Description,
                g.First().Unit,
                g.Max(x => x.ContractQuantity),
                g.Max(x => x.UnitPrice),
                g.First().ProgressPaymentSectionId,
                null))
            .ToListAsync(cancellationToken);

        return (fromPayments, "Hakediş satırları");
    }

    public async Task<IReadOnlyList<ProgressDeviationAlert>> GetAlertsAsync(
        IReadOnlyCollection<Guid>? projectIds,
        CancellationToken cancellationToken)
    {
        var query = db.Projects
            .AsNoTracking()
            // Sözleşme tipi belirlenmemiş projede sapma yorumlanamaz.
            .Where(x => x.ContractType != ProjectContractType.Undetermined &&
                        x.Status != ProjectStatus.Completed &&
                        x.Status != ProjectStatus.Cancelled);

        if (projectIds is not null && projectIds.Count > 0)
            query = query.Where(x => projectIds.Contains(x.Id));

        var candidates = await query
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var alerts = new List<ProgressDeviationAlert>();

        foreach (var id in candidates)
        {
            var view = await BuildAsync(id, cancellationToken);

            if (view.Totals.WarningItemCount == 0 && !view.ErosionAlarm)
                continue;

            alerts.Add(new ProgressDeviationAlert(
                ProjectId: view.ProjectId,
                ProjectCode: view.ProjectCode,
                ProjectName: view.ProjectName,
                ContractType: view.ContractType,
                ExceedingItemCount: view.Totals.WarningItemCount,
                NetDeviationAmount: view.Totals.NetDeviationAmount,
                ErosionAlarm: view.ErosionAlarm));
        }

        return alerts;
    }

    public static string ContractTypeName(ProjectContractType type) => type switch
    {
        ProjectContractType.LumpSum => "Anahtar Teslim (Götürü)",
        ProjectContractType.UnitPrice => "Birim Fiyatlı",
        ProjectContractType.Mixed => "Karma",
        _ => "Belirlenmedi"
    };
}
