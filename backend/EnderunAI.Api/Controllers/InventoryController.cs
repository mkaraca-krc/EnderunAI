using EnderunAI.Api.Contracts.Inventory;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Services.DocumentNumbers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/inventory")]
public sealed class InventoryController(
    AppDbContext db,
    IDocumentNumberService documentNumbers,
    ICurrentUserService currentUser,
    Services.Inventory.IStockAccountingConsistencyService consistency) : ControllerBase
{
    /// <summary>
    /// STOK ↔ MUHASEBE TUTARLILIK RAPORU.
    ///
    /// Depodaki değer (miktar × ağırlıklı ortalama) ile 150/153
    /// hesaplarının mizan bakiyesini karşılaştırır. Fark varsa bir
    /// yerde stok muhasebeye yazılmadan hareket etmiştir.
    ///
    /// 379.01 bakiyesi ayrı gösterilir: o tutarsızlık değil, "malı
    /// aldık faturası gelmedi" demektir.
    /// </summary>
    [HttpGet("accounting-consistency")]
    [RequirePermission(PermissionCatalog.Keys.AccountingView)]
    public async Task<IActionResult> GetAccountingConsistency(
        [FromQuery] Guid? companyId, CancellationToken cancellationToken)
    {
        var resolvedCompanyId = companyId;

        if (resolvedCompanyId is null)
        {
            var companies = await db.Companies
                .Where(x => x.IsActive)
                .Select(x => x.Id)
                .Take(2)
                .ToListAsync(cancellationToken);

            if (companies.Count == 0)
                return BadRequest(new { message = "Aktif şirket bulunamadı." });

            // Birden fazla şirket varsa hangisinin sorulduğu
            // TAHMİN EDİLMEZ: yanlış şirketin mizanı "tutarsızlık"
            // gibi görünür ve boş yere alarm verir.
            if (companies.Count > 1)
                return BadRequest(new { message = "Şirket seçilmelidir." });

            resolvedCompanyId = companies[0];
        }

        return Ok(await consistency.BuildAsync(resolvedCompanyId.Value, cancellationToken));
    }

    [HttpGet("items")]
    [RequirePermission(PermissionCatalog.Keys.InventoryView)]
    public async Task<IActionResult> GetItems(
        [FromQuery] Guid? companyId,
        [FromQuery] string? search,
        [FromQuery] string? category,
        [FromQuery] Guid? warehouseId,
        [FromQuery] bool? criticalOnly,
        [FromQuery] bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = db.InventoryItems.AsNoTracking();

        /*
         * ARŞİVLENMİŞ KART VARSAYILAN OLARAK GELMEZ.
         *
         * `IsActive` bayrağı vardı ama yalnızca `GoodsReceiptService`
         * ona uyuyordu: liste/seçici, perakende ürün arama ve alış
         * faturası doğrulaması yok sayıyordu. Yani kartı arşivlemek
         * HİÇBİR ŞEY İFADE ETMİYORDU — kart yeni belgelerde çıkmaya
         * devam ediyordu.
         *
         * Bu uç hem seçici hem yönetim ekranı tarafından kullanılıyor;
         * yönetim ekranı arşivi görüp geri açabilmeli, o yüzden
         * `includeInactive` AÇIKÇA istenir. Varsayılan kapalı:
         * unutulan bir çağrı arşivi sızdırmasın.
         */
        if (!includeInactive) query = query.Where(x => x.IsActive);

        if (companyId.HasValue) query = query.Where(x => x.CompanyId == companyId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x => x.Code.ToLower().Contains(term) || x.Name.ToLower().Contains(term) ||
                (x.Brand != null && x.Brand.ToLower().Contains(term)) ||
                (x.Model != null && x.Model.ToLower().Contains(term)) ||
                (x.Barcode != null && x.Barcode.ToLower().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(x => x.Category == category);

        // Depo süzgeci: o depoda stok SATIRI olan kalemler. Miktarı sıfır
        // olan satır da gelir — "bu depoda tutulan malzeme" sorusunun
        // cevabı, o an kaç tane olduğundan bağımsızdır.
        if (warehouseId.HasValue)
        {
            query = query.Where(x =>
                x.WarehouseStocks.Any(s => s.WarehouseId == warehouseId.Value));
        }

        var items = await query.OrderBy(x => x.Name).Select(x => new
        {
            x.Id, x.CompanyId, CompanyName = x.Company.Name, x.Code, x.Name, x.Category,
            // KONUM: etiket çıktısı ve raf araması bunları kullanıyor.
            // Açık bölgede raf/kat null kalır — olmayan ayrıntı.
            x.InventoryCategoryId,
            CategoryLabel = x.InventoryCategory != null ? x.InventoryCategory.Name : x.Category,
            ZoneName = x.WarehouseZone != null ? x.WarehouseZone.Name : null,
            ShelfCode = x.WarehouseShelf != null ? x.WarehouseShelf.Code : null,
            LevelCode = x.WarehouseShelfLevel != null ? x.WarehouseShelfLevel.Code : null,
            x.Brand, x.Model, x.Unit, x.Barcode, x.MinimumStock, x.MaximumStock,
            x.AverageUnitCost,
            x.LastPurchasePrice, x.LastPurchaseDate, x.VatRate,
            x.PreferredSupplierCurrentAccountId,
            PreferredSupplierTitle = x.PreferredSupplierCurrentAccount != null
                ? x.PreferredSupplierCurrentAccount.Title
                : null,
            x.Type, x.IsActive,
            TotalStock = x.WarehouseStocks.Sum(s => s.Quantity),
            // Stok değeri ağırlıklı ortalama maliyetten hesaplanır; son
            // alış fiyatı kullanılsaydı eski stok bugünkü fiyatla
            // değerlenir ve bilanço şişerdi.
            StockValue = x.WarehouseStocks.Sum(s => s.Quantity) * x.AverageUnitCost
        }).ToListAsync(cancellationToken);

        if (criticalOnly == true)
        {
            items = items
                .Where(x => x.MinimumStock > 0m && x.TotalStock <= x.MinimumStock)
                .ToList();
        }

        return Ok(items);
    }

    /// <summary>Kategori süzgecinin seçenekleri; serbest metin alandan türetilir.</summary>
    /*
     * ESKİ `GET categories` UCU KALDIRILDI (S1).
     *
     * Serbest metin `InventoryItem.Category` alanından DISTINCT
     * çekiyordu. O alan artık kategori DEĞİL: canlıda bir kartın
     * değeri "TURAN" (tedarikçi adı) yazıyordu ve dört kartta boştu.
     *
     * Kategori artık kendi varlığı — özellik şablonu, izin verilen
     * birimler ve tip taşıyor. Tek kaynak:
     * `InventoryCategoriesController` (aynı rota: api/inventory/categories).
     */

    [HttpGet("items/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.InventoryView)]
    public async Task<IActionResult> GetItem(Guid id, CancellationToken cancellationToken)
    {
        var item = await db.InventoryItems
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new InventoryItemDetail(
                x.Id,
                x.CompanyId,
                x.Company.Name,
                x.Code,
                x.Name,
                x.Category,
                x.Brand,
                x.Model,
                x.Unit,
                x.Barcode,
                x.MinimumStock,
                x.MaximumStock,
                (int)x.Type,
                x.IsActive,
                x.AverageUnitCost,
                x.LastPurchasePrice,
                x.LastPurchaseDate,
                x.PreferredSupplierCurrentAccountId,
                x.PreferredSupplierCurrentAccount != null
                    ? x.PreferredSupplierCurrentAccount.Title
                    : null,
                x.VatRate,
                x.Description,
                x.CopperKgPerUnit,
                x.ImagePath,
                x.WarehouseStocks.Sum(s => s.Quantity),
                x.WarehouseStocks.Sum(s => s.Quantity) * x.AverageUnitCost,
                x.WarehouseStocks.Select(s => new InventoryItemWarehouseStock(
                    s.WarehouseId,
                    s.Warehouse.Code,
                    s.Warehouse.Name,
                    s.Quantity)).ToList()))
            .SingleOrDefaultAsync(cancellationToken);

        return item is null
            ? NotFound(new { message = "Malzeme kartı bulunamadı." })
            : Ok(item);
    }

    public sealed record CreateItemRequest(
        Guid CompanyId,
        Guid CategoryId,
        string Unit,
        Guid[]? OptionIds,
        /*
         * KONUM. Depo verilirse o depodaki kategori varsayılanı
         * uygulanır; kullanıcı elle değiştirmek isterse bölge/raf/kat
         * doğrudan geçilir ve varsayılanın önüne geçer.
         */
        Guid? WarehouseId,
        Guid? ZoneId,
        Guid? ShelfId,
        Guid? LevelId,
        /* SERBEST tipte zorunlu, STANDART tipte YOK SAYILIR. */
        string? Name,
        string? Brand,
        string? Model,
        string? Barcode,
        decimal MinimumStock,
        decimal? MaximumStock,
        decimal? CopperKgPerUnit,
        int Type,
        Guid? PreferredSupplierCurrentAccountId,
        decimal? VatRate,
        string? Description);

    /// <summary>
    /// STOK KARTI AÇMA — kategori güdümlü (S2).
    ///
    /// Kullanıcı KOD ve AD YAZMAZ:
    ///   • KOD tam otomatik sıra (100001…), şirket başına, anlamsız.
    ///     Kod bir kimliktir; ürünü tanımlayan ad ve özelliklerdir.
    ///   • AD, STANDART kategoride özelliklerden üretilir. Elle yazılan
    ///     ad aynı malzemeyi üç farklı isimle açtırır ve stoğu böler.
    ///
    /// BİRİM kategorinin izin verdiği listeden seçilir ve karta
    /// sabitlenir; hareket girişi bunu kullanır, seçim sunmaz.
    ///
    /// MÜKERRER ENGELİ veritabanı seviyesinde: aynı kategori+özellik
    /// kombinasyonu şirket içinde ikinci kez açılamaz.
    /// </summary>
    [HttpPost("items")]
    [RequirePermission(PermissionCatalog.Keys.InventoryCreate)]
    public async Task<IActionResult> CreateItem(
        CreateItemRequest request,
        [FromServices] Services.Inventory.IInventoryCodeService codes,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(typeof(InventoryItemType), request.Type))
            return BadRequest(new { message = "Geçersiz malzeme tipi." });

        if (request.VatRate is < 0m or > 100m)
            return BadRequest(new { message = "KDV oranı 0-100 arasında olmalıdır." });

        var companyExists = await db.Companies
            .AnyAsync(x => x.Id == request.CompanyId && x.IsActive, cancellationToken);

        if (!companyExists)
            return BadRequest(new { message = "Geçerli bir şirket seçilmelidir." });

        var category = await db.InventoryCategories
            .Include(x => x.AllowedUnits)
            .Include(x => x.Attributes).ThenInclude(x => x.Options)
            .SingleOrDefaultAsync(x => x.Id == request.CategoryId, cancellationToken);

        if (category is null)
            return BadRequest(new { message = "Kategori seçilmelidir." });

        if (!category.IsActive)
            return BadRequest(new { message = "Arşivlenmiş kategoriye kart açılamaz." });

        // BİRİM KİLİDİ: kategorinin izin verdiği listeden olmalı.
        var unit = request.Unit?.Trim();

        var allowedUnits = category.AllowedUnits
            .Where(x => x.IsActive)
            .Select(x => x.Unit)
            .ToList();

        if (string.IsNullOrWhiteSpace(unit) ||
            !allowedUnits.Contains(unit, StringComparer.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message = $"Birim '{category.Name}' kategorisinin izin verdiği "
                    + $"birimlerden biri olmalı: {string.Join(", ", allowedUnits)}."
            });
        }

        string name;
        string? signature = null;
        var selectedOptions = new List<(InventoryAttribute Attribute, InventoryAttributeOption Option)>();

        if (category.Kind == InventoryCategoryKind.Standard)
        {
            var optionIds = request.OptionIds ?? [];

            var required = category.Attributes.Where(x => x.IsActive && x.IsRequired).ToList();

            foreach (var attribute in category.Attributes.Where(x => x.IsActive))
            {
                var match = attribute.Options
                    .Where(x => x.IsActive)
                    .SingleOrDefault(x => optionIds.Contains(x.Id));

                if (match is not null) selectedOptions.Add((attribute, match));
            }

            var missing = required
                .Where(attribute => selectedOptions.All(x => x.Attribute.Id != attribute.Id))
                .Select(x => x.Name)
                .ToList();

            if (missing.Count > 0)
                return BadRequest(new
                {
                    message = "Şu özellikler seçilmeli: " + string.Join(", ", missing)
                });

            var selection = selectedOptions
                .Select(x => new Services.Inventory.InventoryItemComposer.SelectedAttribute(
                    x.Attribute.Code,
                    x.Attribute.SortOrder,
                    x.Option.Value,
                    x.Option.Display ?? x.Option.Value))
                .ToList();

            name = Services.Inventory.InventoryItemComposer.BuildName(category.Name, selection);
            signature = Services.Inventory.InventoryItemComposer.BuildSignature(category.Code, selection);

            // MÜKERRER: dostça mesaj için ÖNCE bakılıyor; asıl garanti
            // veritabanı indeksi (yarış durumunda o yakalar).
            var duplicate = await db.InventoryItems
                .Where(x => x.CompanyId == request.CompanyId && x.AttributeSignature == signature)
                .Select(x => new { x.Code, x.Name, x.IsActive })
                .FirstOrDefaultAsync(cancellationToken);

            if (duplicate is not null)
            {
                return Conflict(new
                {
                    message = duplicate.IsActive
                        ? $"Bu malzeme zaten var: {duplicate.Name} ({duplicate.Code})"
                        : $"Bu malzeme ARŞİVDE var: {duplicate.Name} ({duplicate.Code}). "
                            + "Yeni kart açmak yerine arşivden geri açın."
                });
            }
        }
        else
        {
            // SERBEST tip: ad elle yazılır, mükerrer engeli uygulanmaz.
            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest(new
                {
                    message = $"'{category.Name}' serbest bir kategori; malzeme adı yazılmalıdır."
                });

            name = request.Name.Trim();
        }

        /*
         * KONUM ÇÖZÜMÜ.
         *
         * Öncelik ELLE SEÇİMDE: kullanıcı bölge geçtiyse o kullanılır.
         * Geçmediyse ve depo verildiyse, o depodaki kategori
         * varsayılanı uygulanır — "kart açılınca konum otomatik gelir".
         *
         * Hiçbiri yoksa konum boş kalır: depo bölgeleri henüz
         * tanımlanmamış olabilir ve bu kart açmayı engellememeli.
         */
        Guid? zoneId = request.ZoneId;
        Guid? shelfId = request.ShelfId;
        Guid? levelId = request.LevelId;

        if (zoneId is null && request.WarehouseId is Guid warehouseId)
        {
            var defaultLocation = await db.WarehouseCategoryLocations
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.WarehouseId == warehouseId &&
                         x.InventoryCategoryId == category.Id,
                    cancellationToken);

            if (defaultLocation is not null)
            {
                zoneId = defaultLocation.WarehouseZoneId;
                shelfId = defaultLocation.WarehouseShelfId;
                levelId = defaultLocation.WarehouseShelfLevelId;
            }
        }

        if (zoneId is Guid selectedZoneId)
        {
            var zone = await db.WarehouseZones
                .Include(x => x.Shelves).ThenInclude(x => x.Levels)
                .SingleOrDefaultAsync(x => x.Id == selectedZoneId, cancellationToken);

            if (zone is null)
                return BadRequest(new { message = "Bölge bulunamadı." });

            // BÖLGE TİPİ BELİRLEYİCİ — açık bölgede raf/kat olmaz.
            if (zone.Kind == WarehouseZoneKind.Open)
            {
                shelfId = null;
                levelId = null;
            }
            else if (shelfId is Guid selectedShelfId)
            {
                var shelf = zone.Shelves.SingleOrDefault(x => x.Id == selectedShelfId);

                if (shelf is null)
                    return BadRequest(new { message = "Raf bu bölgeye ait değil." });

                if (levelId is Guid selectedLevelId &&
                    shelf.Levels.All(x => x.Id != selectedLevelId))
                    return BadRequest(new { message = "Kat bu rafa ait değil." });
            }
        }

        var entity = new InventoryItem
        {
            CompanyId = request.CompanyId,
            InventoryCategoryId = category.Id,
            WarehouseZoneId = zoneId,
            WarehouseShelfId = shelfId,
            WarehouseShelfLevelId = levelId,
            Code = await codes.NextCodeAsync(request.CompanyId, cancellationToken),
            Name = name,
            AttributeSignature = signature,
            Brand = request.Brand?.Trim(),
            Model = request.Model?.Trim(),
            Unit = unit,
            Barcode = request.Barcode?.Trim(),
            MinimumStock = request.MinimumStock,
            MaximumStock = request.MaximumStock,
            CopperKgPerUnit = request.CopperKgPerUnit,
            Type = (InventoryItemType)request.Type,
            PreferredSupplierCurrentAccountId = request.PreferredSupplierCurrentAccountId,
            VatRate = request.VatRate,
            Description = request.Description?.Trim()
        };

        foreach (var (attribute, option) in selectedOptions)
        {
            entity.AttributeValues.Add(new InventoryItemAttributeValue
            {
                InventoryAttributeId = attribute.Id,
                InventoryAttributeOptionId = option.Id
            });
        }

        db.InventoryItems.Add(entity);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (signature is not null)
        {
            // YARIŞ: iki kullanıcı aynı anda aynı malzemeyi açtı.
            // Kısmi tekil indeks ikincisini reddetti — çökme yerine
            // aynı dostça mesaj.
            return Conflict(new
            {
                message = "Bu malzeme az önce başka bir kullanıcı tarafından açıldı."
            });
        }

        return Ok(new
        {
            message = "Malzeme kartı oluşturuldu.",
            entity.Id,
            entity.Code,
            entity.Name
        });
    }

    [HttpPut("items/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.InventoryEdit)]
    public async Task<IActionResult> UpdateItem(Guid id, UpdateInventoryItemRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Unit))
            return BadRequest(new { message = "Malzeme adı ve birimi zorunludur." });

        if (!Enum.IsDefined(typeof(InventoryItemType), request.Type))
            return BadRequest(new { message = "Geçersiz malzeme tipi." });

        var item = await db.InventoryItems.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null) return NotFound(new { message = "Malzeme kartı bulunamadı." });

        item.Name = request.Name.Trim();
        item.Category = request.Category?.Trim();
        item.Brand = request.Brand?.Trim();
        item.Model = request.Model?.Trim();
        item.Unit = request.Unit.Trim();
        item.Barcode = request.Barcode?.Trim();
        item.MinimumStock = request.MinimumStock;
        item.CopperKgPerUnit = request.CopperKgPerUnit;
        item.MaximumStock = request.MaximumStock;
        item.Type = (InventoryItemType)request.Type;
        item.IsActive = request.IsActive;
        item.PreferredSupplierCurrentAccountId = request.PreferredSupplierCurrentAccountId;
        item.VatRate = request.VatRate;
        item.Description = request.Description?.Trim();

        if (item.VatRate is < 0m or > 100m)
            return BadRequest(new { message = "KDV oranı 0-100 arasında olmalıdır." });

        item.UpdatedAtUtc = DateTime.UtcNow;
        item.UpdatedByUserId = currentUser.UserId;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = "Malzeme kartı güncellendi." });
    }

    [HttpGet("critical-stock-alerts")]
    [RequirePermission(PermissionCatalog.Keys.InventoryView)]
    public async Task<IActionResult> GetCriticalStockAlerts([FromQuery] Guid? companyId, CancellationToken cancellationToken)
    {
        var query = db.WarehouseStocks.AsNoTracking()
            .Where(x => x.InventoryItem.MinimumStock > 0 &&
                        x.Quantity <= x.InventoryItem.MinimumStock);

        if (companyId.HasValue)
            query = query.Where(x => x.InventoryItem.CompanyId == companyId.Value);

        var alerts = await query
            .OrderBy(x => x.InventoryItem.Name)
            .Select(x => new
            {
                x.WarehouseId,
                WarehouseName = x.Warehouse.Name,
                x.InventoryItemId,
                ItemCode = x.InventoryItem.Code,
                ItemName = x.InventoryItem.Name,
                x.InventoryItem.Unit,
                x.Quantity,
                x.InventoryItem.MinimumStock
            })
            .ToListAsync(cancellationToken);

        return Ok(alerts);
    }

    [HttpGet("warehouses/{warehouseId:guid}/stocks")]
    [RequirePermission(PermissionCatalog.Keys.InventoryView)]
    public async Task<IActionResult> GetWarehouseStocks(Guid warehouseId, CancellationToken cancellationToken)
    {
        if (!await db.Warehouses.AsNoTracking().AnyAsync(x => x.Id == warehouseId, cancellationToken))
            return NotFound(new { message = "Depo bulunamadı." });

        var stocks = await db.WarehouseStocks.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId)
            .OrderBy(x => x.InventoryItem.Name)
            .Select(x => new
            {
                x.InventoryItemId, x.InventoryItem.Code, x.InventoryItem.Name,
                x.InventoryItem.Category, x.InventoryItem.Brand, x.InventoryItem.Model,
                x.InventoryItem.Unit, x.Quantity,
                x.InventoryItem.MinimumStock,
                x.InventoryItem.AverageUnitCost,
                IsCritical = x.Quantity <= x.InventoryItem.MinimumStock
            }).ToListAsync(cancellationToken);

        return Ok(stocks);
    }

    [HttpGet("movements")]
    [RequirePermission(PermissionCatalog.Keys.InventoryView)]
    public async Task<IActionResult> GetMovements([FromQuery] Guid? warehouseId, [FromQuery] Guid? projectId,
        [FromQuery] Guid? projectSiteId, [FromQuery] Guid? inventoryItemId, CancellationToken cancellationToken)
    {
        var query = db.StockMovements.AsNoTracking();
        if (warehouseId.HasValue) query = query.Where(x => x.WarehouseId == warehouseId.Value);
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId.Value);
        if (projectSiteId.HasValue) query = query.Where(x => x.ProjectSiteId == projectSiteId.Value);
        if (inventoryItemId.HasValue) query = query.Where(x => x.InventoryItemId == inventoryItemId.Value);

        var movements = await query.OrderByDescending(x => x.MovementDate).ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id, x.WarehouseId, WarehouseName = x.Warehouse.Name,
                x.InventoryItemId, ItemCode = x.InventoryItem.Code, ItemName = x.InventoryItem.Name,
                x.ProjectId, ProjectName = x.Project != null ? x.Project.Name : null,
                x.ProjectSiteId, ProjectSiteName = x.ProjectSite != null ? x.ProjectSite.Name : null,
                x.RelatedWarehouseId,
                RelatedWarehouseName = x.RelatedWarehouse != null ? x.RelatedWarehouse.Name : null,
                x.PurchaseRequestId, x.GoodsReceiptId,
                x.Type, x.Quantity, x.UnitCost, x.TotalCost,
                x.ReferenceNumber, x.MovementDate, x.Description
            }).ToListAsync(cancellationToken);

        return Ok(movements);
    }

    /*
     * SERBEST ELLE GİRİŞ UCU KALDIRILDI (S4 — tek giriş kapısı).
     *
     * `POST inventory/receipts` siparişe ya da mal kabule bağlı
     * DEĞİLDİ: `inventory.create` izni olan biri yalnız bir referans
     * numarası yazarak stok yaratabiliyordu.
     *
     * Daha kötüsü MALİYET YAZMIYORDU — `UnitCost` ve `TotalCost` boş
     * kalıyor, ağırlıklı ortalama güncellenmiyordu. Yani SIFIR
     * MALİYETLİ stok giriyor ve o andan sonra stok değeri ile muhasebe
     * birbirini tutmuyordu.
     *
     * GİRİŞ ARTIK YALNIZ ÜÇ KAPIDAN:
     *   1. Mal kabul (siparişe bağlı, maliyet ve ağırlıklı ortalama
     *      `GoodsReceiptService` içinde),
     *   2. İade dönüşü (alış faturası iadesi),
     *   3. Sayım düzeltme (yetkili + GEREKÇELİ).
     *
     * Canlıda bu uçtan gelmiş hareket yoktu (stok hareketi sayısı 0),
     * yani kaldırmak veri kaybetmedi.
     */

    [HttpPost("issues")]
    [RequirePermission(PermissionCatalog.Keys.InventoryCreate)]
    public async Task<IActionResult> Issue(StockIssueRequest request, CancellationToken cancellationToken)
    {
        if (request.Quantity <= 0) return BadRequest(new { message = "Miktar sıfırdan büyük olmalıdır." });

        var stock = await db.WarehouseStocks.Include(x => x.Warehouse).Include(x => x.InventoryItem).SingleOrDefaultAsync(
            x => x.WarehouseId == request.WarehouseId && x.InventoryItemId == request.InventoryItemId, cancellationToken);

        if (stock is null) return NotFound(new { message = "Depoda bu malzeme bulunmuyor." });
        if (stock.Quantity < request.Quantity)
            return Conflict(new { message = "Stok yetersiz." });

        if (request.ProjectSiteId.HasValue && !request.ProjectId.HasValue)
            return BadRequest(new { message = "Şantiye seçildiyse proje de belirtilmelidir." });

        // Kısım seçildiyse projeye ait olmalı: başka projenin kısmına
        // yazılan sarf, iki projenin de maliyet analizini bozar.
        Guid? sectionId = null;

        if (request.ProjectHakedisSectionId is Guid requestedSectionId)
        {
            if (!request.ProjectId.HasValue)
                return BadRequest(new { message = "Kısım seçildiyse proje de belirtilmelidir." });

            var sectionBelongsToProject = await db.ProjectHakedisSections
                .AnyAsync(
                    x => x.Id == requestedSectionId && x.ProjectId == request.ProjectId.Value,
                    cancellationToken);

            if (!sectionBelongsToProject)
                return BadRequest(new { message = "Seçilen kısım bu projeye ait değil." });

            sectionId = requestedSectionId;
        }

        // Taşeron seçildiyse sözleşmesi aynı projeye ait olmalı: başka
        // projenin taşeronuna yazılan sarf, o taşeronun hakedişinden
        // haksız kesinti önerir.
        Guid? subcontractorContractId = null;

        if (request.SubcontractorContractId is Guid requestedContractId)
        {
            if (!request.ProjectId.HasValue)
            {
                return BadRequest(new
                {
                    message = "Taşeron seçildiyse proje de belirtilmelidir."
                });
            }

            var contractBelongsToProject = await db.SubcontractorContracts
                .AnyAsync(
                    x => x.Id == requestedContractId &&
                         x.ProjectId == request.ProjectId.Value,
                    cancellationToken);

            if (!contractBelongsToProject)
            {
                return BadRequest(new
                {
                    message = "Seçilen taşeron sözleşmesi bu projeye ait değil."
                });
            }

            subcontractorContractId = requestedContractId;
        }

        // İcmal satırı seçildiyse aynı projeye ait olmalı; başka
        // projenin pozuna yazılan sarf iki projenin de kâr hesabını
        // bozar.
        Guid? boqItemId = null;

        if (request.ProjectBoqItemId is Guid requestedBoqItemId)
        {
            if (!request.ProjectId.HasValue)
            {
                return BadRequest(new
                {
                    message = "İcmal satırı seçildiyse proje de belirtilmelidir."
                });
            }

            var boqItemBelongsToProject = await db.ProjectBoqItems
                .AnyAsync(
                    x => x.Id == requestedBoqItemId
                         && x.ProjectBoq.ProjectId == request.ProjectId.Value,
                    cancellationToken);

            if (!boqItemBelongsToProject)
            {
                return BadRequest(new
                {
                    message = "Seçilen icmal satırı bu projeye ait değil."
                });
            }

            boqItemId = requestedBoqItemId;
        }

        // DocumentNumberService kendi transaction'ını açıp kapattığı için,
        // aynı bağlantı üzerinde iç içe transaction hatası almamak adına
        // belge numarası dış transaction başlamadan ÖNCE üretilir.
        var referenceNumber = await documentNumbers.GenerateAsync(
            stock.Warehouse.CompanyId, "STOCK_ISSUE", "CIKIS", cancellationToken);

        await using var dbTransaction = await db.Database.BeginTransactionAsync(cancellationToken);

        stock.Quantity -= request.Quantity;
        stock.UpdatedAtUtc = DateTime.UtcNow;

        // Stok düşerken maliyet, o anki AverageUnitCost'tan hesaplanıp hareket
        // kaydına dondurulur — ortalama sonradan değişse bile bu hareketin
        // maliyeti sabit kalır.
        var unitCost = stock.InventoryItem.AverageUnitCost;
        var totalCost = unitCost * request.Quantity;

        var description = request.Description?.Trim();
        if (!string.IsNullOrWhiteSpace(request.ReferenceNumber))
        {
            var note = $"Kullanıcı referansı: {request.ReferenceNumber.Trim()}";
            description = string.IsNullOrWhiteSpace(description) ? note : $"{description} ({note})";
        }

        var movement = new StockMovement
        {
            CompanyId = stock.Warehouse.CompanyId,
            WarehouseId = stock.WarehouseId,
            InventoryItemId = stock.InventoryItemId,
            ProjectId = request.ProjectId,
            ProjectSiteId = request.ProjectSiteId,
            ProjectHakedisSectionId = sectionId,
            SubcontractorContractId = subcontractorContractId,
            Type = StockMovementType.Issue,
            Quantity = request.Quantity,
            UnitCost = unitCost,
            TotalCost = totalCost,
            ReferenceNumber = referenceNumber,
            MovementDate = ToUtc(request.MovementDate),
            Description = description,
            CreatedByUserId = currentUser.UserId
        };
        db.StockMovements.Add(movement);

        // Proje belirtildiyse (şantiyeli veya şantiyesiz proje geneli) maliyet
        // otomatik işlenir; hiçbir proje seçilmediyse (genel/merkez sarfiyat)
        // hiçbir maliyet kaydı oluşturulmaz.
        if (request.ProjectId.HasValue && totalCost > 0)
        {
            db.ProjectCostTransactions.Add(new ProjectCostTransaction
            {
                ProjectId = request.ProjectId.Value,
                ProjectSiteId = request.ProjectSiteId,
                CostType = ProjectCostType.Material,
                CostClass = Services.Projects.ProjectCostClassifier.ForStockIssue(),
                ProjectHakedisSectionId = sectionId,
                ProjectBoqItemId = boqItemId,
                CostDate = ToUtc(request.MovementDate),
                Amount = totalCost,
                Description = $"Depo sarfı: {stock.InventoryItem.Name} ({request.Quantity} {stock.InventoryItem.Unit})",
                ReferenceType = "StockMovement",
                ReferenceId = movement.Id,
                CreatedByUserId = currentUser.UserId
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);
        return Ok(new { message = "Depo çıkışı kaydedildi.", stock.Quantity, referenceNumber, unitCost, totalCost });
    }

    [HttpPost("transfers")]
    [RequirePermission(PermissionCatalog.Keys.InventoryCreate)]
    public async Task<IActionResult> Transfer(StockTransferRequest request, CancellationToken cancellationToken)
    {
        if (request.SourceWarehouseId == request.TargetWarehouseId)
            return BadRequest(new { message = "Kaynak ve hedef depo aynı olamaz." });
        if (request.Quantity <= 0) return BadRequest(new { message = "Miktar sıfırdan büyük olmalıdır." });

        var source = await db.WarehouseStocks.Include(x => x.Warehouse).Include(x => x.InventoryItem).SingleOrDefaultAsync(
            x => x.WarehouseId == request.SourceWarehouseId && x.InventoryItemId == request.InventoryItemId, cancellationToken);
        if (source is null) return NotFound(new { message = "Kaynak depoda malzeme bulunamadı." });

        var targetWarehouse = await db.Warehouses.SingleOrDefaultAsync(x => x.Id == request.TargetWarehouseId, cancellationToken);
        if (targetWarehouse is null) return NotFound(new { message = "Hedef depo bulunamadı." });
        if (source.Warehouse.CompanyId != targetWarehouse.CompanyId)
            return BadRequest(new { message = "Depolar aynı şirkete ait olmalıdır." });
        if (source.Quantity < request.Quantity)
            return Conflict(new { message = "Kaynak depoda yeterli stok yok." });

        var referenceNumber = await documentNumbers.GenerateAsync(
            source.Warehouse.CompanyId, "STOCK_TRANSFER", "TRF", cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var target = await db.WarehouseStocks.SingleOrDefaultAsync(
            x => x.WarehouseId == request.TargetWarehouseId && x.InventoryItemId == request.InventoryItemId, cancellationToken);
        if (target is null)
        {
            target = new WarehouseStock { WarehouseId = request.TargetWarehouseId, InventoryItemId = request.InventoryItemId };
            db.WarehouseStocks.Add(target);
        }

        source.Quantity -= request.Quantity;
        target.Quantity += request.Quantity;
        source.UpdatedAtUtc = DateTime.UtcNow;
        target.UpdatedAtUtc = DateTime.UtcNow;

        var unitCost = source.InventoryItem.AverageUnitCost;
        var totalCost = unitCost * request.Quantity;

        var description = request.Description?.Trim();
        if (!string.IsNullOrWhiteSpace(request.ReferenceNumber))
        {
            var note = $"Kullanıcı referansı: {request.ReferenceNumber.Trim()}";
            description = string.IsNullOrWhiteSpace(description) ? note : $"{description} ({note})";
        }

        db.StockMovements.AddRange(
            new StockMovement
            {
                CompanyId = source.Warehouse.CompanyId,
                WarehouseId = source.WarehouseId,
                RelatedWarehouseId = targetWarehouse.Id,
                InventoryItemId = request.InventoryItemId,
                ProjectId = request.ProjectId,
                Type = StockMovementType.TransferOut,
                Quantity = request.Quantity,
                UnitCost = unitCost,
                TotalCost = totalCost,
                ReferenceNumber = referenceNumber,
                MovementDate = ToUtc(request.MovementDate),
                Description = description,
                CreatedByUserId = currentUser.UserId
            },
            new StockMovement
            {
                CompanyId = targetWarehouse.CompanyId,
                WarehouseId = targetWarehouse.Id,
                RelatedWarehouseId = source.WarehouseId,
                InventoryItemId = request.InventoryItemId,
                ProjectId = request.ProjectId,
                Type = StockMovementType.TransferIn,
                Quantity = request.Quantity,
                UnitCost = unitCost,
                TotalCost = totalCost,
                ReferenceNumber = referenceNumber,
                MovementDate = ToUtc(request.MovementDate),
                Description = description,
                CreatedByUserId = currentUser.UserId
            });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(new { message = "Depolar arası transfer tamamlandı.", referenceNumber });
    }

    [HttpPost("adjustments")]
    [RequirePermission(PermissionCatalog.Keys.InventoryEdit)]
    public async Task<IActionResult> Adjustment(StockAdjustmentRequest request, CancellationToken cancellationToken)
    {
        if (request.CountedQuantity < 0) return BadRequest(new { message = "Sayılan miktar negatif olamaz." });

        /*
         * GEREKÇE ZORUNLU (S4).
         *
         * Sayım düzeltme, tek giriş kapısı kuralının İSTİSNASI: belgeye
         * bağlı olmadan stok değiştirebilen tek yol. Bu yüzden ne
         * olduğu YAZILMAK ZORUNDA — fire mi, kayıp mı, hatalı giriş mi.
         *
         * Gerekçesiz düzeltme, kaldırdığımız serbest giriş ucunun aynı
         * kapısını arka taraftan açardı.
         */
        if (string.IsNullOrWhiteSpace(request.Description))
            return BadRequest(new
            {
                message = "Düzeltme gerekçesi zorunludur (fire, kayıp, hatalı giriş vb.)."
            });

        var stock = await db.WarehouseStocks.Include(x => x.Warehouse).Include(x => x.InventoryItem).SingleOrDefaultAsync(
            x => x.WarehouseId == request.WarehouseId && x.InventoryItemId == request.InventoryItemId, cancellationToken);
        if (stock is null) return NotFound(new { message = "Depoda bu malzeme bulunmuyor." });

        var delta = request.CountedQuantity - stock.Quantity;
        if (delta == 0) return BadRequest(new { message = "Sayılan miktar mevcut stokla aynı, düzeltme gerekmiyor." });

        var referenceNumber = await documentNumbers.GenerateAsync(
            stock.Warehouse.CompanyId, "STOCK_ADJUSTMENT", "SAYIM", cancellationToken);

        stock.Quantity = request.CountedQuantity;
        stock.UpdatedAtUtc = DateTime.UtcNow;

        var unitCost = stock.InventoryItem.AverageUnitCost;

        db.StockMovements.Add(new StockMovement
        {
            CompanyId = stock.Warehouse.CompanyId,
            WarehouseId = stock.WarehouseId,
            InventoryItemId = stock.InventoryItemId,
            ProjectId = request.ProjectId,
            Type = StockMovementType.Adjustment,
            Quantity = delta,
            UnitCost = unitCost,
            TotalCost = unitCost * delta,
            ReferenceNumber = referenceNumber,
            MovementDate = ToUtc(request.MovementDate),
            Description = request.Description?.Trim(),
            CreatedByUserId = currentUser.UserId
        });

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new
        {
            message = delta > 0 ? "Sayım fazlası kaydedildi." : "Sayım eksiği kaydedildi.",
            referenceNumber,
            delta,
            newQuantity = stock.Quantity
        });
    }

    private static DateTime ToUtc(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
