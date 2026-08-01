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

namespace EnderunAI.Api.Services.GoodsReceipts;

public sealed class GoodsReceiptService(
    AppDbContext db,
    IDocumentNumberService documentNumbers,
    ICurrentDataScopeService dataScope,
    ICurrentUserService currentUser) : IGoodsReceiptService
{
    public async Task<IReadOnlyList<GoodsReceiptListItemResponse>> GetAllAsync(
        Guid? companyId,
        Guid? warehouseId,
        Guid? purchaseOrderId,
        int? status,
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

        return await query
            .OrderByDescending(x => x.ReceiptDate)
            .ThenByDescending(x => x.CreatedAtUtc)
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
                        i.Notes))
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
                InventoryItemId = null,
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
            ValidatePostingQuantities(item);

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
            .AsNoTracking()
            .Where(x =>
                inventoryItemIds.Contains(x.Id) &&
                x.CompanyId == receipt.CompanyId &&
                x.IsActive)
            .Select(x => new { x.Id, x.Unit })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        if (inventoryItems.Count != inventoryItemIds.Length)
            throw new ProcurementValidationException("Bağlı stok kartlarından biri bulunamadı veya şirket kapsamına uygun değil.");

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

            stock.Quantity += item.AcceptedQuantity;
            stock.UpdatedAtUtc = now;
            stock.UpdatedByUserId = currentUser.UserId;

            purchaseOrderItems[item.PurchaseOrderItemId].ReceivedQuantity +=
                item.AcceptedQuantity;

            db.StockMovements.Add(new StockMovement
            {
                CompanyId = receipt.CompanyId,
                WarehouseId = receipt.WarehouseId,
                InventoryItemId = inventoryItemId,
                ProjectId = purchaseOrder.ProjectId,
                PurchaseRequestId = purchaseOrder.Rfq.PurchaseRequestId,
                Type = StockMovementType.Receipt,
                Quantity = item.AcceptedQuantity,
                ReferenceNumber = receipt.ReceiptNumber,
                MovementDate = receipt.ReceiptDate,
                Description = $"Mal kabul {receipt.ReceiptNumber} - {item.MaterialDescription}",
                CreatedByUserId = currentUser.UserId
            });
        }

        purchaseOrder.Status = purchaseOrder.Items.All(x =>
            x.ReceivedQuantity >= x.Quantity)
            ? PurchaseOrderStatus.Completed
            : PurchaseOrderStatus.PartiallyReceived;
        purchaseOrder.UpdatedAtUtc = now;
        purchaseOrder.UpdatedByUserId = currentUser.UserId;

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

