using EnderunAI.Api.Contracts.GoodsReceipts;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.GoodsReceipt;
using EnderunAI.Api.Models.PurchaseOrder;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Services.DocumentNumbers;
using EnderunAI.Api.Services.Procurement;
using Microsoft.EntityFrameworkCore;
using System.Data;
using GoodsReceiptEntity = EnderunAI.Api.Models.GoodsReceipt.GoodsReceipt;
using EnderunAI.Api.Contracts.Core;
using EnderunAI.Api.Search;

namespace EnderunAI.Api.Services.GoodsReceipts;

public sealed class GoodsReceiptService(
    AppDbContext db,
    IDocumentNumberService documentNumbers,
    ICurrentDataScopeService dataScope,
    ICurrentUserService currentUser,
    Services.Inventory.IGoodsReceiptAccountingPoster accountingPoster,
    Services.Inventory.IStockCountLockService countLock,
    Services.Inventory.IStokSatirKilidi stokKilidi)
    : IGoodsReceiptService
{
    public async Task<PagedResult<GoodsReceiptListItemResponse>> GetAllAsync(
        Guid? companyId,
        Guid? warehouseId,
        Guid? purchaseOrderId,
        int? status,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var scope = await GetScopeAsync(cancellationToken);
        var query = db.GoodsReceipts.AsNoTracking().ApplyScope(scope);

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        if (warehouseId.HasValue)
            query = query.Where(x => x.WarehouseId == warehouseId.Value);

        if (purchaseOrderId.HasValue)
            query = query.Where(x => x.PurchaseOrderId == purchaseOrderId.Value);

        if (status.HasValue)
        {
            if (!Enum.IsDefined(typeof(GoodsReceiptStatus), status.Value))
                throw new ProcurementValidationException("Geçersiz mal kabul durumu.");

            query = query.Where(x => x.Status == (GoodsReceiptStatus)status.Value);
        }

        /*
         * ARAMA SUNUCUDA VE KATLANMIŞ.
         *
         * Eskiden tüm liste indirilip ön yüzde süzülüyordu; kayıt
         * sayısı büyüdükçe hem taşınan veri hem tarayıcıdaki dizi
         * doğrusal büyür. Katlama `enderun_fold` ile veritabanında
         * yapılıyor — ekranla AYNI kural (bkz. lib/search/fold.ts).
         *
         * BİRLEŞTİRİLMİŞ ALANLAR DA KAPSANIYOR: tedarikçi unvanı, depo
         * adı ve teslim alan bu tabloda değil; tek tabloya üretilmiş
         * kolon eklemek onları dışarıda bırakırdı.
         */
        query = ApplySearch(query, search);

        /*
         * TOPLAM AYRI SORGULANIYOR: sayfayla birlikte alınsaydı LIMIT
         * toplamı da kırpardı ve "kaç kayıt var" cevabı kendi kendini
         * yanlışlardı. Ekran bu sayıyı "Toplam X kayıt" diye yazıyor.
         */
        var total = await query.CountAsync(cancellationToken);

        var take = Math.Clamp(pageSize, 1, 200);
        var currentPage = Math.Max(page, 1);

        var items = await query
            .OrderByDescending(x => x.ReceiptDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .Skip((currentPage - 1) * take)
            .Take(take)
            .Select(x => new GoodsReceiptListItemResponse(
                x.Id,
                x.CompanyId,
                x.PurchaseOrderId,
                x.PurchaseOrder.OrderNumber,
                x.WarehouseId,
                x.Warehouse.Code,
                x.Warehouse.Name,
                x.PurchaseOrder.SupplierCurrentAccountId,
                x.PurchaseOrder.SupplierCurrentAccount.Title,
                x.ReceiptNumber,
                x.ReceiptDate,
                (int)x.Status,
                x.DispatchNoteNumber,
                x.ReceivedByName,
                x.Items.Count,
                x.Items.Sum(i => i.DeliveredQuantity),
                x.Items.Sum(i => i.AcceptedQuantity),
                x.Items.Sum(i => i.RejectedQuantity),
                x.Items.Sum(i => i.DamagedQuantity)))
            .ToListAsync(cancellationToken);

        return PagedResult<GoodsReceiptListItemResponse>.FromPage(
            items, total, take, currentPage);
    }

    /// <summary>
    /// KATLANMIŞ ARAMA — liste ve özet AYNI süzgeci kullanır.
    ///
    /// İki yerde ayrı yazılsaydı özet kartları listeyle farklı bir
    /// kümeyi sayardı: kullanıcı 12 satır görürken kartta 47 yazardı
    /// ve hangisinin doğru olduğunu bilemezdi.
    ///
    /// Katlama veritabanında (`enderun_fold`) — ekranla aynı kural.
    /// BİRLEŞTİRİLMİŞ ALANLAR da kapsanıyor (tedarikçi unvanı, depo
    /// adı, teslim alan); tek tabloya üretilmiş kolon eklemek onları
    /// dışarıda bırakırdı.
    /// </summary>
    private static IQueryable<GoodsReceipt> ApplySearch(
        IQueryable<GoodsReceipt> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return query;

        var folded = TurkishSearch.Fold(search);

        return query.Where(x =>
            AppDbContext.Fold(x.ReceiptNumber).Contains(folded) ||
            AppDbContext.Fold(x.PurchaseOrder.OrderNumber).Contains(folded) ||
            AppDbContext.Fold(x.PurchaseOrder.SupplierCurrentAccount.Title).Contains(folded) ||
            AppDbContext.Fold(x.Warehouse.Code).Contains(folded) ||
            AppDbContext.Fold(x.Warehouse.Name).Contains(folded) ||
            (x.DispatchNoteNumber != null &&
             AppDbContext.Fold(x.DispatchNoteNumber).Contains(folded)) ||
            (x.ReceivedByName != null &&
             AppDbContext.Fold(x.ReceivedByName).Contains(folded)));
    }

    /// <summary>
    /// Özet kartları — SÜZGEÇLERE UYAN TÜM kayıtlardan sayılıyor.
    /// Sayfadan hesaplansaydı 10.000 kayıtlık listede "Toplam 50"
    /// yazardı.
    /// </summary>
    public async Task<GoodsReceiptSummaryResponse> GetSummaryAsync(
        Guid? companyId,
        Guid? warehouseId,
        Guid? purchaseOrderId,
        string? search,
        CancellationToken cancellationToken)
    {
        var scope = await GetScopeAsync(cancellationToken);
        var query = db.GoodsReceipts.AsNoTracking().ApplyScope(scope);

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        if (warehouseId.HasValue)
            query = query.Where(x => x.WarehouseId == warehouseId.Value);

        if (purchaseOrderId.HasValue)
            query = query.Where(x => x.PurchaseOrderId == purchaseOrderId.Value);

        query = ApplySearch(query, search);

        // Tek turda sayılıyor: dört ayrı COUNT dört tur demekti.
        var gruplar = await query
            .GroupBy(x => x.Status)
            .Select(g => new
            {
                Status = g.Key,
                Adet = g.Count(),
                Kabul = g.Sum(x => x.Items.Sum(i => i.AcceptedQuantity))
            })
            .ToListAsync(cancellationToken);

        int SayiFor(GoodsReceiptStatus durum) =>
            gruplar.FirstOrDefault(x => x.Status == durum)?.Adet ?? 0;

        return new GoodsReceiptSummaryResponse(
            gruplar.Sum(x => x.Adet),
            SayiFor(GoodsReceiptStatus.Draft),
            SayiFor(GoodsReceiptStatus.Posted),
            SayiFor(GoodsReceiptStatus.Cancelled),
            gruplar.Sum(x => x.Kabul));
    }

    public async Task<GoodsReceiptDetailResponse> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var scope = await GetScopeAsync(cancellationToken);
        var result = await db.GoodsReceipts
            .AsNoTracking()
            .ApplyScope(scope)
            .Where(x => x.Id == id)
            .Select(x => new GoodsReceiptDetailResponse(
                x.Id,
                x.CompanyId,
                x.PurchaseOrderId,
                x.PurchaseOrder.OrderNumber,
                x.PurchaseOrder.ProjectId,
                x.PurchaseOrder.Project.Code,
                x.PurchaseOrder.Project.Name,
                x.WarehouseId,
                x.Warehouse.Code,
                x.Warehouse.Name,
                x.PurchaseOrder.SupplierCurrentAccountId,
                x.PurchaseOrder.SupplierCurrentAccount.Code,
                x.PurchaseOrder.SupplierCurrentAccount.Title,
                x.ReceiptNumber,
                x.ReceiptDate,
                (int)x.Status,
                x.DispatchNoteNumber,
                x.DispatchNoteDate,
                x.InvoiceNumber,
                x.InvoiceDate,
                x.ReceivedByName,
                x.VehiclePlate,
                x.DriverName,
                x.Description,
                x.Notes,
                x.PostedAtUtc,
                x.CancelledAtUtc,
                x.CancellationReason,
                null,
                null,
                null,
                null,
                x.Items
                    .OrderBy(i => i.LineNumber)
                    .Select(i => new GoodsReceiptItemResponse(
                        i.Id,
                        i.PurchaseOrderItemId,
                        i.InventoryItemId,
                        i.InventoryItem != null ? i.InventoryItem.Code : null,
                        i.InventoryItem != null ? i.InventoryItem.Name : null,
                        i.LineNumber,
                        i.MaterialDescription,
                        i.Brand,
                        i.Model,
                        i.OrderedQuantity,
                        i.PreviouslyReceivedQuantity,
                        i.DeliveredQuantity,
                        i.AcceptedQuantity,
                        i.RejectedQuantity,
                        i.DamagedQuantity,
                        i.Unit,
                        i.LotNumber,
                        i.SerialNumber,
                        i.ProductionDate,
                        i.ExpiryDate,
                        i.WarrantyEndDate,
                        i.ShelfLocation,
                        i.Notes,
                        i.RejectionReason))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);

        return result ?? throw new ProcurementNotFoundException("Mal kabul kaydı bulunamadı.");
    }

    public async Task<CreateGoodsReceiptResponse> CreateFromPurchaseOrderAsync(
        Guid purchaseOrderId,
        CreateGoodsReceiptRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ReceivedByName))
            throw new ProcurementValidationException("Teslim alan kişi zorunludur.");

        var scope = await GetScopeAsync(cancellationToken);
        var purchaseOrder = await db.PurchaseOrders
            .ApplyScope(scope)
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == purchaseOrderId, cancellationToken);

        if (purchaseOrder is null)
            throw new ProcurementNotFoundException("Satın alma siparişi bulunamadı.");

        if (purchaseOrder.Status is not (
                PurchaseOrderStatus.Approved or
                PurchaseOrderStatus.PartiallyReceived))
        {
            throw new ProcurementValidationException("Mal kabul yalnız onaylı sipariş için oluşturulabilir.");
        }

        if (await db.GoodsReceipts.AnyAsync(
                x => x.PurchaseOrderId == purchaseOrderId &&
                     x.Status == GoodsReceiptStatus.Draft,
                cancellationToken))
        {
            throw new ProcurementValidationException("Bu sipariş için tamamlanmamış mal kabul kaydı var.");
        }

        var warehouse = await db.Warehouses
            .AsNoTracking()
            .Where(x =>
                x.Id == request.WarehouseId &&
                x.CompanyId == purchaseOrder.CompanyId &&
                x.IsActive &&
                (!x.ProjectId.HasValue || x.ProjectId == purchaseOrder.ProjectId))
            .Select(x => new { x.Id, x.Name })
            .SingleOrDefaultAsync(cancellationToken);

        if (warehouse is null)
            throw new ProcurementValidationException("Depo bulunamadı veya sipariş kapsamına uygun değil.");

        var remainingItems = purchaseOrder.Items
            .OrderBy(x => x.LineNumber)
            .Select(x => new
            {
                Item = x,
                Remaining = Math.Max(0m, x.Quantity - x.ReceivedQuantity)
            })
            .Where(x => x.Remaining > 0)
            .ToList();

        if (remainingItems.Count == 0)
            throw new ProcurementValidationException("Siparişin kabul edilecek kalan miktarı yok.");

        var receiptNumber = await documentNumbers.GenerateAsync(
            purchaseOrder.CompanyId,
            "GOODS_RECEIPT",
            "GR",
            cancellationToken);

        var entity = new GoodsReceiptEntity
        {
            CompanyId = purchaseOrder.CompanyId,
            PurchaseOrderId = purchaseOrder.Id,
            WarehouseId = warehouse.Id,
            ReceiptNumber = receiptNumber,
            ReceiptDate = request.ReceiptDate.AsUtc(),
            Status = GoodsReceiptStatus.Draft,
            DispatchNoteNumber = request.DispatchNoteNumber?.Trim(),
            DispatchNoteDate = request.DispatchNoteDate.AsUtc(),
            InvoiceNumber = request.InvoiceNumber?.Trim(),
            InvoiceDate = request.InvoiceDate.AsUtc(),
            ReceivedByName = request.ReceivedByName.Trim(),
            ReceivedByUserId = currentUser.UserId,
            VehiclePlate = request.VehiclePlate?.Trim(),
            DriverName = request.DriverName?.Trim(),
            Description = request.Description?.Trim(),
            Notes = request.Notes?.Trim(),
            Items = remainingItems.Select(x => new GoodsReceiptItem
            {
                PurchaseOrderItemId = x.Item.Id,
                // Sipariş kaleminde stok kartı seçiliyse mal kabul de
                // onunla açılır; kullanıcı her kalemi elle eşleştirmek
                // zorunda kalmaz. Seçili değilse eskisi gibi boş gelir ve
                // kabul ekranında seçilir.
                InventoryItemId = x.Item.InventoryItemId,
                LineNumber = x.Item.LineNumber,
                MaterialDescription = x.Item.MaterialDescription,
                Brand = x.Item.Brand,
                Model = x.Item.Model,
                OrderedQuantity = x.Item.Quantity,
                PreviouslyReceivedQuantity = x.Item.ReceivedQuantity,
                DeliveredQuantity = x.Remaining,
                AcceptedQuantity = x.Remaining,
                RejectedQuantity = 0m,
                DamagedQuantity = 0m,
                Unit = x.Item.Unit,
                Notes = x.Item.Notes
            }).ToList()
        };

        db.GoodsReceipts.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateGoodsReceiptResponse(
            entity.Id,
            entity.ReceiptNumber,
            entity.PurchaseOrderId,
            purchaseOrder.OrderNumber,
            entity.WarehouseId,
            warehouse.Name,
            entity.Items.Count,
            (int)entity.Status);
    }

    public async Task<IReadOnlyList<GoodsReceiptInventoryOptionResponse>> GetInventoryOptionsAsync(
        Guid id,
        string? search,
        CancellationToken cancellationToken)
    {
        var scope = await GetScopeAsync(cancellationToken);
        var companyId = await db.GoodsReceipts
            .AsNoTracking()
            .ApplyScope(scope)
            .Where(x => x.Id == id && x.Status == GoodsReceiptStatus.Draft)
            .Select(x => (Guid?)x.CompanyId)
            .SingleOrDefaultAsync(cancellationToken);

        if (!companyId.HasValue)
            throw new ProcurementNotFoundException("Düzenlenebilir mal kabul kaydı bulunamadı.");

        var query = db.InventoryItems
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId.Value && x.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(x =>
                EF.Functions.ILike(x.Code, pattern) ||
                EF.Functions.ILike(x.Name, pattern) ||
                (x.Brand != null && EF.Functions.ILike(x.Brand, pattern)) ||
                (x.Model != null && EF.Functions.ILike(x.Model, pattern)));
        }

        return await query
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Code)
            .Take(250)
            .Select(x => new GoodsReceiptInventoryOptionResponse(
                x.Id,
                x.Code,
                x.Name,
                x.Category,
                x.Brand,
                x.Model,
                x.Unit))
            .ToListAsync(cancellationToken);
    }

    public async Task<GoodsReceiptActionResponse> UpdateDraftAsync(
        Guid id,
        UpdateGoodsReceiptDraftRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0)
            throw new ProcurementValidationException("En az bir mal kabul kalemi zorunludur.");

        if (request.Items.Select(x => x.Id).Distinct().Count() != request.Items.Count)
            throw new ProcurementValidationException("Aynı mal kabul kalemi birden fazla gönderilemez.");

        var scope = await GetScopeAsync(cancellationToken);
        var receipt = await db.GoodsReceipts
            .ApplyScope(scope)
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (receipt is null)
            throw new ProcurementNotFoundException("Mal kabul kaydı bulunamadı.");

        if (receipt.Status != GoodsReceiptStatus.Draft)
            throw new ProcurementValidationException("Yalnız taslak mal kabul kaydı düzenlenebilir.");

        var requestedItems = request.Items.ToDictionary(x => x.Id);
        if (receipt.Items.Count != requestedItems.Count ||
            receipt.Items.Any(x => !requestedItems.ContainsKey(x.Id)))
        {
            throw new ProcurementValidationException("Mal kabul kalemleri kayıtla uyuşmuyor.");
        }

        var inventoryItemIds = request.Items
            .Where(x => x.InventoryItemId.HasValue)
            .Select(x => x.InventoryItemId!.Value)
            .Distinct()
            .ToArray();

        var inventoryItems = await db.InventoryItems
            .AsNoTracking()
            .Where(x =>
                inventoryItemIds.Contains(x.Id) &&
                x.CompanyId == receipt.CompanyId &&
                x.IsActive)
            .Select(x => new { x.Id, x.Unit })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        if (inventoryItems.Count != inventoryItemIds.Length)
            throw new ProcurementValidationException("Seçilen stok kartlarından biri bulunamadı veya şirket kapsamına uygun değil.");

        foreach (var item in receipt.Items)
        {
            var update = requestedItems[item.Id];
            ValidateDraftQuantities(item, update);

            if (update.InventoryItemId.HasValue &&
                !string.Equals(
                    inventoryItems[update.InventoryItemId.Value].Unit,
                    item.Unit,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ProcurementValidationException(
                    $"{item.LineNumber}. kalemin birimi seçilen stok kartıyla uyuşmuyor.");
            }

            item.InventoryItemId = update.InventoryItemId;
            item.DeliveredQuantity = update.DeliveredQuantity;
            item.AcceptedQuantity = update.AcceptedQuantity;
            item.RejectedQuantity = update.RejectedQuantity;
            item.DamagedQuantity = update.DamagedQuantity;
            item.RejectionReason =
                TrimOrNull(update.RejectionReason, 1000, "Red gerekçesi");
            item.LotNumber = TrimOrNull(update.LotNumber, 100, "Lot numarası");
            item.SerialNumber = TrimOrNull(update.SerialNumber, 250, "Seri numarası");
            item.ProductionDate = update.ProductionDate.AsUtc();
            item.ExpiryDate = update.ExpiryDate.AsUtc();
            item.WarrantyEndDate = update.WarrantyEndDate.AsUtc();
            item.ShelfLocation = TrimOrNull(update.ShelfLocation, 100, "Raf konumu");
            item.Notes = TrimOrNull(update.Notes, 1000, "Kalem notu");
            item.UpdatedAtUtc = DateTime.UtcNow;
            item.UpdatedByUserId = currentUser.UserId;
        }

        receipt.UpdatedAtUtc = DateTime.UtcNow;
        receipt.UpdatedByUserId = currentUser.UserId;
        await db.SaveChangesAsync(cancellationToken);

        return Action(receipt, 0, "Mal kabul taslağı güncellendi.");
    }

    public async Task<GoodsReceiptActionResponse> PostAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var scope = await GetScopeAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var receipt = await db.GoodsReceipts
            .ApplyScope(scope)
            .Include(x => x.Warehouse)
            .Include(x => x.Items)
            .Include(x => x.PurchaseOrder)
                .ThenInclude(x => x.Items)
            .Include(x => x.PurchaseOrder)
                .ThenInclude(x => x.Rfq)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (receipt is null)
            throw new ProcurementNotFoundException("Mal kabul kaydı bulunamadı.");

        if (receipt.Status != GoodsReceiptStatus.Draft)
            throw new ProcurementValidationException("Yalnız taslak mal kabul kaydı stoklara işlenebilir.");

        var purchaseOrder = receipt.PurchaseOrder;
        if (purchaseOrder.Status is not (
                PurchaseOrderStatus.Approved or
                PurchaseOrderStatus.PartiallyReceived))
        {
            throw new ProcurementValidationException("Sipariş mal kabul işlemine uygun durumda değil.");
        }

        if (!receipt.Warehouse.IsActive ||
            receipt.Warehouse.CompanyId != receipt.CompanyId ||
            (receipt.Warehouse.ProjectId.HasValue &&
             receipt.Warehouse.ProjectId != purchaseOrder.ProjectId))
        {
            throw new ProcurementValidationException("Depo artık sipariş kapsamına uygun değil.");
        }

        foreach (var item in receipt.Items)
        {
            ValidatePostingQuantities(item);

            // Reddedilen ya da hasarlı miktar varsa gerekçe ZORUNLU.
            // Gerekçesiz red tedarikçiyle mutabakatta savunulamaz ve
            // kalite geçmişini "sebebi bilinmeyen redler"le doldurur.
            if (item.RejectedQuantity + item.DamagedQuantity > 0m &&
                string.IsNullOrWhiteSpace(item.RejectionReason))
            {
                throw new ProcurementValidationException(
                    $"{item.LineNumber}. kalemde reddedilen/hasarlı miktar " +
                    "için gerekçe zorunludur.");
            }
        }

        var acceptedItems = receipt.Items
            .Where(x => x.AcceptedQuantity > 0m)
            .OrderBy(x => x.LineNumber)
            .ToArray();

        if (acceptedItems.Length == 0)
            throw new ProcurementValidationException("Stok kaydı için en az bir kalemde kabul edilen miktar olmalıdır.");

        if (acceptedItems.Any(x => !x.InventoryItemId.HasValue))
            throw new ProcurementValidationException("Kabul edilen her kaleme stok kartı bağlanmalıdır.");

        var inventoryItemIds = acceptedItems
            .Select(x => x.InventoryItemId!.Value)
            .Distinct()
            .ToArray();

        var inventoryItems = await db.InventoryItems
            .Where(x =>
                inventoryItemIds.Contains(x.Id) &&
                x.CompanyId == receipt.CompanyId &&
                x.IsActive)
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        if (inventoryItems.Count != inventoryItemIds.Length)
            throw new ProcurementValidationException("Bağlı stok kartlarından biri bulunamadı veya şirket kapsamına uygun değil.");

        // Ağırlıklı ortalama maliyet için mevcut toplam miktar (tüm depolarda,
        // bu mal kabulden ÖNCEKİ hâliyle) — tek para birimi TRY, döviz cinsi
        // sipariş kalemleri PurchaseOrder.ExchangeRate ile TRY'ye çevrilir.
        var priorTotalQuantities = await db.WarehouseStocks
            .Where(x => inventoryItemIds.Contains(x.InventoryItemId))
            .GroupBy(x => x.InventoryItemId)
            .Select(g => new { InventoryItemId = g.Key, Total = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.InventoryItemId, x => x.Total, cancellationToken);

        var purchaseOrderItems = purchaseOrder.Items.ToDictionary(x => x.Id);
        foreach (var item in acceptedItems)
        {
            if (!purchaseOrderItems.TryGetValue(item.PurchaseOrderItemId, out var orderItem))
                throw new ProcurementValidationException("Mal kabul kalemi siparişle uyuşmuyor.");

            if (!string.Equals(
                    inventoryItems[item.InventoryItemId!.Value].Unit,
                    item.Unit,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ProcurementValidationException(
                    $"{item.LineNumber}. kalemin birimi stok kartıyla uyuşmuyor.");
            }

            var remainingQuantity = orderItem.Quantity - orderItem.ReceivedQuantity;
            if (item.AcceptedQuantity > remainingQuantity)
            {
                throw new ProcurementValidationException(
                    $"{item.LineNumber}. kalemde kabul miktarı siparişin kalan miktarını aşıyor.");
            }
        }

        var warehouseStocks = await db.WarehouseStocks
            .Where(x =>
                x.WarehouseId == receipt.WarehouseId &&
                inventoryItemIds.Contains(x.InventoryItemId))
            .ToDictionaryAsync(x => x.InventoryItemId, cancellationToken);

        var now = DateTime.UtcNow;
        var costByInventoryItem = new Dictionary<Guid, decimal>();

        foreach (var item in acceptedItems)
        {
            var inventoryItemId = item.InventoryItemId!.Value;
            if (!warehouseStocks.TryGetValue(inventoryItemId, out var stock))
            {
                stock = new WarehouseStock
                {
                    WarehouseId = receipt.WarehouseId,
                    InventoryItemId = inventoryItemId,
                    CreatedByUserId = currentUser.UserId
                };
                warehouseStocks.Add(inventoryItemId, stock);
                db.WarehouseStocks.Add(stock);
            }

            // SAYIM KİLİDİ: sayılan bölgeye hareket girmez.
            await countLock.EnsureNotLockedAsync(
                receipt.WarehouseId, inventoryItemId, cancellationToken);

            // SATIR KİLİDİ: iki eşzamanlı mal kabul aynı miktarı okur
            // ve biri diğerinin girişini siler — mal kaybolur.
            await stokKilidi.KilitleAsync(
                receipt.WarehouseId, inventoryItemId, cancellationToken);

            stock.Quantity += item.AcceptedQuantity;
            stock.UpdatedAtUtc = now;
            stock.UpdatedByUserId = currentUser.UserId;

            purchaseOrderItems[item.PurchaseOrderItemId].ReceivedQuantity +=
                item.AcceptedQuantity;

            // Sipariş kalemi hangi para biriminde olursa olsun, stok maliyeti
            // tek tip olarak TRY tutulur: NetUnitPrice zaten siparişin kendi
            // para biriminde, ExchangeRate ile TRY'ye çevrilir (TRY siparişte
            // ExchangeRate=1 olduğundan formül tüm para birimlerinde tek tip çalışır).
            var orderItem = purchaseOrderItems[item.PurchaseOrderItemId];
            var unitCostTry = orderItem.NetUnitPrice * purchaseOrder.ExchangeRate;

            var inventoryItem = inventoryItems[inventoryItemId];
            var priorQuantity = priorTotalQuantities.GetValueOrDefault(inventoryItemId, 0m);

            // Formül ortak motorda: doğrudan alış faturası da aynı
            // hesabı kullanıyor, iki yerde iki formül olmasın.
            inventoryItem.AverageUnitCost =
                Services.Inventory.WeightedAverageCostCalculator.Next(
                    priorQuantity,
                    inventoryItem.AverageUnitCost,
                    item.AcceptedQuantity,
                    unitCostTry);

            // Son alış fiyatı ortalamadan ayrı tutulur: ortalama stok
            // değerlemesi için doğru, ama "bu malzemeyi en son kaça
            // aldık" sorusuna eski ucuz alışları da taşıdığı için yanlış
            // cevap verir. Satın almanın pazarlıkta baktığı rakam budur.
            inventoryItem.LastPurchasePrice = unitCostTry;
            inventoryItem.LastPurchaseDate = receipt.ReceiptDate;

            inventoryItem.UpdatedAtUtc = now;
            inventoryItem.UpdatedByUserId = currentUser.UserId;

            // Bu ürünün bu kabuldeki payı işlendiği için sonraki kalemlerin
            // ortalaması da (aynı üründen birden fazla kalem olması ihtimaline
            // karşı) güncel miktar üzerinden hesaplansın.
            priorTotalQuantities[inventoryItemId] = priorQuantity + item.AcceptedQuantity;

            // Muhasebe fişi için kalem maliyetleri biriktiriliyor:
            // aynı karttan birden fazla satır olabilir, hesap bazında
            // tek satır yazılsın diye kart kimliğinde toplanıyor.
            costByInventoryItem[inventoryItemId] =
                costByInventoryItem.GetValueOrDefault(inventoryItemId)
                + unitCostTry * item.AcceptedQuantity;

            db.StockMovements.Add(new StockMovement
            {
                CompanyId = receipt.CompanyId,
                WarehouseId = receipt.WarehouseId,
                InventoryItemId = inventoryItemId,
                ProjectId = purchaseOrder.ProjectId,
                PurchaseRequestId = purchaseOrder.Rfq.PurchaseRequestId,
                GoodsReceiptId = receipt.Id,
                Type = StockMovementType.Receipt,
                Quantity = item.AcceptedQuantity,
                UnitCost = unitCostTry,
                TotalCost = unitCostTry * item.AcceptedQuantity,
                ReferenceNumber = receipt.ReceiptNumber,
                MovementDate = receipt.ReceiptDate,
                Description = $"Mal kabul {receipt.ReceiptNumber} - {item.MaterialDescription}",
                CreatedByUserId = currentUser.UserId
            });
        }

        // Reddedilen ve hasarlı miktar için ALIŞ İADESİ belgesi
        // otomatik doğar.
        //
        // Elle açma adımı olsaydı unutulduğunda reddedilen mal
        // kayıtsız kalır, tedarikçiye neyin neden iade edildiği
        // belgelenemezdi. Belge TASLAK doğar; tedarikçiye gönderim
        // ayrı bir adımdır.
        //
        // Reddedilen miktar ReceivedQuantity'ye EKLENMEZ (yukarıda
        // yalnız AcceptedQuantity ekleniyor), yani sipariş o miktar
        // için AÇIK kalır ve tedarikçi eksiği yeniden gönderebilir.
        var returnLines = receipt.Items
            .Where(x => x.RejectedQuantity + x.DamagedQuantity > 0m)
            .OrderBy(x => x.LineNumber)
            .ToArray();

        if (returnLines.Length > 0)
        {
            var returnNumber = await documentNumbers.GenerateAsync(
                receipt.CompanyId,
                "PURCHASE_RETURN",
                "AI",
                cancellationToken);

            var purchaseReturn = new PurchaseReturn
            {
                CompanyId = receipt.CompanyId,
                GoodsReceiptId = receipt.Id,
                PurchaseOrderId = purchaseOrder.Id,
                SupplierCurrentAccountId = purchaseOrder.SupplierCurrentAccountId,
                ProjectId = purchaseOrder.ProjectId,
                ReturnNumber = returnNumber,
                ReturnDate = receipt.ReceiptDate,
                Status = PurchaseReturnStatus.Draft,
                CurrencyCode = purchaseOrder.Currency,
                ExchangeRate = purchaseOrder.ExchangeRate,
                Notes = $"{receipt.ReceiptNumber} mal kabulünden otomatik üretildi."
            };

            var returnLineNumber = 1;

            foreach (var item in returnLines)
            {
                var orderItem = purchaseOrderItems[item.PurchaseOrderItemId];

                // Red ve hasar AYRI satır: ikisi farklı gerekçedir ve
                // tedarikçi kalite analizinde ayrı sayılmalı.
                foreach (var (quantity, kind) in new[]
                         {
                             (item.RejectedQuantity, PurchaseReturnReasonKind.Rejected),
                             (item.DamagedQuantity, PurchaseReturnReasonKind.Damaged)
                         })
                {
                    if (quantity <= 0m) continue;

                    purchaseReturn.Items.Add(new PurchaseReturnItem
                    {
                        GoodsReceiptItemId = item.Id,
                        PurchaseOrderItemId = item.PurchaseOrderItemId,
                        LineNumber = returnLineNumber++,
                        MaterialDescription = item.MaterialDescription,
                        Unit = item.Unit,
                        Quantity = quantity,
                        UnitPrice = orderItem.NetUnitPrice,
                        LineTotal = decimal.Round(quantity * orderItem.NetUnitPrice, 2),
                        ReasonKind = kind,
                        Reason = item.RejectionReason ?? string.Empty
                    });
                }
            }

            purchaseReturn.TotalAmount = decimal.Round(
                purchaseReturn.Items.Sum(x => x.LineTotal), 2);

            db.PurchaseReturns.Add(purchaseReturn);
        }

        purchaseOrder.Status = purchaseOrder.Items.All(x =>
            x.ReceivedQuantity >= x.Quantity)
            ? PurchaseOrderStatus.Completed
            : PurchaseOrderStatus.PartiallyReceived;
        purchaseOrder.UpdatedAtUtc = now;
        purchaseOrder.UpdatedByUserId = currentUser.UserId;

        /*
         * STOK MUHASEBEYE BURADA GİRER.
         *
         * Fiş kesilmezse stok fiziken artar ama mali tabloda görünmez;
         * sayım ile mizan ilk günden ayrışır ve fark aylar sonra,
         * kimsenin sebebini hatırlamadığı bir tutarsızlık olarak
         * çıkar. Aynı transaction içinde: fiş kesilemezse stok da
         * işlenmez.
         */
        receipt.AccountingVoucherId = await accountingPoster.PostAsync(
            receipt, costByInventoryItem, cancellationToken);

        receipt.Status = GoodsReceiptStatus.Posted;
        receipt.PostedAtUtc = now;
        receipt.PostedByUserId = currentUser.UserId;
        receipt.UpdatedAtUtc = now;
        receipt.UpdatedByUserId = currentUser.UserId;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Action(
            receipt,
            acceptedItems.Length,
            purchaseOrder.Status == PurchaseOrderStatus.Completed
                ? "Mal kabul stoklara işlendi ve sipariş tamamlandı."
                : "Mal kabul stoklara işlendi; sipariş kısmi teslim durumuna geçti.");
    }

    public async Task<GoodsReceiptActionResponse> CancelAsync(
        Guid id,
        string reason,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ProcurementValidationException("İptal nedeni zorunludur.");

        var scope = await GetScopeAsync(cancellationToken);
        var receipt = await db.GoodsReceipts
            .ApplyScope(scope)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (receipt is null)
            throw new ProcurementNotFoundException("Mal kabul kaydı bulunamadı.");

        if (receipt.Status != GoodsReceiptStatus.Draft)
            throw new ProcurementValidationException("Yalnız taslak mal kabul kaydı iptal edilebilir.");

        receipt.Status = GoodsReceiptStatus.Cancelled;
        receipt.CancelledAtUtc = DateTime.UtcNow;
        receipt.CancelledByUserId = currentUser.UserId;
        receipt.CancellationReason = TrimOrNull(reason, 1000, "İptal nedeni");
        receipt.UpdatedAtUtc = DateTime.UtcNow;
        receipt.UpdatedByUserId = currentUser.UserId;
        await db.SaveChangesAsync(cancellationToken);

        return Action(receipt, 0, "Mal kabul taslağı iptal edildi.");
    }

    private static void ValidateDraftQuantities(
        GoodsReceiptItem item,
        UpdateGoodsReceiptItemRequest update)
    {
        ValidateQuantities(
            item.LineNumber,
            update.DeliveredQuantity,
            update.AcceptedQuantity,
            update.RejectedQuantity,
            update.DamagedQuantity);

        var remainingQuantity = item.OrderedQuantity - item.PreviouslyReceivedQuantity;
        if (update.DeliveredQuantity > remainingQuantity)
        {
            throw new ProcurementValidationException(
                $"{item.LineNumber}. kalemde teslim miktarı siparişin kalan miktarını aşıyor.");
        }
    }

    private static void ValidatePostingQuantities(GoodsReceiptItem item) =>
        ValidateQuantities(
            item.LineNumber,
            item.DeliveredQuantity,
            item.AcceptedQuantity,
            item.RejectedQuantity,
            item.DamagedQuantity);

    private static void ValidateQuantities(
        int lineNumber,
        decimal deliveredQuantity,
        decimal acceptedQuantity,
        decimal rejectedQuantity,
        decimal damagedQuantity)
    {
        if (deliveredQuantity < 0m ||
            acceptedQuantity < 0m ||
            rejectedQuantity < 0m ||
            damagedQuantity < 0m)
        {
            throw new ProcurementValidationException(
                $"{lineNumber}. kalemde miktarlar negatif olamaz.");
        }

        if (acceptedQuantity + rejectedQuantity + damagedQuantity != deliveredQuantity)
        {
            throw new ProcurementValidationException(
                $"{lineNumber}. kalemde kabul, red ve hasarlı toplamı teslim miktarına eşit olmalıdır.");
        }
    }

    private static string? TrimOrNull(string? value, int maxLength, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new ProcurementValidationException($"{fieldName} en fazla {maxLength} karakter olabilir.");

        return trimmed;
    }

    private static GoodsReceiptActionResponse Action(
        GoodsReceiptEntity receipt,
        int stockMovementCount,
        string message) =>
        new(
            receipt.Id,
            receipt.ReceiptNumber,
            (int)receipt.Status,
            stockMovementCount,
            message);

    private async Task<CurrentDataScopeSnapshot> GetScopeAsync(
        CancellationToken cancellationToken) =>
        await dataScope.GetAsync(cancellationToken) ??
        throw new UnauthorizedAccessException("Kullanıcı veri kapsamı bulunamadı.");
}

