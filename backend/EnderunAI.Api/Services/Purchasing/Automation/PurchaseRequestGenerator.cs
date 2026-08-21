using EnderunAI.Api.Contracts.Inventory;
using EnderunAI.Api.Contracts.Purchasing;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.DocumentNumbers;
using EnderunAI.Api.Services.Engineering;
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
                        y.InventoryItemId,
                        y.MaterialCode,
                        y.MaterialName,
                        y.Quantity,
                        y.Unit,
                        y.WastePercent
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        // HESAP BURADA YAPILMIYOR: malzeme ihtiyacı ortak motordan
        // (MaterialRequirementCalculator) okunuyor. Aynı hesap proje
        // malzeme tedarikinde de kullanılıyor; iki kopya zamanla
        // ayrışır ve aynı iş için iki farklı miktar üretirdi.
        var sources = sourceItems
            .Select(offerItem =>
            {
                var positionId = ResolvePositionId(
                    offerItem,
                    positionIds,
                    positionIdByCode);

                var recipe = offerItem.EngineeringRecipeId.HasValue
                    ? recipes.FirstOrDefault(
                        x => x.Id == offerItem.EngineeringRecipeId.Value)
                    : recipes
                        .Where(x => x.EngineeringPositionId == positionId)
                        .OrderByDescending(x => x.IsDefault)
                        .ThenByDescending(x => x.Version)
                        .FirstOrDefault();

                return new MaterialRequirementSource(
                    offerItem.LineNumber,
                    offerItem.PositionNumber,
                    null,
                    offerItem.Quantity,
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

        if (requirement.Materials.Count == 0)
        {
            var missingText = requirement.MissingRecipes.Count > 0
                ? " Reçetesi bulunamayan teklif satırları: " +
                  string.Join(", ", requirement.MissingRecipes.Select(x => x.LineNumber))
                : string.Empty;

            throw new InvalidOperationException(
                "Teklif reçetelerinden satın alma malzemesi üretilemedi." +
                missingText + ".");
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

        foreach (var material in requirement.Materials)
        {
            var description =
                string.IsNullOrWhiteSpace(material.MaterialCode)
                    ? material.MaterialName
                    : $"{material.MaterialCode} | {material.MaterialName}";

            entity.Items.Add(new PurchaseRequestItem
            {
                LineNumber = lineNumber++,

                // Stok kartı bağı taşınıyor: talep kalemi hangi
                // malzemeye ait, sonradan sorulabilsin. Reçete kartsız
                // kurulmuşsa null kalır.
                InventoryItemId = material.InventoryItemId,

                MaterialDescription = description,
                Quantity = material.Quantity,
                Unit = material.Unit,
                RequestedDeliveryDate = request.NeededByDate?.Date,
                Notes =
                    $"Kaynak teklif: {offer.OfferNumber}. " +
                    $"Teklif satırları: {string.Join(", ", material.SourceLineNumbers)}."
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

    public async Task<GeneratePurchaseRequestFromStockLevelsResponse>
        GenerateFromStockLevelsAsync(
            GeneratePurchaseRequestFromStockLevelsRequest request,
            Guid? requestedByUserId,
            CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RequestedByName))
            throw new ArgumentException("Talep eden kişi zorunludur.");

        if (!Enum.IsDefined(typeof(PurchaseRequestPriority), request.Priority))
            throw new ArgumentException("Geçersiz satın alma talebi önceliği.");

        if (request.Lines is null || request.Lines.Count == 0)
            throw new ArgumentException("Talebe alınacak en az bir malzeme seçilmelidir.");

        if (request.Lines.Any(x => x.Quantity <= 0m))
            throw new ArgumentException("Talep miktarı sıfırdan büyük olmalıdır.");

        // Aynı malzeme iki kez seçilirse tek satırda toplanmaz, HATA
        // verilir: hangisinin geçerli olduğu kullanıcının kararı,
        // sessizce toplamak istemediği bir miktar sipariş ettirebilir.
        var duplicate = request.Lines
            .GroupBy(x => x.InventoryItemId)
            .Any(x => x.Count() > 1);

        if (duplicate)
            throw new ArgumentException("Aynı malzeme talepte birden fazla kez yer alamaz.");

        var warehouse = await db.Warehouses
            .AsNoTracking()
            .Where(x => x.Id == request.WarehouseId)
            .Select(x => new { x.Id, x.Name, x.CompanyId })
            .SingleOrDefaultAsync(cancellationToken);

        if (warehouse is null)
            throw new KeyNotFoundException("Talebin açılacağı depo bulunamadı.");

        var project = await db.Projects
            .AsNoTracking()
            .Where(x => x.Id == request.ProjectId && x.CompanyId == warehouse.CompanyId)
            .Select(x => new { x.Id, x.Name })
            .SingleOrDefaultAsync(cancellationToken);

        // Depo ikmali gerçekte projesiz bir iştir; ama talep kaydı
        // projeye bağlı (bütçe onayı ve raporlama oradan besleniyor).
        // Bu yüzden proje ZORUNLU ve deponun şirketiyle aynı olmalı —
        // başka şirketin bütçesine yazılan bir ikmal talebi sessiz bir
        // maliyet kayması olurdu.
        if (project is null)
            throw new KeyNotFoundException("Proje bulunamadı veya deponun şirketine ait değil.");

        var itemIds = request.Lines.Select(x => x.InventoryItemId).ToList();

        var levels = await db.WarehouseStockLevels
            .AsNoTracking()
            .Where(x => x.WarehouseId == request.WarehouseId &&
                        itemIds.Contains(x.InventoryItemId))
            .Select(x => new
            {
                x.InventoryItemId,
                Code = x.InventoryItem.Code,
                Name = x.InventoryItem.Name,
                x.InventoryItem.Unit,
                x.MinimumQuantity,
                x.MaximumQuantity
            })
            .ToListAsync(cancellationToken);

        // Seviye tanımı olmayan kalem bu yoldan talep edilemez: bu uç
        // "asgarinin altına düştü" gerekçesiyle talep açıyor. Gerekçesi
        // olmayan kalem serbest talep ekranından istenmeli, yoksa
        // otomasyon kapısı elle talep kapısına dönüşürdü.
        var missing = itemIds
            .Where(id => levels.All(x => x.InventoryItemId != id))
            .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Seçilen kalemlerden bazılarının bu depoda stok seviyesi tanımlı değil: " +
                string.Join(", ", missing));
        }

        var requestNumber = await documentNumbers.GenerateAsync(
            warehouse.CompanyId,
            "PURCHASE_REQUEST",
            "PR",
            cancellationToken);

        var entity = new PurchaseRequest
        {
            CompanyId = warehouse.CompanyId,
            ProjectId = project.Id,
            RequestNumber = requestNumber,
            RequestDate = DateTime.UtcNow.Date,
            NeededByDate = request.NeededByDate?.Date,
            RequestedByName = request.RequestedByName.Trim(),
            RequestedByUserId = requestedByUserId,
            Description = string.IsNullOrWhiteSpace(request.Description)
                ? $"Stok seviyesi uyarısından otomatik oluşturuldu: {warehouse.Name}"
                : request.Description.Trim(),
            Priority = (PurchaseRequestPriority)request.Priority,
            Status = PurchaseRequestStatus.Draft
        };

        var lineNumber = 1;

        foreach (var line in request.Lines)
        {
            var level = levels.Single(x => x.InventoryItemId == line.InventoryItemId);

            var maximumText = level.MaximumQuantity is decimal max
                ? $", azami {max:0.####}"
                : ", azami tanımsız";

            entity.Items.Add(new PurchaseRequestItem
            {
                LineNumber = lineNumber++,
                InventoryItemId = level.InventoryItemId,
                MaterialDescription = $"{level.Code} | {level.Name}",
                Quantity = line.Quantity,
                Unit = level.Unit,

                // Marka serbest: ikmal talebinde marka kararı satın
                // almanın; kart üzerinde tercihli tedarikçi varsa o
                // zaten teklif aşamasında görünür.
                BrandIrrelevant = true,

                RequestedDeliveryDate = request.NeededByDate?.Date,
                Notes =
                    $"Kaynak: {warehouse.Name} deposu stok seviyesi " +
                    $"(asgari {level.MinimumQuantity:0.####}{maximumText})."
            });
        }

        db.PurchaseRequests.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return new GeneratePurchaseRequestFromStockLevelsResponse(
            entity.Id,
            entity.RequestNumber,
            warehouse.Id,
            warehouse.Name,
            entity.Items.Count,
            decimal.Round(entity.Items.Sum(x => x.Quantity), 4));
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
}
