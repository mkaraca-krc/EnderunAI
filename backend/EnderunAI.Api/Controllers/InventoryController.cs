using EnderunAI.Api.Contracts.Inventory;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/inventory")]
public sealed class InventoryController(AppDbContext db) : ControllerBase
{
    [HttpGet("items")]
    public async Task<IActionResult> GetItems([FromQuery] Guid? companyId, [FromQuery] string? search, CancellationToken cancellationToken)
    {
        var query = db.InventoryItems.AsNoTracking();
        if (companyId.HasValue) query = query.Where(x => x.CompanyId == companyId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x => x.Code.ToLower().Contains(term) || x.Name.ToLower().Contains(term) ||
                (x.Brand != null && x.Brand.ToLower().Contains(term)) ||
                (x.Model != null && x.Model.ToLower().Contains(term)));
        }

        var items = await query.OrderBy(x => x.Name).Select(x => new
        {
            x.Id, x.CompanyId, CompanyName = x.Company.Name, x.Code, x.Name, x.Category,
            x.Brand, x.Model, x.Unit, x.Barcode, x.MinimumStock, x.MaximumStock,
            x.Type, x.IsActive,
            TotalStock = x.WarehouseStocks.Sum(s => s.Quantity),
            AvailableStock = x.WarehouseStocks.Sum(s => s.Quantity - s.ReservedQuantity)
        }).ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpPost("items")]
    public async Task<IActionResult> CreateItem(CreateInventoryItemRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Unit))
            return BadRequest(new { message = "Malzeme kodu, adı ve birimi zorunludur." });

        if (!Enum.IsDefined(typeof(InventoryItemType), request.Type))
            return BadRequest(new { message = "Geçersiz malzeme tipi." });

        var companyExists = await db.Companies.AnyAsync(x => x.Id == request.CompanyId && x.IsActive, cancellationToken);
        if (!companyExists) return BadRequest(new { message = "Geçerli bir şirket seçilmelidir." });

        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.InventoryItems.AnyAsync(x => x.CompanyId == request.CompanyId && x.Code == code, cancellationToken))
            return Conflict(new { message = "Bu malzeme kodu zaten kullanılıyor." });

        var entity = new InventoryItem
        {
            CompanyId = request.CompanyId,
            Code = code,
            Name = request.Name.Trim(),
            Category = request.Category?.Trim(),
            Brand = request.Brand?.Trim(),
            Model = request.Model?.Trim(),
            Unit = request.Unit.Trim(),
            Barcode = request.Barcode?.Trim(),
            MinimumStock = request.MinimumStock,
            MaximumStock = request.MaximumStock,
            Type = (InventoryItemType)request.Type
        };

        db.InventoryItems.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = "Malzeme kartı oluşturuldu.", entity.Id, entity.Code, entity.Name });
    }

    [HttpGet("warehouses/{warehouseId:guid}/stocks")]
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
                x.InventoryItem.Unit, x.Quantity, x.ReservedQuantity,
                AvailableQuantity = x.Quantity - x.ReservedQuantity,
                x.InventoryItem.MinimumStock,
                IsCritical = x.Quantity - x.ReservedQuantity <= x.InventoryItem.MinimumStock
            }).ToListAsync(cancellationToken);

        return Ok(stocks);
    }

    [HttpGet("movements")]
    public async Task<IActionResult> GetMovements([FromQuery] Guid? warehouseId, [FromQuery] Guid? projectId,
        [FromQuery] Guid? inventoryItemId, CancellationToken cancellationToken)
    {
        var query = db.StockMovements.AsNoTracking();
        if (warehouseId.HasValue) query = query.Where(x => x.WarehouseId == warehouseId.Value);
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId.Value);
        if (inventoryItemId.HasValue) query = query.Where(x => x.InventoryItemId == inventoryItemId.Value);

        var movements = await query.OrderByDescending(x => x.MovementDate).ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id, x.WarehouseId, WarehouseName = x.Warehouse.Name,
                x.InventoryItemId, ItemCode = x.InventoryItem.Code, ItemName = x.InventoryItem.Name,
                x.ProjectId, ProjectName = x.Project != null ? x.Project.Name : null,
                x.RelatedWarehouseId,
                RelatedWarehouseName = x.RelatedWarehouse != null ? x.RelatedWarehouse.Name : null,
                x.PurchaseRequestId, x.Type, x.Quantity, x.ReferenceNumber, x.MovementDate, x.Description
            }).ToListAsync(cancellationToken);

        return Ok(movements);
    }

    [HttpPost("receipts")]
    public async Task<IActionResult> Receipt(StockReceiptRequest request, CancellationToken cancellationToken)
    {
        if (request.Quantity <= 0) return BadRequest(new { message = "Miktar sıfırdan büyük olmalıdır." });

        var warehouse = await db.Warehouses.SingleOrDefaultAsync(x => x.Id == request.WarehouseId, cancellationToken);
        if (warehouse is null) return NotFound(new { message = "Depo bulunamadı." });

        var item = await db.InventoryItems.SingleOrDefaultAsync(
            x => x.Id == request.InventoryItemId && x.CompanyId == warehouse.CompanyId, cancellationToken);
        if (item is null) return NotFound(new { message = "Malzeme kartı bulunamadı." });

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var stock = await db.WarehouseStocks.SingleOrDefaultAsync(
            x => x.WarehouseId == request.WarehouseId && x.InventoryItemId == request.InventoryItemId, cancellationToken);

        if (stock is null)
        {
            stock = new WarehouseStock { WarehouseId = request.WarehouseId, InventoryItemId = request.InventoryItemId };
            db.WarehouseStocks.Add(stock);
        }

        stock.Quantity += request.Quantity;
        stock.UpdatedAtUtc = DateTime.UtcNow;

        db.StockMovements.Add(new StockMovement
        {
            CompanyId = warehouse.CompanyId,
            WarehouseId = warehouse.Id,
            InventoryItemId = item.Id,
            ProjectId = request.ProjectId,
            PurchaseRequestId = request.PurchaseRequestId,
            Type = StockMovementType.Receipt,
            Quantity = request.Quantity,
            ReferenceNumber = request.ReferenceNumber.Trim(),
            MovementDate = request.MovementDate,
            Description = request.Description?.Trim()
        });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(new { message = "Depo girişi kaydedildi.", stock.Quantity });
    }

    [HttpPost("issues")]
    public async Task<IActionResult> Issue(StockIssueRequest request, CancellationToken cancellationToken)
    {
        if (request.Quantity <= 0) return BadRequest(new { message = "Miktar sıfırdan büyük olmalıdır." });

        var stock = await db.WarehouseStocks.Include(x => x.Warehouse).SingleOrDefaultAsync(
            x => x.WarehouseId == request.WarehouseId && x.InventoryItemId == request.InventoryItemId, cancellationToken);

        if (stock is null) return NotFound(new { message = "Depoda bu malzeme bulunmuyor." });
        if (stock.Quantity - stock.ReservedQuantity < request.Quantity)
            return Conflict(new { message = "Kullanılabilir stok yetersiz." });

        stock.Quantity -= request.Quantity;
        stock.UpdatedAtUtc = DateTime.UtcNow;

        db.StockMovements.Add(new StockMovement
        {
            CompanyId = stock.Warehouse.CompanyId,
            WarehouseId = stock.WarehouseId,
            InventoryItemId = stock.InventoryItemId,
            ProjectId = request.ProjectId,
            Type = StockMovementType.Issue,
            Quantity = request.Quantity,
            ReferenceNumber = request.ReferenceNumber.Trim(),
            MovementDate = request.MovementDate,
            Description = request.Description?.Trim()
        });

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = "Depo çıkışı kaydedildi.", stock.Quantity });
    }

    [HttpPost("transfers")]
    public async Task<IActionResult> Transfer(StockTransferRequest request, CancellationToken cancellationToken)
    {
        if (request.SourceWarehouseId == request.TargetWarehouseId)
            return BadRequest(new { message = "Kaynak ve hedef depo aynı olamaz." });
        if (request.Quantity <= 0) return BadRequest(new { message = "Miktar sıfırdan büyük olmalıdır." });

        var source = await db.WarehouseStocks.Include(x => x.Warehouse).SingleOrDefaultAsync(
            x => x.WarehouseId == request.SourceWarehouseId && x.InventoryItemId == request.InventoryItemId, cancellationToken);
        if (source is null) return NotFound(new { message = "Kaynak depoda malzeme bulunamadı." });

        var targetWarehouse = await db.Warehouses.SingleOrDefaultAsync(x => x.Id == request.TargetWarehouseId, cancellationToken);
        if (targetWarehouse is null) return NotFound(new { message = "Hedef depo bulunamadı." });
        if (source.Warehouse.CompanyId != targetWarehouse.CompanyId)
            return BadRequest(new { message = "Depolar aynı şirkete ait olmalıdır." });
        if (source.Quantity - source.ReservedQuantity < request.Quantity)
            return Conflict(new { message = "Kaynak depoda yeterli stok yok." });

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
                ReferenceNumber = request.ReferenceNumber.Trim(),
                MovementDate = request.MovementDate,
                Description = request.Description?.Trim()
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
                ReferenceNumber = request.ReferenceNumber.Trim(),
                MovementDate = request.MovementDate,
                Description = request.Description?.Trim()
            });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(new { message = "Depolar arası transfer tamamlandı." });
    }
}
