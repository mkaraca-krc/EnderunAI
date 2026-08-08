using EnderunAI.Api.Contracts.Purchasing;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.DocumentNumbers;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Purchasing.Automation;

public sealed class PurchaseRequestGenerator(
    AppDbContext db,
    IDocumentNumberService documentNumbers)
    : IPurchaseRequestGenerator
{
    public async Task<GeneratePurchaseRequestFromOfferResponse>
        GenerateFromOfferAsync(
            Guid offerId,
            GeneratePurchaseRequestFromOfferRequest request,
            CancellationToken cancellationToken)
    {
        ValidateRequest(offerId, request);

        var offer = await db.Offers
            .AsNoTracking()
            .Include(x => x.Items)
            .SingleOrDefaultAsync(
                x => x.Id == offerId,
                cancellationToken);

        if (offer is null)
        {
            throw new KeyNotFoundException(
                "Satın alma talebi oluşturulacak teklif bulunamadı.");
        }

        if (!offer.ProjectId.HasValue)
        {
            throw new InvalidOperationException(
                "Satın alma talebi oluşturabilmek için teklif bir projeye bağlı olmalıdır.");
        }

        // Eskiden Approved(2) durumu da kabul ediliyordu. O değer artık
        // BEKLEMEDE anlamına geliyor (karşı taraftan cevap bekliyoruz)
        // ve henüz kazanmadığımız bir iş için malzeme talebi açmak
        // bağlayıcı olmayan bir teklif yüzünden gerçek para harcamak
        // demek olurdu. Canlıda hiçbir teklif o durumda olmadığı için
        // davranış kaybı yok.
        if (offer.Status is not OfferStatus.Won)
        {
            throw new InvalidOperationException(
                "Yalnızca kazanılmış tekliflerden satın alma talebi oluşturulabilir.");
        }

        if (offer.Items.Count == 0)
        {
            throw new InvalidOperationException(
                "Teklifte satın alma talebine dönüştürülecek kalem bulunmuyor.");
        }

        var sourceItems = offer.Items
            .Where(x =>
                x.Quantity > 0 &&
                (x.EngineeringRecipeId.HasValue ||
                 x.EngineeringPositionId.HasValue ||
                 !string.IsNullOrWhiteSpace(x.PositionNumber)))
            .OrderBy(x => x.LineNumber)
            .ToList();

        if (sourceItems.Count == 0)
        {
            throw new InvalidOperationException(
                "Teklif kalemlerinde mühendislik pozu veya reçete bağlantısı bulunmuyor.");
        }

        var engineeringPositionIds = sourceItems
            .Where(x => x.EngineeringPositionId.HasValue)
            .Select(x => x.EngineeringPositionId!.Value)
            .Distinct()
            .ToList();

        var positionNumbers = sourceItems
            .Where(x => !string.IsNullOrWhiteSpace(x.PositionNumber))
            .Select(x => x.PositionNumber!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var positions = await db.EngineeringPositions
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == offer.CompanyId &&
                (engineeringPositionIds.Contains(x.Id) ||
                 positionNumbers.Contains(x.Code)))
            .Select(x => new
            {
                x.Id,
                x.Code
            })
            .ToListAsync(cancellationToken);

        var positionIds = positions
            .Select(x => x.Id)
            .ToHashSet();

        var positionIdByCode = positions
            .GroupBy(
                x => x.Code,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => x.First().Id,
                StringComparer.OrdinalIgnoreCase);

        var requestedRecipeIds = sourceItems
            .Where(x => x.EngineeringRecipeId.HasValue)
            .Select(x => x.EngineeringRecipeId!.Value)
            .Distinct()
            .ToList();

        var resolvedPositionIds = sourceItems
            .Select(item =>
            {
                if (item.EngineeringPositionId.HasValue &&
                    positionIds.Contains(
                        item.EngineeringPositionId.Value))
                {
                    return item.EngineeringPositionId.Value;
                }

                if (!string.IsNullOrWhiteSpace(item.PositionNumber) &&
                    positionIdByCode.TryGetValue(
                        item.PositionNumber.Trim(),
                        out var resolvedPositionId))
                {
                    return resolvedPositionId;
                }

                return Guid.Empty;
            })
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();

        var recipes = await db.EngineeringRecipes
            .AsNoTracking()
            .Where(x =>
                requestedRecipeIds.Contains(x.Id) ||
                resolvedPositionIds.Contains(
                    x.EngineeringPositionId))
            .Select(x => new
            {
                x.Id,
                x.EngineeringPositionId,
                x.Version,
                x.IsDefault,

                Materials = x.Materials
                    .OrderBy(y => y.MaterialName)
                    .Select(y => new
                    {
                        y.MaterialCode,
                        y.MaterialName,
                        y.Quantity,
                        y.Unit,
                        y.WastePercent
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        var consolidatedMaterials =
            new Dictionary<string, ConsolidatedMaterial>(
                StringComparer.OrdinalIgnoreCase);

        var missingRecipeLines = new List<int>();

        foreach (var offerItem in sourceItems)
        {
            var positionId = ResolvePositionId(
                offerItem,
                positionIds,
                positionIdByCode);

            var recipe = offerItem.EngineeringRecipeId.HasValue
                ? recipes.FirstOrDefault(
                    x => x.Id ==
                         offerItem.EngineeringRecipeId.Value)
                : recipes
                    .Where(x =>
                        x.EngineeringPositionId == positionId)
                    .OrderByDescending(x => x.IsDefault)
                    .ThenByDescending(x => x.Version)
                    .FirstOrDefault();

            if (recipe is null)
            {
                missingRecipeLines.Add(offerItem.LineNumber);
                continue;
            }

            foreach (var material in recipe.Materials)
            {
                var materialCode =
                    material.MaterialCode?.Trim() ?? string.Empty;

                var materialName =
                    material.MaterialName.Trim();

                var unit = material.Unit.Trim();

                var effectiveRecipeQuantity = decimal.Round(
                    material.Quantity *
                    (1m + material.WastePercent / 100m),
                    6);

                var requiredQuantity = decimal.Round(
                    offerItem.Quantity *
                    effectiveRecipeQuantity,
                    4);

                if (requiredQuantity <= 0)
                    continue;

                var groupingIdentity =
                    !string.IsNullOrWhiteSpace(materialCode)
                        ? $"CODE:{materialCode}|UNIT:{unit}"
                        : $"NAME:{materialName}|UNIT:{unit}";

                if (!consolidatedMaterials.TryGetValue(
                        groupingIdentity,
                        out var consolidated))
                {
                    consolidated =
                        new ConsolidatedMaterial(
                            materialCode,
                            materialName,
                            unit);

                    consolidatedMaterials.Add(
                        groupingIdentity,
                        consolidated);
                }

                consolidated.Quantity += requiredQuantity;
                consolidated.SourceOfferLines.Add(
                    offerItem.LineNumber);
            }
        }

        if (consolidatedMaterials.Count == 0)
        {
            var missingText = missingRecipeLines.Count > 0
                ? $" Reçetesi bulunamayan teklif satırları: {string.Join(", ", missingRecipeLines)}."
                : string.Empty;

            throw new InvalidOperationException(
                "Teklif reçetelerinden satın alma malzemesi üretilemedi." +
                missingText);
        }

        var requestNumber =
            await documentNumbers.GenerateAsync(
                offer.CompanyId,
                "PURCHASE_REQUEST",
                "PR",
                cancellationToken);

        var entity = new PurchaseRequest
        {
            CompanyId = offer.CompanyId,
            ProjectId = offer.ProjectId.Value,
            RequestNumber = requestNumber,
            RequestDate = DateTime.UtcNow.Date,
            NeededByDate = request.NeededByDate?.Date,
            RequestedByName = request.RequestedByName.Trim(),
            Description =
                $"Tekliften otomatik oluşturuldu: {offer.OfferNumber} - {offer.Title}",
            Priority =
                (PurchaseRequestPriority)request.Priority,
            Status = PurchaseRequestStatus.Draft
        };

        var lineNumber = 1;

        foreach (var material in consolidatedMaterials.Values
                     .OrderBy(x => x.MaterialName)
                     .ThenBy(x => x.Unit))
        {
            var description =
                string.IsNullOrWhiteSpace(material.MaterialCode)
                    ? material.MaterialName
                    : $"{material.MaterialCode} | {material.MaterialName}";

            entity.Items.Add(new PurchaseRequestItem
            {
                LineNumber = lineNumber++,
                MaterialDescription = description,
                Quantity = decimal.Round(
                    material.Quantity,
                    4),
                Unit = material.Unit,
                RequestedDeliveryDate =
                    request.NeededByDate?.Date,
                Notes =
                    $"Kaynak teklif: {offer.OfferNumber}. " +
                    $"Teklif satırları: {string.Join(", ", material.SourceOfferLines.OrderBy(x => x))}."
            });
        }

        db.PurchaseRequests.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return new GeneratePurchaseRequestFromOfferResponse(
            entity.Id,
            entity.RequestNumber,
            offer.Id,
            offer.OfferNumber,
            sourceItems.Count,
            entity.Items.Count,
            decimal.Round(
                entity.Items.Sum(x => x.Quantity),
                4));
    }

    private static Guid ResolvePositionId(
        OfferItem offerItem,
        IReadOnlySet<Guid> positionIds,
        IReadOnlyDictionary<string, Guid> positionIdByCode)
    {
        if (offerItem.EngineeringPositionId.HasValue &&
            positionIds.Contains(
                offerItem.EngineeringPositionId.Value))
        {
            return offerItem.EngineeringPositionId.Value;
        }

        if (!string.IsNullOrWhiteSpace(
                offerItem.PositionNumber) &&
            positionIdByCode.TryGetValue(
                offerItem.PositionNumber.Trim(),
                out var resolvedPositionId))
        {
            return resolvedPositionId;
        }

        return Guid.Empty;
    }

    private static void ValidateRequest(
        Guid offerId,
        GeneratePurchaseRequestFromOfferRequest request)
    {
        if (offerId == Guid.Empty)
        {
            throw new ArgumentException(
                "Teklif kimliği zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(
                request.RequestedByName))
        {
            throw new ArgumentException(
                "Talep eden kişi zorunludur.");
        }

        if (!Enum.IsDefined(
                typeof(PurchaseRequestPriority),
                request.Priority))
        {
            throw new ArgumentException(
                "Geçersiz satın alma talebi önceliği.");
        }
    }

    private sealed class ConsolidatedMaterial(
        string materialCode,
        string materialName,
        string unit)
    {
        public string MaterialCode { get; } =
            materialCode;

        public string MaterialName { get; } =
            materialName;

        public string Unit { get; } =
            unit;

        public decimal Quantity { get; set; }

        public HashSet<int> SourceOfferLines { get; } =
            [];
    }
}
