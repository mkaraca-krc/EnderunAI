using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Hakedis;

/// <summary>Bir icmal kaleminin ilerleme satırı.</summary>
/// <param name="FieldQuantity">Onaylı saha raporlarından biriken miktar
/// — İÇ gerçekleşme.</param>
/// <param name="EmployerQuantity">Hakedişlerde işverenin kabul ettiği
/// kümülatif miktar — RESMİ gerçekleşme.</param>
public sealed record ContractSummaryProgressItem(
    Guid BoqItemId,
    Guid? SectionId,
    string PositionCode,
    string Description,
    string Unit,
    decimal ContractQuantity,
    decimal UnitPrice,
    decimal ContractAmount,
    decimal FieldQuantity,
    decimal EmployerQuantity)
{
    public decimal RemainingQuantity =>
        Math.Max(0m, ContractQuantity - EmployerQuantity);

    /// <summary>Saha ile işveren kabulü arasındaki fark — devreden iş.</summary>
    public decimal PendingQuantity => FieldQuantity - EmployerQuantity;

    public decimal FieldAmount => decimal.Round(FieldQuantity * UnitPrice, 2);
    public decimal EmployerAmount => decimal.Round(EmployerQuantity * UnitPrice, 2);

    public decimal FieldRate => Rate(FieldQuantity, ContractQuantity);
    public decimal EmployerRate => Rate(EmployerQuantity, ContractQuantity);

    /// <summary>
    /// Yüzde. Sözleşme miktarı sıfırsa oran hesaplanmaz (0 döner):
    /// sıfıra bölmek yerine "ölçülemez" demek doğru olanı.
    /// </summary>
    private static decimal Rate(decimal actual, decimal contract) =>
        contract <= 0m
            ? 0m
            : decimal.Round(actual / contract * 100m, 2);
}

public sealed record ContractSummaryProgressSection(
    Guid? SectionId,
    string Name,
    int Order,
    decimal ContractAmount,
    decimal FieldRate,
    decimal EmployerRate,
    IReadOnlyList<ContractSummaryProgressItem> Items);

public sealed record ContractSummaryProgressView(
    Guid ProjectId,
    bool HasContractSummary,
    Guid? BoqId,
    string? BoqNumber,
    decimal ContractAmount,
    decimal FieldRate,
    decimal EmployerRate,
    IReadOnlyList<ContractSummaryProgressSection> Sections);

public interface IContractSummaryProgressService
{
    /// <summary>
    /// Kalem bazında ONAYLI saha birikimi. Anahtar icmal kalemi kimliği.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, decimal>> GetFieldRealizationAsync(
        Guid projectId, CancellationToken cancellationToken);

    Task<ContractSummaryProgressView> BuildAsync(
        Guid projectId, CancellationToken cancellationToken);
}

