using EnderunAI.Api.Contracts.Inventory;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Services.Inventory;
using EnderunAI.Api.Services.Purchasing.Automation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// DEPO BAZLI ASGARİ/AZAMİ SEVİYE ve bunlardan doğan satın alma
/// talebi önerisi.
///
/// Seviye tanımı stok kartını DEĞİŞTİRMEZ: kartta artık min/max alanı
/// yok (bkz. <see cref="WarehouseStockLevel"/>). Seviye satırı silinmek,
/// takibi bırakmak demektir; "asgarisi sıfır" diye bir takip yoktur.
/// </summary>
[ApiController]
[Authorize]
[Route("api/stock-levels")]
public sealed class StockLevelsController(
    AppDbContext db,
    StockLevelAlertService alerts,
    IPurchaseRequestGenerator generator,
    ICurrentUserService currentUser)
    : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.InventoryView)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? warehouseId,
        [FromQuery] bool? belowMinimumOnly,
        CancellationToken cancellationToken)
    {
        var rows = await alerts.BuildAsync(
            companyId,
            warehouseId,
            belowMinimumOnly == true,
            cancellationToken);

        return Ok(rows);
    }

    /// <summary>
    /// Seviye tanımlar ya da günceller. Aynı depo+malzeme için ikinci
    /// satır açılmaz; mevcut satır güncellenir.
    /// </summary>
    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.InventoryEdit)]
    public async Task<IActionResult> Save(
        SaveWarehouseStockLevelRequest request,
        CancellationToken cancellationToken)
    {
        if (request.MinimumQuantity <= 0m)
        {
            return BadRequest(new
            {
                message =
                    "Asgari miktar sıfırdan büyük olmalıdır. Takibi bırakmak için seviye satırını silin."
            });
        }

        if (request.MaximumQuantity is decimal max && max <= request.MinimumQuantity)
        {
            return BadRequest(new
            {
                message = "Azami miktar asgari miktardan büyük olmalıdır."
            });
        }

        var warehouse = await db.Warehouses
            .AsNoTracking()
            .Where(x => x.Id == request.WarehouseId)
            .Select(x => new { x.Id, x.CompanyId })
            .SingleOrDefaultAsync(cancellationToken);

        if (warehouse is null)
            return NotFound(new { message = "Depo bulunamadı." });

        var item = await db.InventoryItems
            .AsNoTracking()
            .Where(x => x.Id == request.InventoryItemId)
            .Select(x => new { x.Id, x.CompanyId, x.IsActive })
            .SingleOrDefaultAsync(cancellationToken);

        if (item is null)
            return NotFound(new { message = "Malzeme kartı bulunamadı." });

        // Şirketler arası seviye tanımı, bir şirketin deposunu başka
        // şirketin malzemesiyle ikmal etmeye kalkardı.
        if (item.CompanyId != warehouse.CompanyId)
        {
            return BadRequest(new
            {
                message = "Malzeme kartı bu deponun şirketine ait değil."
            });
        }

        var existing = await db.WarehouseStockLevels
            .SingleOrDefaultAsync(
                x => x.WarehouseId == request.WarehouseId &&
                     x.InventoryItemId == request.InventoryItemId,
                cancellationToken);

        if (existing is null)
        {
            db.WarehouseStockLevels.Add(new WarehouseStockLevel
            {
                WarehouseId = request.WarehouseId,
                InventoryItemId = request.InventoryItemId,
                MinimumQuantity = request.MinimumQuantity,
                MaximumQuantity = request.MaximumQuantity,
                Note = request.Note?.Trim(),
                CreatedByUserId = currentUser.UserId
            });
        }
        else
        {
            existing.MinimumQuantity = request.MinimumQuantity;
            existing.MaximumQuantity = request.MaximumQuantity;
            existing.Note = request.Note?.Trim();
            existing.UpdatedAtUtc = DateTime.UtcNow;
            existing.UpdatedByUserId = currentUser.UserId;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = "Stok seviyesi kaydedildi." });
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.InventoryEdit)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var level = await db.WarehouseStockLevels
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (level is null)
            return NotFound(new { message = "Stok seviyesi bulunamadı." });

        // Kim ne zaman takibi bıraktı: seviye bir POLİTİKA kaydı, kalkması
        // da bir karar. Yalnız `IsDeleted` işaretlense o karar izsiz kalırdı.
        level.IsDeleted = true;
        level.DeletedAtUtc = DateTime.UtcNow;
        level.DeletedByUserId = currentUser.UserId;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = "Stok seviyesi takibi kaldırıldı." });
    }

    /// <summary>
    /// Asgarinin altına düşmüş kalemlerden satın alma talebi üretir.
    ///
    /// İZİN: talep AÇILDIĞI için satın alma talebi oluşturma izni
    /// aranıyor, stok görme izni değil. Depoyu gören herkes talep
    /// açabilseydi onay süreci baştan delinirdi.
    /// </summary>
    [HttpPost("satin-alma-talebi")]
    [RequirePermission(PermissionCatalog.Keys.PurchasingRequestsCreate)]
    public async Task<IActionResult> CreatePurchaseRequest(
        GeneratePurchaseRequestFromStockLevelsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await generator.GenerateFromStockLevelsAsync(
                request,
                currentUser.UserId,
                cancellationToken);

            return Ok(new
            {
                message = "Stok seviyesi uyarılarından satın alma talebi oluşturuldu.",
                result.PurchaseRequestId,
                result.RequestNumber,
                result.WarehouseId,
                result.WarehouseName,
                result.LineCount,
                result.TotalQuantity
            });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }
}
