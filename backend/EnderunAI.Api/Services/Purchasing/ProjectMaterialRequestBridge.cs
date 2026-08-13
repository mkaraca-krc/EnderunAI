using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.DocumentNumbers;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Purchasing;

public sealed record MaterialRequestBridgeLine(
    Guid InventoryItemId,
    decimal Quantity);

public sealed record CreateMaterialRequestFromRequirementRequest(
    string RequestedByName,
    DateTime? NeededByDate,
    int Priority,
    IReadOnlyList<MaterialRequestBridgeLine> Lines);

public sealed record CreateMaterialRequestFromRequirementResult(
    Guid PurchaseRequestId,
    string RequestNumber,
    int ItemCount,
    decimal TotalQuantity,
    /// <summary>
    /// Talebe girmeyen ya da kırpılan satırlar — sessizce düşmesin.
    /// </summary>
    IReadOnlyList<string> Adjustments);

public interface IProjectMaterialRequestBridge
{
    Task<CreateMaterialRequestFromRequirementResult> CreateAsync(
        Guid projectId,
        CreateMaterialRequestFromRequirementRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// ÖNERİ → TALEP KÖPRÜSÜ. Eksik listesinden kullanıcının seçtiği
/// satırlar TASLAK satın alma talebine dönüşür ve mevcut onay
/// döngüsüne girer. Otomatik talep AÇILMAZ: ihtiyaç tüm proje süresi
/// için hesaplanır, satın alma ise zamanlıdır — üç ay sonra lazım olan
/// malzemeyi bugün sipariş etmek parayı ve depoyu bağlar.
///
/// ÇİFT SAYIM KORUMASI BURADA: istenen miktar, kaydetme anında
/// yeniden hesaplanan EKSİK ile sınırlanır. Ekranın gördüğü eksik
/// bayat olabilir (arada başka biri talep açmış olabilir); istemciden
/// gelen sayıya güvenilseydi aynı ihtiyaç iki kez talep edilirdi.
/// </summary>
public sealed class ProjectMaterialRequestBridge(
    AppDbContext db,
    IProjectMaterialRequirementService requirementService,
    IDocumentNumberService documentNumbers) : IProjectMaterialRequestBridge
{
    public async Task<CreateMaterialRequestFromRequirementResult> CreateAsync(
        Guid projectId,
        CreateMaterialRequestFromRequirementRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RequestedByName))
            throw new ArgumentException("Talep eden kişi girilmelidir.");

        if (!Enum.IsDefined(typeof(PurchaseRequestPriority), request.Priority))
            throw new ArgumentException("Geçersiz talep önceliği.");

        if (request.Lines.Count == 0)
            throw new ArgumentException("En az bir malzeme seçilmelidir.");

        var project = await db.Projects
            .AsNoTracking()
            .Where(x => x.Id == projectId)
            .Select(x => new { x.Id, x.CompanyId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Proje bulunamadı.");

        // Eksik SUNUCUDA yeniden hesaplanıyor; istemciden gelen miktar
        // yalnızca bir TALEPTİR, üst sınır buradan gelir.
        var requirement = await requirementService.GetAsync(
            projectId,
            includeCentralWarehouse: false,
            cancellationToken);

        var shortageByItem = requirement.Lines
            .Where(x => x.InventoryItemId.HasValue)
            .ToDictionary(x => x.InventoryItemId!.Value, x => x);

        var adjustments = new List<string>();
        var accepted = new List<(ProjectMaterialRequirementLine Line, decimal Quantity)>();

        foreach (var line in request.Lines)
        {
            if (!shortageByItem.TryGetValue(line.InventoryItemId, out var requirementLine))
            {
                adjustments.Add(
                    "Seçilen malzemelerden biri projenin güncel ihtiyaç listesinde " +
                    "yok; talebe eklenmedi.");

                continue;
            }

            if (requirementLine.ShortageQuantity <= 0)
            {
                adjustments.Add(
                    $"{requirementLine.MaterialName}: eksik kalmadı " +
                    "(depo mevcudu ve açık talepler ihtiyacı karşılıyor), eklenmedi.");

                continue;
            }

            var quantity = line.Quantity <= 0
                ? requirementLine.ShortageQuantity
                : Math.Min(line.Quantity, requirementLine.ShortageQuantity);

            if (line.Quantity > requirementLine.ShortageQuantity)
            {
                adjustments.Add(
                    $"{requirementLine.MaterialName}: istenen " +
                    $"{line.Quantity} {requirementLine.Unit}, kalan eksiğe " +
                    $"({requirementLine.ShortageQuantity} {requirementLine.Unit}) " +
                    "indirildi.");
            }

            accepted.Add((requirementLine, decimal.Round(quantity, 4)));
        }

        if (accepted.Count == 0)
        {
            throw new InvalidOperationException(
                "Talep oluşturulmadı: seçilen malzemelerde açık eksik kalmamış. " +
                string.Join(" ", adjustments));
        }

        var itemIds = accepted.Select(x => x.Line.InventoryItemId!.Value).ToList();

        // Marka STOK KARTINDAN geliyor: kartta marka varsa istenen marka
        // olarak taşınır, yoksa "farketmez". Marka kuralı zincirde
        // yaşıyor; talep kartsız açılsaydı zincire boş marka giderdi.
        var cards = await db.InventoryItems
            .AsNoTracking()
            .Where(x => itemIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Brand })
            .ToListAsync(cancellationToken);

        var brandByItem = cards.ToDictionary(x => x.Id, x => x.Brand);

        var requestNumber = await documentNumbers.GenerateAsync(
            project.CompanyId,
            "PURCHASE_REQUEST",
            "PR",
            cancellationToken);

        var entity = new PurchaseRequest
        {
            CompanyId = project.CompanyId,
            ProjectId = projectId,
            RequestNumber = requestNumber,
            RequestDate = DateTime.UtcNow.Date,
            NeededByDate = request.NeededByDate?.Date,
            RequestedByName = request.RequestedByName.Trim(),
            Description =
                $"Proje malzeme ihtiyacından oluşturuldu " +
                $"(icmal {requirement.BoqNumber}).",
            Priority = (PurchaseRequestPriority)request.Priority,
            Status = PurchaseRequestStatus.Draft
        };

        var lineNumber = 1;

        foreach (var (line, quantity) in accepted)
        {
            var brand = brandByItem.GetValueOrDefault(line.InventoryItemId!.Value);

            entity.Items.Add(new PurchaseRequestItem
            {
                LineNumber = lineNumber++,
                InventoryItemId = line.InventoryItemId,
                MaterialDescription = string.IsNullOrWhiteSpace(line.MaterialCode)
                    ? line.MaterialName
                    : $"{line.MaterialCode} | {line.MaterialName}",
                Quantity = quantity,
                Unit = line.Unit,
                RequestedDeliveryDate = request.NeededByDate?.Date,
                RequestedBrand = string.IsNullOrWhiteSpace(brand) ? null : brand.Trim(),
                BrandIrrelevant = string.IsNullOrWhiteSpace(brand),
                Notes =
                    $"İhtiyaç {line.RequiredQuantity} {line.Unit}, " +
                    $"depo {line.StockQuantity}, açık talep {line.OpenRequestedQuantity}."
            });
        }

        db.PurchaseRequests.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateMaterialRequestFromRequirementResult(
            entity.Id,
            entity.RequestNumber,
            entity.Items.Count,
            decimal.Round(entity.Items.Sum(x => x.Quantity), 4),
            adjustments);
    }
}