/// <summary>
/// Sözleşme icmaline dayalı ilerleme.
///
/// İKİ AYRI GERÇEKLEŞME tutulur ve hiçbir zaman birbirine karıştırılmaz:
///   - SAHA: onaylı günlük raporlardan biriken miktar. Bizim ne yaptığımız.
///   - İŞVEREN: hakedişlerde kabul edilen kümülatif miktar. Resmî olan.
/// İkisinin farkı "devreden iş"tir; işveren sistematik olarak eksik
/// kabul ediyorsa bu fark büyür ve görünür olur.
///
/// Yalnızca ONAYLI günlük raporlar sayılır. Taslak rapor da sayılsaydı,
/// henüz kimsenin doğrulamadığı bir miktar hakedişe öneri olarak
/// giderdi.
/// </summary>
public sealed class ContractSummaryProgressService(AppDbContext db)
    : IContractSummaryProgressService
{
    public async Task<IReadOnlyDictionary<Guid, decimal>> GetFieldRealizationAsync(
        Guid projectId, CancellationToken cancellationToken)
    {
        var rows = await db.ProjectSiteDailyReportWorkItems
            .AsNoTracking()
            .Where(x =>
                x.ProjectBoqItemId != null &&
                x.Quantity != null &&
                x.DailyReport.Status == ProjectSiteDailyReportStatus.Approved &&
                x.DailyReport.ProjectSite.ProjectId == projectId)
            .GroupBy(x => x.ProjectBoqItemId!.Value)
            .Select(g => new { BoqItemId = g.Key, Quantity = g.Sum(x => x.Quantity!.Value) })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(x => x.BoqItemId, x => x.Quantity);
    }

    public async Task<ContractSummaryProgressView> BuildAsync(
        Guid projectId, CancellationToken cancellationToken)
    {
        var boq = await ResolveSummaryAsync(projectId, cancellationToken);

        if (boq is null)
        {
            return new ContractSummaryProgressView(
                projectId, false, null, null, 0m, 0m, 0m, []);
        }

        var items = await db.ProjectBoqItems
            .AsNoTracking()
            .Where(x => x.ProjectBoqId == boq.Id)
            .OrderBy(x => x.LineNumber)
            .Select(x => new
            {
                x.Id,
                x.ProjectHakedisSectionId,
                x.PositionCode,
                x.Description,
                x.Unit,
                x.ContractQuantity,
                x.UnitPrice,
                x.TotalAmount
            })
            .ToListAsync(cancellationToken);

        var field = await GetFieldRealizationAsync(projectId, cancellationToken);
        var employer = await GetEmployerRealizationAsync(projectId, cancellationToken);

        var progressItems = items.Select(x => new ContractSummaryProgressItem(
            x.Id,
            x.ProjectHakedisSectionId,
            x.PositionCode,
            x.Description,
            x.Unit,
            x.ContractQuantity,
            x.UnitPrice,
            x.TotalAmount,
            field.GetValueOrDefault(x.Id),
            // Hakediş satırı icmal kalemine bağlıysa kimlikten, değilse
            // poz koduna göre eşleşir — icmal öncesinde açılmış
            // hakedişler de görünsün diye.
            employer.ById.TryGetValue(x.Id, out var byId)
                ? byId
                : employer.ByCode.GetValueOrDefault(x.PositionCode, 0m)))
            .ToList();

        var sectionNames = await db.ProjectHakedisSections
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderBy(x => x.Order)
            .Select(x => new { x.Id, x.Name, x.Order })
            .ToListAsync(cancellationToken);

        var sections = new List<ContractSummaryProgressSection>();

        foreach (var section in sectionNames)
        {
            var lines = progressItems.Where(x => x.SectionId == section.Id).ToList();

            if (lines.Count == 0)
                continue;

            sections.Add(BuildSection(section.Id, section.Name, section.Order, lines));
        }

        var unsectioned = progressItems.Where(x => x.SectionId is null).ToList();

        if (unsectioned.Count > 0)
        {
            sections.Add(BuildSection(
                null, "Kısımsız", int.MaxValue, unsectioned));
        }

        return new ContractSummaryProgressView(
            projectId,
            true,
            boq.Id,
            boq.BoqNumber,
            progressItems.Sum(x => x.ContractAmount),
            WeightedRate(progressItems, x => x.FieldQuantity),
            WeightedRate(progressItems, x => x.EmployerQuantity),
            sections);
    }

    private static ContractSummaryProgressSection BuildSection(
        Guid? id, string name, int order,
        IReadOnlyList<ContractSummaryProgressItem> lines) =>
        new(
            id,
            name,
            order,
            lines.Sum(x => x.ContractAmount),
            WeightedRate(lines, x => x.FieldQuantity),
            WeightedRate(lines, x => x.EmployerQuantity),
            lines);

    /// <summary>
    /// Kısım ve proje yüzdesi, kalemlerin SÖZLEŞME TUTARIYLA ağırlıklı
    /// ortalamasıdır. Ağırlıksız ortalamada 1 adetlik küçük poz ile
    /// 5.000 metrelik ana iş eşit sayılır ve yüzde gerçeği yansıtmaz.
    ///
    /// Kalem bazında oran 100'ü aşabilir (sözleşme üstü imalat); ama
    /// bütünün yüzdesi 100'de sınırlanır — "işin %130'u bitti" anlamsız
    /// bir cümledir, fazlalık sapma raporunun konusudur.
    /// </summary>
    private static decimal WeightedRate(
        IReadOnlyList<ContractSummaryProgressItem> items,
        Func<ContractSummaryProgressItem, decimal> quantity)
    {
        var totalWeight = items.Sum(x => x.ContractAmount);

        if (totalWeight <= 0m)
            return 0m;

        var completed = items.Sum(x =>
        {
            if (x.ContractQuantity <= 0m)
                return 0m;

            var rate = quantity(x) / x.ContractQuantity;
            return Math.Min(1m, Math.Max(0m, rate)) * x.ContractAmount;
        });

        return decimal.Round(completed / totalWeight * 100m, 2);
    }

    /// <summary>
    /// Hakedişlerdeki kümülatif kabul. İptal edilmiş hakedişler sayılmaz.
    /// Hem kalem kimliği hem poz kodu üzerinden döner: icmal bağı
    /// kurulmadan önce açılmış hakedişler yalnızca kodla eşleşebilir.
    /// </summary>
    private async Task<(Dictionary<Guid, decimal> ById, Dictionary<string, decimal> ByCode)>
        GetEmployerRealizationAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var rows = await db.ProgressPaymentItems
            .AsNoTracking()
            .Where(x =>
                x.ProgressPayment.ProjectId == projectId &&
                x.ProgressPayment.Status != ProgressPaymentStatus.Cancelled)
            .Select(x => new
            {
                x.ProjectBoqItemId,
                x.PositionCode,
                x.CurrentQuantity
            })
            .ToListAsync(cancellationToken);

        var byId = rows
            .Where(x => x.ProjectBoqItemId != null)
            .GroupBy(x => x.ProjectBoqItemId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.CurrentQuantity));

        var byCode = rows
            .Where(x => x.ProjectBoqItemId == null && !string.IsNullOrWhiteSpace(x.PositionCode))
            .GroupBy(x => x.PositionCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(x => x.CurrentQuantity),
                StringComparer.OrdinalIgnoreCase);

        return (byId, byCode);
    }

    /// <summary>
    /// Projenin geçerli icmali. Sıra ProgressTrackingService ile aynı:
    /// önce sözleşme tabanı işaretli olan, yoksa onaylı güncel revizyon.
    /// İkisi de yoksa proje icmalsiz çalışıyor demektir.
    /// </summary>
    private async Task<BoqRef?> ResolveSummaryAsync(
        Guid projectId, CancellationToken cancellationToken)
    {
        var baseline = await db.ProjectBoqs
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.IsContractBaseline)
            .Select(x => new BoqRef(x.Id, x.BoqNumber))
            .SingleOrDefaultAsync(cancellationToken);

        if (baseline is not null)
            return baseline;

        return await db.ProjectBoqs
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId &&
                        x.IsCurrentRevision &&
                        x.Status == ProjectBoqStatus.Approved)
            .OrderByDescending(x => x.RevisionNumber)
            .Select(x => new BoqRef(x.Id, x.BoqNumber))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private sealed record BoqRef(Guid Id, string BoqNumber);
}
