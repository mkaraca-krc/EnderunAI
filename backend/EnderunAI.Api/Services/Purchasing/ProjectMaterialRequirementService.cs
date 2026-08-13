using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Engineering;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Purchasing;

public sealed record ProjectMaterialRequirementLine(
    Guid? InventoryItemId,
    string MaterialCode,
    string MaterialName,
    string Unit,
    /// <summary>Reçetelerden çıkan brüt ihtiyaç (fire dahil).</summary>
    decimal RequiredQuantity,
    /// <summary>Kapsam dahilindeki depolardaki fiili mevcut.</summary>
    decimal StockQuantity,
    /// <summary>Bu proje için AÇIK taleplerde bekleyen miktar.</summary>
    decimal OpenRequestedQuantity,
    /// <summary>ihtiyaç − mevcut − açık talep (negatife düşmez).</summary>
    decimal ShortageQuantity,
    /// <summary>Kaç icmal kaleminden besleniyor.</summary>
    int SourceLineCount,
    /// <summary>
    /// Talep edilebilir mi. Stok kartı bağı olmayan malzeme talep
    /// edilemez: mevcut ve açık talep onun üzerinden düşülüyor,
    /// bağsız satır ikinci kez talep edilmeyi engelleyemez.
    /// </summary>
    bool CanRequest);

public sealed record ProjectMaterialRequirementResult(
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    Guid? BoqId,
    string? BoqNumber,
    /// <summary>Hangi icmalden okundu — onaylı yoksa taslak kullanılır.</summary>
    string? BoqStatusName,
    int PositionLineCount,
    int PositionsWithoutRecipe,
    bool IncludesCentralWarehouse,
    IReadOnlyList<ProjectMaterialRequirementLine> Lines,
    IReadOnlyList<MaterialRequirementIssue> MissingRecipes,
    IReadOnlyList<MaterialRequirementIssue> UnitConflicts,
    IReadOnlyList<string> Warnings);

public interface IProjectMaterialRequirementService
{
    Task<ProjectMaterialRequirementResult> GetAsync(
        Guid projectId,
        bool includeCentralWarehouse,
        CancellationToken cancellationToken);
}

