using EnderunAI.Api.Contracts.Offers;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.DocumentNumbers;
using EnderunAI.Api.Services.Offers;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/offers")]
public sealed class OffersController(
    AppDbContext db,
    IDocumentNumberService documentNumbers) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.EngineeringView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] int? status,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var query = db.Offers.AsNoTracking();

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        if (projectId.HasValue)
            query = query.Where(x => x.ProjectId == projectId.Value);

        if (status.HasValue)
        {
            if (!Enum.IsDefined(typeof(OfferStatus), status.Value))
                return BadRequest(new { message = "Geçersiz teklif durumu." });

            query = query.Where(x => x.Status == (OfferStatus)status.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x =>
                x.OfferNumber.ToLower().Contains(term) ||
                x.Title.ToLower().Contains(term) ||
                (x.Project != null && x.Project.Name.ToLower().Contains(term)));
        }

        var rows = await query
            .OrderByDescending(x => x.OfferDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                CompanyName = x.Company.Name,
                x.ProjectId,
                ProjectName = x.Project != null ? x.Project.Name : null,
                x.CustomerId,
                x.OfferNumber,
                x.Title,
                x.OfferDate,
                x.ValidUntil,
                x.Currency,
                x.ExchangeRate,
                x.Status,
                x.Subtotal,
                x.DiscountTotal,
                x.CostTotal,
                x.ProfitTotal,
                x.GrandTotal,
                ItemCount = x.Items.Count
            })
            .ToListAsync(cancellationToken);

        return Ok(rows);
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.EngineeringView)]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var row = await db.Offers
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                CompanyName = x.Company.Name,
                x.ProjectId,
                ProjectName = x.Project != null ? x.Project.Name : null,
                x.CustomerId,
                x.OfferNumber,
                x.Title,
                x.OfferDate,
                x.ValidUntil,
                x.Currency,
                x.ExchangeRate,
                x.Status,
                x.Description,
                x.Notes,
                x.Subtotal,
                x.DiscountTotal,
                x.CostTotal,
                x.ProfitTotal,
                x.GrandTotal,
                Items = x.Items
                    .OrderBy(i => i.LineNumber)
                    .Select(i => new
                    {
                        i.Id,
                        i.LineNumber,
                        i.PositionNumber,
                        i.Description,
                        i.ManufacturerPriceListItemId,
                        i.ManufacturerName,
                        i.ProductCode,
                        i.Brand,
                        i.Model,
                        i.Quantity,
                        i.Unit,
                        i.ListPrice,
                        i.DiscountRate,
                        i.NetPurchasePrice,
                        i.FreightRate,
                        i.WasteRate,
                        i.FinanceRate,
                        i.GeneralExpenseRate,
                        i.ProfitRate,
                        i.UnitCost,
                        i.UnitSalesPrice,
                        i.CostTotal,
                        i.SalesTotal,
                        i.Notes
                    })
            })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? NotFound(new { message = "Teklif bulunamadı." })
            : Ok(row);
    }

    [HttpPost("calculate-item")]
    [RequirePermission(PermissionCatalog.Keys.EngineeringManage)]
    public IActionResult CalculateItem(CalculateOfferItemRequest request)
    {
        var validation = ValidateRates(
            request.Quantity,
            request.ListPrice,
            request.DiscountRate,
            request.FreightRate,
            request.WasteRate,
            request.FinanceRate,
            request.GeneralExpenseRate,
            request.ProfitRate);

        if (validation is not null)
            return BadRequest(new { message = validation });

        var result = Calculate(
            request.Quantity,
            request.ListPrice,
            request.DiscountRate,
            request.FreightRate,
            request.WasteRate,
            request.FinanceRate,
            request.GeneralExpenseRate,
            request.ProfitRate);

        return Ok(new CalculateOfferItemResponse(
            result.NetPurchasePrice,
            result.UnitCost,
            result.UnitSalesPrice,
            result.CostTotal,
            result.SalesTotal,
            result.ProfitTotal));
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.EngineeringManage)]
    public async Task<IActionResult> Create(
        CreateOfferRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { message = "Teklif başlığı zorunludur." });

        if (string.IsNullOrWhiteSpace(request.Currency))
            return BadRequest(new { message = "Para birimi zorunludur." });

        if (request.ExchangeRate <= 0)
            return BadRequest(new { message = "Kur sıfırdan büyük olmalıdır." });

        if (request.Items.Count == 0)
            return BadRequest(new { message = "Teklifte en az bir kalem bulunmalıdır." });

        var companyExists = await db.Companies.AnyAsync(
            x => x.Id == request.CompanyId && x.IsActive,
            cancellationToken);

        if (!companyExists)
            return BadRequest(new { message = "Geçerli bir şirket seçilmelidir." });

        if (request.ProjectId.HasValue)
        {
            var projectExists = await db.Projects.AnyAsync(
                x => x.Id == request.ProjectId.Value &&
                     x.CompanyId == request.CompanyId &&
                     x.IsActive,
                cancellationToken);

            if (!projectExists)
                return BadRequest(new { message = "Proje seçilen şirkete ait değil." });
        }

        var offerNumber = await documentNumbers.GenerateAsync(
            request.CompanyId,
            "OFFER",
            "TKL",
            cancellationToken);

        var entity = new Offer
        {
            CompanyId = request.CompanyId,
            ProjectId = request.ProjectId,
            CustomerId = request.CustomerId,
            CounterpartyCurrentAccountId = request.CounterpartyCurrentAccountId,
            CounterpartyRole = request.CounterpartyRole,
            Kind = request.Kind,
            OfferNumber = offerNumber,
            Title = request.Title.Trim(),
            // Tarihler UTC'ye normalize edilir: Kind belirtilmemiş bir
            // tarih Postgres'e yazılamıyor ve istek 500 ile düşüyordu.
            // İstemcinin saat dilimi eklemesine güvenilmez.
            OfferDate = AsUtcDate(request.OfferDate),
            ValidUntil = request.ValidUntil.HasValue
                ? AsUtcDate(request.ValidUntil.Value)
                : null,
            Currency = request.Currency.Trim().ToUpperInvariant(),
            ExchangeRate = request.ExchangeRate,
            Description = request.Description?.Trim(),
            Notes = request.Notes?.Trim(),
            Status = OfferStatus.Draft
        };

        var lineNumber = 1;

        foreach (var item in request.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Description) ||
                string.IsNullOrWhiteSpace(item.Unit))
            {
                return BadRequest(new
                {
                    message = "Her teklif kaleminde açıklama ve birim zorunludur."
                });
            }

            var validation = ValidateRates(
                item.Quantity,
                item.ListPrice,
                item.DiscountRate,
                item.FreightRate,
                item.WasteRate,
                item.FinanceRate,
                item.GeneralExpenseRate,
                item.ProfitRate);

            if (validation is not null)
                return BadRequest(new { message = validation });

            var calculation = Calculate(
                item.Quantity,
                item.ListPrice,
                item.DiscountRate,
                item.FreightRate,
                item.WasteRate,
                item.FinanceRate,
                item.GeneralExpenseRate,
                item.ProfitRate);

            entity.Items.Add(new OfferItem
            {
                LineNumber = lineNumber++,
                PositionNumber = item.PositionNumber?.Trim(),

                EngineeringPositionId = item.EngineeringPositionId,
                EngineeringRecipeId = item.EngineeringRecipeId,
                RecipeVersion = item.RecipeVersion,

                Description = item.Description.Trim(),
                ManufacturerPriceListItemId = item.ManufacturerPriceListItemId,
                ManufacturerName = item.ManufacturerName?.Trim(),
                ProductCode = item.ProductCode?.Trim(),
                Brand = item.Brand?.Trim(),
                Model = item.Model?.Trim(),
                Quantity = item.Quantity,
                Unit = item.Unit.Trim(),
                ListPrice = item.ListPrice,
                DiscountRate = item.DiscountRate,
                NetPurchasePrice = calculation.NetPurchasePrice,
                FreightRate = item.FreightRate,
                WasteRate = item.WasteRate,
                FinanceRate = item.FinanceRate,
                GeneralExpenseRate = item.GeneralExpenseRate,
                ProfitRate = item.ProfitRate,
                UnitCost = calculation.UnitCost,
                UnitSalesPrice = calculation.UnitSalesPrice,
                CostTotal = calculation.CostTotal,
                SalesTotal = calculation.SalesTotal,
                Notes = item.Notes?.Trim()
            });

            ApplyComponents(
                entity.Items.Last(),
                item.MaterialUnitPrice,
                item.LaborUnitPrice,
                item.OverheadUnitPrice);
        }

        entity.Subtotal = decimal.Round(
            entity.Items.Sum(x => x.ListPrice * x.Quantity), 2);

        entity.DiscountTotal = decimal.Round(
            entity.Items.Sum(x =>
                (x.ListPrice - x.NetPurchasePrice) * x.Quantity), 2);

        entity.CostTotal = decimal.Round(
            entity.Items.Sum(x => x.CostTotal), 2);

        entity.GrandTotal = decimal.Round(
            entity.Items.Sum(x => x.SalesTotal), 2);

        entity.ProfitTotal = decimal.Round(
            entity.GrandTotal - entity.CostTotal, 2);

        db.Offers.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Teklif taslak olarak oluşturuldu.",
            entity.Id,
            entity.OfferNumber,
            entity.GrandTotal,
            entity.Status
        });
    }

    /// <summary>
    /// Pozdan teklif kalemi üretir ve teklife ekler.
    ///
    /// İki kaynak var ve ikisi de gerçek veriden gelir:
    /// - RESMÎ YIL FİYATI: kurumun yayımladığı birim fiyat; malzeme ve
    ///   montaj ayrımı varsa birebir taşınır.
    /// - REÇETE ANALİZİ: pozun reçetesindeki malzeme ve işçilikten
    ///   maliyet çıkarılır, üstüne kâr eklenir.
    ///
    /// Fiyat bulunamazsa kalem EKLENMEZ: sıfır fiyatlı bir keşif satırı,
    /// olmayan bir satırdan daha tehlikelidir çünkü toplamı sessizce
    /// düşürür.
    /// </summary>
    [HttpPost("{id:guid}/items/from-position")]
    [RequirePermission(PermissionCatalog.Keys.EngineeringManage)]
    public async Task<IActionResult> AddItemFromPosition(
        Guid id,
        OfferItemFromPositionRequest request,
        [FromServices] Services.Engineering.IPositionPriceService positionPrices,
        [FromServices] Services.Costing.ICostEngine costEngine,
        CancellationToken cancellationToken)
    {
        if (request.Quantity <= 0)
            return BadRequest(new { message = "Metraj sıfırdan büyük olmalıdır." });

        var offer = await db.Offers
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (offer is null)
            return NotFound(new { message = "Teklif bulunamadı." });

        if (offer.Status != OfferStatus.Draft)
        {
            return BadRequest(new
            {
                message = "Yalnızca taslak teklife kalem eklenebilir."
            });
        }

        var position = await db.EngineeringPositions
            .AsNoTracking()
            .Where(x => x.Id == request.EngineeringPositionId)
            .Select(x => new { x.Id, x.Code, x.Name, x.Unit })
            .SingleOrDefaultAsync(cancellationToken);

        if (position is null)
            return NotFound(new { message = "Poz bulunamadı." });

        decimal unitSalesPrice;
        decimal material, labor, overhead;
        string sourceNote;

        if (request.Source == OfferPositionPriceSource.OfficialYearPrice)
        {
            var institution = request.Institution.HasValue
                ? (PositionPriceInstitution)request.Institution.Value
                : (PositionPriceInstitution?)null;

            var resolution = await positionPrices.ResolveAsync(
                position.Id, request.Year, institution, cancellationToken);

            if (!resolution.Found || resolution.UnitPrice is not > 0m)
            {
                return BadRequest(new
                {
                    message =
                        $"{position.Code} pozuna fiyat bulunamadı. {resolution.Explanation}"
                });
            }

            unitSalesPrice = resolution.UnitPrice.Value;

            // Kurum malzeme/montaj ayrımı yayımladıysa birebir taşınır;
            // yayımlamadıysa tutarın tamamı malzemeye yazılır.
            material = resolution.MaterialPrice ?? unitSalesPrice;
            labor = resolution.LaborPrice ?? 0m;
            overhead = decimal.Round(unitSalesPrice - material - labor, 6);

            if (overhead < 0m)
            {
                // Bileşen toplamı birim fiyatı aşıyorsa kurumun verisi
                // kendi içinde tutarsız; uydurma bir GG üretmek yerine
                // ayrımı bırakıp tamamını malzemeye yazıyoruz.
                material = unitSalesPrice;
                labor = 0m;
                overhead = 0m;
            }

            sourceNote = resolution.Explanation;
        }
        else
        {
            var estimate = await costEngine.EstimatePositionAsync(
                new Contracts.OfferCosting.EstimatePositionCostRequest(
                    offer.CompanyId,
                    position.Id,
                    offer.Currency,
                    request.LaborHourRate,
                    request.MachineHourRate),
                cancellationToken);

            if (estimate.UnitCost <= 0m)
            {
                return BadRequest(new
                {
                    message =
                        $"{position.Code} pozunun reçete analizi sıfır maliyet " +
                        "üretti; malzeme fiyatları eksik olabilir."
                });
            }

            var profit = request.ProfitRate is >= 0m and <= 100m
                ? request.ProfitRate
                : 0m;

            unitSalesPrice = decimal.Round(
                estimate.UnitCost * (1 + profit / 100m), 6);

            // Analizde malzeme ve işçilik zaten ayrı çıkıyor; kâr her
            // ikisine oranlı dağıtılır ki bileşen toplamı satış
            // fiyatına eşit kalsın.
            var costMaterial = estimate.MaterialCost;
            var costLabor = estimate.LaborCost + estimate.MachineCost;
            var costTotal = costMaterial + costLabor;

            if (costTotal > 0m)
            {
                material = decimal.Round(
                    unitSalesPrice * (costMaterial / costTotal), 6);
                labor = decimal.Round(unitSalesPrice - material, 6);
            }
            else
            {
                material = unitSalesPrice;
                labor = 0m;
            }

            overhead = 0m;

            sourceNote =
                $"Reçete analizi (v{estimate.RecipeVersion}); " +
                $"{estimate.PricedMaterialCount} malzeme fiyatlandı, " +
                $"{estimate.UnpricedMaterialCount} fiyatsız.";
        }

        var nextLine = offer.Items.Count == 0
            ? 1
            : offer.Items.Max(x => x.LineNumber) + 1;

        var newItem = new OfferItem
        {
            OfferId = offer.Id,
            LineNumber = nextLine,
            PositionNumber = position.Code,
            EngineeringPositionId = position.Id,
            Description = position.Name,
            Quantity = request.Quantity,
            Unit = string.IsNullOrWhiteSpace(position.Unit) ? "ad" : position.Unit,
            // Pozdan gelen kalemde liste fiyatı/iskonto kavramı yok:
            // birim fiyat doğrudan belirlenmiştir.
            ListPrice = unitSalesPrice,
            NetPurchasePrice = unitSalesPrice,
            UnitCost = unitSalesPrice,
            UnitSalesPrice = unitSalesPrice,
            MaterialUnitPrice = material,
            LaborUnitPrice = labor,
            OverheadUnitPrice = overhead,
            CostTotal = decimal.Round(unitSalesPrice * request.Quantity, 2),
            SalesTotal = decimal.Round(unitSalesPrice * request.Quantity, 2),
            Notes = sourceNote
        };

        offer.Items.Add(newItem);

        // BaseEntity yapıcıda Id atadığı için EF, izlenen üst kaydın
        // koleksiyonundan gelen yeni çocuğu "mevcut" sanabiliyor.
        db.Entry(newItem).State = EntityState.Added;

        RecalculateOfferTotals(offer);

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Kalem pozdan eklendi.",
            newItem.Id,
            newItem.LineNumber,
            newItem.UnitSalesPrice,
            newItem.MaterialUnitPrice,
            newItem.LaborUnitPrice,
            newItem.OverheadUnitPrice,
            newItem.SalesTotal,
            sourceNote
        });
    }

    /// <summary>
    /// Teklifi projenin keşif icmaline aktarır.
    /// </summary>
    /// <summary>
    /// Teklifin takip künyesi: kime verildi (işveren / ana yüklenici)
    /// ve hangi tipte.
    ///
    /// Teklif HAZIRLAMA yetkisinden ayrı bir anahtarla korunuyor;
    /// Finans teklif kalemi giremeden huniyi yönetebilsin diye.
    /// </summary>
    [HttpPut("{id:guid}/takip")]
    [RequirePermission(PermissionCatalog.Keys.OfferTrackingManage)]
    public async Task<IActionResult> UpdateTracking(
        Guid id,
        UpdateOfferTrackingRequest request,
        CancellationToken cancellationToken)
    {
        var offer = await db.Offers
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (offer is null)
            return NotFound(new { message = "Teklif bulunamadı." });

        if (OfferStatusTransitions.IsFinal(offer.Status))
        {
            return BadRequest(new
            {
                message = $"{OfferStatusTransitions.Label(offer.Status)} " +
                          "durumundaki teklifin künyesi değiştirilemez."
            });
        }

        if (!Enum.IsDefined(typeof(OfferCounterpartyRole), request.CounterpartyRole))
            return BadRequest(new { message = "Geçersiz karşı taraf rolü." });

        if (!Enum.IsDefined(typeof(OfferKind), request.Kind))
            return BadRequest(new { message = "Geçersiz teklif tipi." });

        if (request.CounterpartyCurrentAccountId is Guid accountId)
        {
            var belongs = await db.CurrentAccounts.AnyAsync(
                x => x.Id == accountId && x.CompanyId == offer.CompanyId,
                cancellationToken);

            if (!belongs)
            {
                return BadRequest(new
                {
                    message = "Seçilen cari teklifin şirketine ait değil."
                });
            }

            // Rolü olmayan bir cariye teklif verildiğini kaydetmek,
            // huniyi "kime verdiğimiz belirsiz" satırlarla doldurur.
            if (request.CounterpartyRole == OfferCounterpartyRole.Unspecified)
            {
                return BadRequest(new
                {
                    message = "Cari seçildiğinde işveren mi ana yüklenici mi " +
                              "olduğu da belirtilmelidir."
                });
            }
        }
        else if (request.CounterpartyRole != OfferCounterpartyRole.Unspecified)
        {
            return BadRequest(new
            {
                message = "Karşı taraf rolü için cari seçilmelidir."
            });
        }

        offer.CounterpartyCurrentAccountId = request.CounterpartyCurrentAccountId;
        offer.CounterpartyRole = request.CounterpartyRole;
        offer.Kind = request.Kind;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Teklif takip künyesi güncellendi." });
    }

    /// <summary>
    /// Teklif durumunu değiştirir (fırsat hunisi).
    ///
    /// Geçerli geçişler tek yerde tanımlı
    /// (<see cref="OfferStatusTransitions"/>) ve burada zorlanıyor;
    /// serbest durum ataması hunide "verilmeden kazanılmış" gibi
    /// imkânsız satırlar üretirdi.
    /// </summary>
    [HttpPost("{id:guid}/durum")]
    [RequirePermission(PermissionCatalog.Keys.OfferTrackingManage)]
    public async Task<IActionResult> ChangeStatus(
        Guid id,
        ChangeOfferStatusRequest request,
        CancellationToken cancellationToken)
    {
        var offer = await db.Offers
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (offer is null)
            return NotFound(new { message = "Teklif bulunamadı." });

        if (!Enum.IsDefined(typeof(OfferLostReason), request.LostReason))
            return BadRequest(new { message = "Geçersiz kayıp nedeni." });

        var problem = OfferStatusTransitions.Validate(
            offer.Status,
            request.Status,
            offer.CounterpartyCurrentAccountId.HasValue,
            request.LostReason,
            offer.Items.Count);

        if (problem is not null)
            return BadRequest(new { message = problem });

        var previous = offer.Status;

        offer.Status = request.Status;
        offer.LostReason = request.LostReason;
        offer.LostReasonNote = request.Status == OfferStatus.Lost
            ? request.LostReasonNote?.Trim()
            : null;
        offer.StatusNote = request.Note?.Trim();
        offer.StatusChangedAtUtc = DateTime.UtcNow;

        var raw = User.FindFirst("sub")?.Value
            ?? User.FindFirst(
                System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        offer.StatusChangedByUserId =
            Guid.TryParse(raw, out var actorId) ? actorId : null;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = $"Teklif {OfferStatusTransitions.Label(previous)} " +
                      $"durumundan {OfferStatusTransitions.Label(request.Status)} " +
                      "durumuna alındı.",
            status = (int)offer.Status,
            statusName = OfferStatusTransitions.Label(offer.Status),
            // Kazanıldı işaretlemek tek başına proje açmaz; sözleşme
            // künyesi ayrı adımda girilir.
            requiresContract = offer.Status == OfferStatus.Won
        });
    }

    /// <summary>
    /// Kazanılan teklifin sözleşmesini açar: yeni proje kurar ya da
    /// mevcut projeye ek iş olarak bağlar, ardından icmali teklif
    /// kalemlerinden üretir.
    ///
    /// Proje AÇMA yetkisi de aranır ve bilinçli olarak KOD İÇİNDE
    /// kontrol edilir: birden fazla [RequirePermission] birleştiği
    /// zaman "herhangi biri yeterli" anlamına geliyor, yani ikinci
    /// attribute yetkiyi daraltmak yerine genişletirdi. Yeni proje
    /// açılmayan ek iş bağlamasında bu koşul aranmaz.
    /// </summary>
    [HttpPost("{id:guid}/sozlesme")]
    [RequirePermission(PermissionCatalog.Keys.OfferTrackingManage)]
    public async Task<IActionResult> CreateContract(
        Guid id,
        CreateOfferContractRequest request,
        [FromServices] OfferContractService contracts,
        [FromServices] ICurrentUserService currentUser,
        [FromServices] IUserAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        if (request.ProjectId is null &&
            !await HasPermissionAsync(
                currentUser, authorization,
                PermissionCatalog.Keys.ProjectsCreate, cancellationToken))
        {
            return StatusCode(403, new
            {
                message = "Yeni proje açmak için proje oluşturma yetkisi gerekir.",
                requiredPermission = PermissionCatalog.Keys.ProjectsCreate
            });
        }

        if (request.ContractType is ProjectContractType type &&
            !Enum.IsDefined(typeof(ProjectContractType), type))
        {
            return BadRequest(new { message = "Geçersiz sözleşme tipi." });
        }

        if (!Enum.IsDefined(
                typeof(ProjectProgressPaymentPeriod),
                request.ProgressPaymentPeriod))
        {
            return BadRequest(new { message = "Geçersiz hakediş periyodu." });
        }

        if (request.ContractAmount is decimal amount && amount < 0m)
            return BadRequest(new { message = "Sözleşme bedeli negatif olamaz." });

        if (request.PlannedStartDate is DateTime start &&
            request.PlannedEndDate is DateTime end &&
            end < start)
        {
            return BadRequest(new
            {
                message = "Termin, işe başlama tarihinden önce olamaz."
            });
        }

        try
        {
            var result = await contracts.CreateAsync(
                id,
                new OfferContractInput(
                    request.ProjectId,
                    request.BranchId,
                    request.Code,
                    request.Name,
                    request.ContractNumber,
                    request.ContractDate,
                    request.ContractAmount,
                    request.ContractType,
                    request.PlannedStartDate,
                    request.PlannedEndDate,
                    request.CashRetentionRate,
                    request.VatRate,
                    request.WithholdingTaxRate,
                    request.MaterialDeductionRate,
                    request.ProgressPaymentPeriod,
                    request.PaymentTerms,
                    request.City,
                    request.District,
                    request.Address,
                    request.TransferToBoq,
                    request.BoqName),
                cancellationToken);

            return Ok(new
            {
                message = result.ProjectCreated
                    ? "Sözleşme künyesi kaydedildi, proje ve icmal oluşturuldu."
                    : "Teklif mevcut projeye ek iş olarak bağlandı.",
                result.ProjectId,
                result.ProjectCode,
                result.ProjectCreated,
                result.WarehouseId,
                result.ProjectBoqId,
                result.BoqNumber,
                result.BoqItemCount,
                result.BoqTotalAmount,
                result.Warnings
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Teklifin iş zinciri: teklif → proje → icmal → hakediş.
    ///
    /// "Bu proje hangi tekliften geldi" ve "verdiğimiz teklif ne oldu"
    /// soruları bugüne kadar cevapsızdı: ProjectBoq.SourceOfferId
    /// yazılıyordu ama hiçbir yerde okunmuyordu. Zincir, bir kalemin
    /// fiyatı tartışıldığında hangi teklife dayandığını göstermek için
    /// gerekli.
    /// </summary>
    [HttpGet("{id:guid}/zincir")]
    [RequirePermission(PermissionCatalog.Keys.OfferTrackingView)]
    public async Task<IActionResult> GetChain(
        Guid id,
        CancellationToken cancellationToken)
    {
        var offer = await db.Offers
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.OfferNumber,
                x.Title,
                x.OfferDate,
                x.Currency,
                x.GrandTotal,
                x.Status,
                x.Kind,
                x.LostReason,
                x.CounterpartyCurrentAccountId,
                CounterpartyName = x.CounterpartyCurrentAccount != null
                    ? x.CounterpartyCurrentAccount.Title
                    : null,
                x.CounterpartyRole,
                x.ProjectId
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (offer is null)
            return NotFound(new { message = "Teklif bulunamadı." });

        object? project = null;
        object[] boqs = [];
        object[] payments = [];

        if (offer.ProjectId is Guid projectId)
        {
            project = await db.Projects
                .AsNoTracking()
                .Where(x => x.Id == projectId)
                .Select(x => new
                {
                    x.Id,
                    x.Code,
                    x.Name,
                    x.ContractNumber,
                    x.ContractDate,
                    x.ContractAmount,
                    x.CurrencyCode,
                    ContractType = (int)x.ContractType,
                    ProgressPaymentPeriod = (int)x.ProgressPaymentPeriod,
                    x.PaymentTerms,
                    Status = (int)x.Status,
                    x.IsArchived,
                    // Bu proje bu tekliften mi doğdu, yoksa teklif
                    // sonradan ek iş olarak mı bağlandı?
                    BornFromThisOffer = x.SourceOfferId == id
                })
                .SingleOrDefaultAsync(cancellationToken);

            boqs = await db.ProjectBoqs
                .AsNoTracking()
                .Where(x => x.ProjectId == projectId)
                .OrderBy(x => x.CreatedAtUtc)
                .Select(x => new
                {
                    x.Id,
                    x.BoqNumber,
                    x.Name,
                    Status = (int)x.Status,
                    x.TotalAmount,
                    x.IsCurrentRevision,
                    x.SourceOfferId,
                    // Bu icmal bu teklifin kalemlerinden mi üretildi?
                    FromThisOffer = x.SourceOfferId == id,
                    ItemCount = x.Items.Count
                })
                .ToArrayAsync(cancellationToken);

            payments = await db.ProgressPayments
                .AsNoTracking()
                .Where(x => x.ProjectId == projectId)
                .OrderBy(x => x.PeriodNumber)
                .Select(x => new
                {
                    x.Id,
                    x.ProgressPaymentNumber,
                    x.PeriodNumber,
                    x.ProgressPaymentDate,
                    Status = (int)x.Status,
                    x.CurrentAmount,
                    x.CumulativeAmount,
                    x.CurrencyCode
                })
                .ToArrayAsync(cancellationToken);
        }

        return Ok(new
        {
            offer = new
            {
                offer.Id,
                offer.OfferNumber,
                offer.Title,
                offer.OfferDate,
                offer.Currency,
                offer.GrandTotal,
                Status = (int)offer.Status,
                StatusName = OfferStatusTransitions.Label(offer.Status),
                Kind = (int)offer.Kind,
                KindName = OfferStatusTransitions.KindLabel(offer.Kind),
                LostReason = (int)offer.LostReason,
                LostReasonName =
                    OfferStatusTransitions.LostReasonLabel(offer.LostReason),
                offer.CounterpartyCurrentAccountId,
                offer.CounterpartyName,
                CounterpartyRole = (int)offer.CounterpartyRole,
                CounterpartyRoleName =
                    OfferStatusTransitions.RoleLabel(offer.CounterpartyRole)
            },
            project,
            boqs,
            progressPayments = payments
        });
    }

    /// <summary>
    /// Kazanma oranı özeti — adet ve tutar bazında.
    ///
    /// Oranın paydası kazanılan + kaybedilendir; sonucu belli olmamış
    /// teklif henüz kaybedilmediği için oranı yapay olarak düşürürdü.
    /// </summary>
    [HttpGet("kazanma-orani")]
    [RequirePermission(PermissionCatalog.Keys.OfferTrackingView)]
    public async Task<IActionResult> GetWinRate(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? counterpartyId,
        [FromQuery] int? kind,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        CancellationToken cancellationToken)
    {
        var query = db.Offers.AsNoTracking();

        if (companyId is Guid cid) query = query.Where(x => x.CompanyId == cid);

        if (counterpartyId is Guid partyId)
            query = query.Where(x => x.CounterpartyCurrentAccountId == partyId);

        if (kind is int k)
        {
            if (!Enum.IsDefined(typeof(OfferKind), k))
                return BadRequest(new { message = "Geçersiz teklif tipi." });

            query = query.Where(x => x.Kind == (OfferKind)k);
        }

        if (fromDate is DateTime from)
            query = query.Where(x => x.OfferDate >= AsUtcDate(from));

        if (toDate is DateTime to)
            query = query.Where(x => x.OfferDate <= AsUtcDate(to));

        var rows = await query
            .Select(x => new { x.Status, x.GrandTotal })
            .ToListAsync(cancellationToken);

        var summary = OfferWinRateCalculator.Calculate(
            rows.Select(x => (x.Status, x.GrandTotal)));

        // Kayıp nedenlerinin dağılımı: "fiyatımız mı yüksek yoksa
        // referansımız mı yetmiyor" sorusunun cevabı.
        var lostBreakdown = await query
            .Where(x => x.Status == OfferStatus.Lost)
            .GroupBy(x => x.LostReason)
            .Select(g => new
            {
                Reason = g.Key,
                Count = g.Count(),
                Amount = g.Sum(x => x.GrandTotal)
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            summary.TotalCount,
            summary.WonCount,
            summary.LostCount,
            summary.OpenCount,
            summary.CancelledCount,
            summary.WonAmount,
            summary.LostAmount,
            summary.OpenAmount,
            summary.CountWinRate,
            summary.AmountWinRate,
            lostReasons = lostBreakdown
                .OrderByDescending(x => x.Count)
                .Select(x => new
                {
                    reason = (int)x.Reason,
                    reasonName = OfferStatusTransitions.LostReasonLabel(x.Reason),
                    x.Count,
                    Amount = decimal.Round(x.Amount, 2)
                })
        });
    }

    /// <summary>
    /// Oturumdaki kullanıcının belirli bir izne sahip olup olmadığı.
    /// Attribute'lar VEYA mantığıyla birleştiği için ikinci bir izin
    /// koşulu ancak burada zorlanabiliyor.
    /// </summary>
    private static async Task<bool> HasPermissionAsync(
        ICurrentUserService currentUser,
        IUserAuthorizationService authorization,
        string permissionKey,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
            return false;

        var snapshot = await authorization.GetAsync(userId, cancellationToken);

        if (snapshot is null || !snapshot.IsActive)
            return false;

        // Admin rolü yetki katmanının tamamını atlıyor (middleware ile
        // aynı davranış); burada da aynı istisna geçerli olmalı.
        if (snapshot.RoleNames.Contains("Admin", StringComparer.OrdinalIgnoreCase))
            return true;

        return snapshot.Permissions.Contains(
            permissionKey, StringComparer.OrdinalIgnoreCase);
    }

    [HttpPost("{id:guid}/icmale-aktar")]
    [RequirePermission(PermissionCatalog.Keys.EngineeringManage)]
    public async Task<IActionResult> TransferToBoq(
        Guid id,
        TransferOfferToBoqRequest? request,
        [FromServices] Services.Offers.OfferBoqTransferService transfer,
        CancellationToken cancellationToken)
    {
        try
        {
            var raw = User.FindFirst("sub")?.Value
                ?? User.FindFirst(
                    System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            var result = await transfer.TransferAsync(
                id,
                request?.ProjectId,
                request?.Name,
                Guid.TryParse(raw, out var actorId) ? actorId : null,
                cancellationToken);

            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Antetli çıktı için teklif ve şirket bilgisi.
    ///
    /// Yazdırma ekranı bu uçtan beslenir; şirket bilgisi ayrı bir
    /// istekle çekilseydi antet ile içerik farklı anlarda gelir ve
    /// yazdırma sırasında yarım görünürdü.
    /// </summary>
    [HttpGet("{id:guid}/print")]
    [RequirePermission(PermissionCatalog.Keys.EngineeringView)]
    public async Task<IActionResult> GetPrintData(
        Guid id,
        CancellationToken cancellationToken)
    {
        var offer = await db.Offers
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.OfferNumber,
                x.Title,
                x.OfferDate,
                x.ValidUntil,
                x.Currency,
                x.Status,
                x.Description,
                x.Notes,
                x.Subtotal,
                x.DiscountTotal,
                x.GrandTotal,
                Company = new
                {
                    x.Company.Name,
                    x.Company.TaxOffice,
                    x.Company.TaxNumber,
                    x.Company.Address,
                    x.Company.Phone,
                    x.Company.Email
                },
                ProjectCode = x.Project != null ? x.Project.Code : null,
                ProjectName = x.Project != null ? x.Project.Name : null,
                Items = x.Items
                    .OrderBy(item => item.LineNumber)
                    .Select(item => new
                    {
                        item.LineNumber,
                        item.PositionNumber,
                        item.Description,
                        item.Unit,
                        item.Quantity,
                        item.UnitSalesPrice,
                        item.MaterialUnitPrice,
                        item.LaborUnitPrice,
                        item.OverheadUnitPrice,
                        item.SalesTotal
                    })
                    .ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);

        return offer is null
            ? NotFound(new { message = "Teklif bulunamadı." })
            : Ok(offer);
    }

    /// <summary>
    /// Kalemin malzeme/montaj/GG dağılımını uygular.
    ///
    /// Bileşen verilmemişse tutarın TAMAMI malzemeye yazılır; toplam
    /// korunur. Verilmişse bileşen toplamı satış fiyatı olur — iki
    /// rakamın çelişmesindense bileşenlerin esas alınması, keşfin
    /// hakedişe bire bir taşınabilmesi için gerekli.
    /// </summary>
    private static void ApplyComponents(
        OfferItem item,
        decimal? material,
        decimal? labor,
        decimal? overhead)
    {
        var hasComponents =
            material is > 0m || labor is > 0m || overhead is > 0m;

        if (!hasComponents)
        {
            item.MaterialUnitPrice = item.UnitSalesPrice;
            item.LaborUnitPrice = 0m;
            item.OverheadUnitPrice = 0m;
            return;
        }

        item.MaterialUnitPrice = material ?? 0m;
        item.LaborUnitPrice = labor ?? 0m;
        item.OverheadUnitPrice = overhead ?? 0m;

        item.UnitSalesPrice = decimal.Round(
            item.MaterialUnitPrice + item.LaborUnitPrice + item.OverheadUnitPrice, 6);

        item.SalesTotal = decimal.Round(item.UnitSalesPrice * item.Quantity, 2);
    }

    /// <summary>Teklif toplamlarını kalemlerden yeniden hesaplar.</summary>
    private static void RecalculateOfferTotals(Offer offer)
    {
        offer.Subtotal = decimal.Round(
            offer.Items.Sum(x => x.ListPrice * x.Quantity), 2);

        offer.DiscountTotal = decimal.Round(
            offer.Items.Sum(x => (x.ListPrice - x.NetPurchasePrice) * x.Quantity), 2);

        offer.CostTotal = decimal.Round(offer.Items.Sum(x => x.CostTotal), 2);
        offer.GrandTotal = decimal.Round(offer.Items.Sum(x => x.SalesTotal), 2);
        offer.ProfitTotal = decimal.Round(offer.GrandTotal - offer.CostTotal, 2);
    }

    /// <summary>
    /// Tarihi gün başlangıcına indirip UTC olarak damgalar.
    /// </summary>
    private static DateTime AsUtcDate(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);

    private static string? ValidateRates(
        decimal quantity,
        decimal listPrice,
        params decimal[] rates)
    {
        if (quantity <= 0)
            return "Miktar sıfırdan büyük olmalıdır.";

        if (listPrice < 0)
            return "Liste fiyatı negatif olamaz.";

        if (rates.Any(x => x < 0 || x > 100))
            return "İskonto ve maliyet oranları 0 ile 100 arasında olmalıdır.";

        return null;
    }

    private static CalculationResult Calculate(
        decimal quantity,
        decimal listPrice,
        decimal discountRate,
        decimal freightRate,
        decimal wasteRate,
        decimal financeRate,
        decimal generalExpenseRate,
        decimal profitRate)
    {
        var netPurchasePrice =
            listPrice * (1 - discountRate / 100m);

        var unitCost = netPurchasePrice *
            (1 +
             freightRate / 100m +
             wasteRate / 100m +
             financeRate / 100m +
             generalExpenseRate / 100m);

        var unitSalesPrice =
            unitCost * (1 + profitRate / 100m);

        var costTotal = unitCost * quantity;
        var salesTotal = unitSalesPrice * quantity;

        return new CalculationResult(
            decimal.Round(netPurchasePrice, 6),
            decimal.Round(unitCost, 6),
            decimal.Round(unitSalesPrice, 6),
            decimal.Round(costTotal, 2),
            decimal.Round(salesTotal, 2),
            decimal.Round(salesTotal - costTotal, 2));
    }

    private sealed record CalculationResult(
        decimal NetPurchasePrice,
        decimal UnitCost,
        decimal UnitSalesPrice,
        decimal CostTotal,
        decimal SalesTotal,
        decimal ProfitTotal);
}