/// <summary>
/// PROJE MALZEME İHTİYACI: icmal → reçete → ihtiyaç, sonra depo ve
/// açık talep düşülerek EKSİK.
///
///   eksik = ihtiyaç − depo mevcudu − açık talepler
///
/// İhtiyacın kendisi burada HESAPLANMAZ; ortak motordan
/// (<see cref="MaterialRequirementCalculator"/>) okunur. Teklif yolu da
/// aynı motoru kullanıyor — iki kopya zamanla ayrışır ve aynı iş için
/// iki farklı miktar üretirdi.
///
/// AÇIK TALEBİN DÜŞÜLMESİ ÇİFT SAYIMI ÖNLER: ekran iki kez
/// çalıştırıldığında aynı malzeme ikinci kez talep edilmemeli.
/// Tamamlanmış talepler düşülmez — o malzeme artık depoda görünür,
/// ikisini birden düşmek ihtiyacı iki kez azaltırdı.
/// </summary>
public sealed class ProjectMaterialRequirementService(AppDbContext db)
    : IProjectMaterialRequirementService
{
    /// <summary>
    /// Miktarı hâlâ beklenen talep durumları. Tamamlanan (mal kabul
    /// yapılmış) ve iptal/red edilenler dışarıda; düzeltmeye iade
    /// edilen talep hâlâ açıktır, sahibi düzeltip yeniden gönderecek.
    /// </summary>
    public static readonly PurchaseRequestStatus[] OpenStatuses =
    [
        PurchaseRequestStatus.Draft,
        PurchaseRequestStatus.Submitted,
        PurchaseRequestStatus.Approved,
        PurchaseRequestStatus.Quotation,
        PurchaseRequestStatus.Ordered,
        PurchaseRequestStatus.ReturnedForRevision
    ];

    public async Task<ProjectMaterialRequirementResult> GetAsync(
        Guid projectId,
        bool includeCentralWarehouse,
        CancellationToken cancellationToken)
    {
        var project = await db.Projects
            .AsNoTracking()
            .Where(x => x.Id == projectId)
            .Select(x => new { x.Id, x.Code, x.Name, x.CompanyId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Proje bulunamadı.");

        var warnings = new List<string>();

        // İCMAL SEÇİMİ: onaylı icmal varsa o, yoksa taslak. Arşiv ve
        // yerine geçilmiş sürümler okunmaz — eski sözleşmeden malzeme
        // talep edilmemeli.
        var boq = await db.ProjectBoqs
            .AsNoTracking()
            .Where(x =>
                x.ProjectId == projectId &&
                (x.Status == ProjectBoqStatus.Approved ||
                 x.Status == ProjectBoqStatus.Draft))
            .OrderByDescending(x => x.Status == ProjectBoqStatus.Approved)
            .ThenByDescending(x => x.RevisionNumber)
            .Select(x => new { x.Id, x.BoqNumber, x.Status })
            .FirstOrDefaultAsync(cancellationToken);

        if (boq is null)
        {
            warnings.Add(
                "Projede okunabilir bir sözleşme icmali yok; malzeme ihtiyacı " +
                "hesaplanamadı.");

            return new ProjectMaterialRequirementResult(
                project.Id, project.Code, project.Name,
                null, null, null, 0, 0, includeCentralWarehouse,
                [], [], [], warnings);
        }

        if (boq.Status == ProjectBoqStatus.Draft)
        {
            warnings.Add(
                "İhtiyaç TASLAK icmalden hesaplandı; onaylı icmal " +
                "bulunmuyor. Miktarlar değişebilir.");
        }

        var boqItems = await db.ProjectBoqItems
            .AsNoTracking()
            .Where(x => x.ProjectBoqId == boq.Id)
            .OrderBy(x => x.LineNumber)
            .Select(x => new
            {
                x.LineNumber,
                x.EngineeringPositionId,
                x.PositionCode,
                x.Description,
                x.ContractQuantity
            })
            .ToListAsync(cancellationToken);

        var positionIds = boqItems
            .Where(x => x.EngineeringPositionId.HasValue)
            .Select(x => x.EngineeringPositionId!.Value)
            .Distinct()
            .ToList();

        // Poza bağlanmamış icmal kalemi reçeteye ulaşamaz; sessiz
        // geçmesin diye uyarıya yazılır.
        var unlinkedCount = boqItems.Count(x => !x.EngineeringPositionId.HasValue);

        if (unlinkedCount > 0)
        {
            warnings.Add(
                $"{unlinkedCount} icmal kalemi bir poza bağlı değil; bu kalemler " +
                "ihtiyaca katılmadı.");
        }

        var recipes = await db.EngineeringRecipes
            .AsNoTracking()
            .Where(x => positionIds.Contains(x.EngineeringPositionId) && x.IsDefault)
            .Select(x => new
            {
                x.EngineeringPositionId,
                x.Version,
                Materials = x.Materials
                    .Select(y => new
                    {
                        y.InventoryItemId,
                        y.MaterialCode,
                        y.MaterialName,
                        y.Unit,
                        y.Quantity,
                        y.WastePercent
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        var recipeByPosition = recipes
            .GroupBy(x => x.EngineeringPositionId)
            .ToDictionary(
                x => x.Key,
                x => x.OrderByDescending(y => y.Version).First());

        var sources = boqItems
            .Where(x => x.EngineeringPositionId.HasValue)
            .Select(item =>
            {
                var recipe = recipeByPosition.GetValueOrDefault(
                    item.EngineeringPositionId!.Value);

                return new MaterialRequirementSource(
                    item.LineNumber,
                    item.PositionCode,
                    item.Description,
                    item.ContractQuantity,
                    recipe?.Materials
                        .Select(y => new MaterialRequirementRecipeLine(
                            y.InventoryItemId,
                            y.MaterialCode,
                            y.MaterialName,
                            y.Unit,
                            y.Quantity,
                            y.WastePercent))
                        .ToList());
            })
            .ToList();

        var requirement = MaterialRequirementCalculator.Calculate(sources);

        var itemIds = requirement.Materials
            .Where(x => x.InventoryItemId.HasValue)
            .Select(x => x.InventoryItemId!.Value)
            .Distinct()
            .ToList();

        var stockByItem = await LoadStockAsync(
            projectId, itemIds, includeCentralWarehouse, cancellationToken);

        var openByItem = await LoadOpenRequestsAsync(
            projectId, itemIds, cancellationToken);

        var lines = requirement.Materials
            .Select(material =>
            {
                var stock = material.InventoryItemId is Guid id
                    ? stockByItem.GetValueOrDefault(id)
                    : 0m;

                var open = material.InventoryItemId is Guid openId
                    ? openByItem.GetValueOrDefault(openId)
                    : 0m;

                var shortage = Math.Max(
                    0m,
                    decimal.Round(material.Quantity - stock - open, 4));

                return new ProjectMaterialRequirementLine(
                    material.InventoryItemId,
                    material.MaterialCode,
                    material.MaterialName,
                    material.Unit,
                    material.Quantity,
                    stock,
                    open,
                    shortage,
                    material.SourceLineNumbers.Count,
                    material.InventoryItemId.HasValue);
            })
            .ToList();

        var withoutCard = lines.Count(x => !x.CanRequest);

        if (withoutCard > 0)
        {
            warnings.Add(
                $"{withoutCard} malzeme stok kartına bağlı değil; depo mevcudu " +
                "ve açık talep düşülemediği için talep edilemez. Reçetede stok " +
                "kartı seçin.");
        }

        return new ProjectMaterialRequirementResult(
            project.Id,
            project.Code,
            project.Name,
            boq.Id,
            boq.BoqNumber,
            boq.Status.ToString(),
            sources.Count,
            requirement.MissingRecipes.Count,
            includeCentralWarehouse,
            lines,
            requirement.MissingRecipes,
            requirement.UnitConflicts,
            warnings);
    }

    /// <summary>
    /// Kapsam: projenin kendi depoları. Merkez depo yalnız istenirse
    /// katılır — merkez stok şirket geneline ait, başka bir projeye
    /// ayrılmış olabilir; varsayılan olarak düşmek olmayan malzemeyi
    /// var saymak olurdu.
    /// </summary>
    private async Task<Dictionary<Guid, decimal>> LoadStockAsync(
        Guid projectId,
        IReadOnlyCollection<Guid> itemIds,
        bool includeCentralWarehouse,
        CancellationToken cancellationToken)
    {
        if (itemIds.Count == 0)
            return [];

        var rows = await db.WarehouseStocks
            .AsNoTracking()
            .Where(x =>
                itemIds.Contains(x.InventoryItemId) &&
                (x.Warehouse.ProjectId == projectId ||
                 (includeCentralWarehouse && x.Warehouse.ProjectId == null)))
            .GroupBy(x => x.InventoryItemId)
            .Select(x => new { ItemId = x.Key, Quantity = x.Sum(y => y.Quantity) })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(x => x.ItemId, x => x.Quantity);
    }

    private async Task<Dictionary<Guid, decimal>> LoadOpenRequestsAsync(
        Guid projectId,
        IReadOnlyCollection<Guid> itemIds,
        CancellationToken cancellationToken)
    {
        if (itemIds.Count == 0)
            return [];

        var rows = await db.PurchaseRequestItems
            .AsNoTracking()
            .Where(x =>
                x.InventoryItemId.HasValue &&
                itemIds.Contains(x.InventoryItemId!.Value) &&
                x.PurchaseRequest.ProjectId == projectId &&
                OpenStatuses.Contains(x.PurchaseRequest.Status))
            .GroupBy(x => x.InventoryItemId!.Value)
            .Select(x => new { ItemId = x.Key, Quantity = x.Sum(y => y.Quantity) })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(x => x.ItemId, x => x.Quantity);
    }
}
