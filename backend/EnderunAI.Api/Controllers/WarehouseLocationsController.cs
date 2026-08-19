using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// DEPO KONUMLARI — bölge, raf, kat ve kategori varsayılanları.
///
/// ÜÇ SEVİYE: Bölge → Raf → Kat ("Oda 2 - Raf 3 - Kat 2"). AÇIK
/// bölgede yalnız bölge vardır: rafa sığmayan büyük malzemeden
/// raf/kat istemek, olmayan bir ayrıntıyı zorunlu kılmak olurdu.
/// </summary>
[ApiController]
[Authorize]
[Route("api/warehouses/{warehouseId:guid}/locations")]
public sealed class WarehouseLocationsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.InventoryView)]
    public async Task<IActionResult> Get(Guid warehouseId, CancellationToken cancellationToken)
    {
        var exists = await db.Warehouses.AnyAsync(x => x.Id == warehouseId, cancellationToken);
        if (!exists) return NotFound(new { message = "Depo bulunamadı." });

        var zones = await db.WarehouseZones.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId && x.IsActive)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Select(zone => new
            {
                zone.Id,
                zone.Code,
                zone.Name,
                Kind = (int)zone.Kind,
                zone.SortOrder,
                Shelves = zone.Shelves
                    .Where(shelf => shelf.IsActive)
                    .OrderBy(shelf => shelf.SortOrder)
                    .Select(shelf => new
                    {
                        shelf.Id,
                        shelf.Code,
                        shelf.SortOrder,
                        Levels = shelf.Levels
                            .Where(level => level.IsActive)
                            .OrderBy(level => level.SortOrder)
                            .Select(level => new { level.Id, level.Code, level.SortOrder })
                            .ToList()
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        var defaults = await db.WarehouseCategoryLocations.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId)
            .Select(x => new
            {
                x.Id,
                x.InventoryCategoryId,
                CategoryName = x.InventoryCategory.Name,
                x.WarehouseZoneId,
                ZoneName = x.WarehouseZone.Name,
                x.WarehouseShelfId,
                ShelfCode = x.WarehouseShelf != null ? x.WarehouseShelf.Code : null,
                x.WarehouseShelfLevelId,
                LevelCode = x.WarehouseShelfLevel != null ? x.WarehouseShelfLevel.Code : null
            })
            .ToListAsync(cancellationToken);

        return Ok(new { zones, defaults });
    }

    public sealed record ZoneRequest(
        string Code,
        string Name,
        int Kind,
        int SortOrder,
        /* Raflı bölgede toplu kurulum: kaç raf, her rafta kaç kat. */
        int ShelfCount,
        int LevelsPerShelf);

    [HttpPost("zones")]
    [RequirePermission(PermissionCatalog.Keys.InventoryManage)]
    public async Task<IActionResult> CreateZone(
        Guid warehouseId, ZoneRequest request, CancellationToken cancellationToken)
    {
        var exists = await db.Warehouses.AnyAsync(x => x.Id == warehouseId, cancellationToken);
        if (!exists) return NotFound(new { message = "Depo bulunamadı." });

        var code = request.Code?.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "Bölge kodu ve adı zorunludur." });

        if (await db.WarehouseZones.AnyAsync(
                x => x.WarehouseId == warehouseId && x.Code == code, cancellationToken))
            return Conflict(new { message = $"'{code}' kodlu bölge zaten var." });

        var kind = (WarehouseZoneKind)request.Kind;

        var zone = new WarehouseZone
        {
            WarehouseId = warehouseId,
            Code = code,
            Name = request.Name.Trim(),
            Kind = kind,
            SortOrder = request.SortOrder
        };

        if (kind == WarehouseZoneKind.Shelved)
        {
            if (request.ShelfCount <= 0)
                return BadRequest(new
                {
                    message = "Raflı bölgede en az bir raf tanımlanmalıdır."
                });

            for (var shelfNo = 1; shelfNo <= request.ShelfCount; shelfNo++)
            {
                var shelf = new WarehouseShelf
                {
                    Code = $"RAF {shelfNo}",
                    SortOrder = shelfNo * 10
                };

                // Kat sayısı sıfır verilirse tek kat kurulur: raf var
                // ama kat yok demek, üç seviyeli konumu yarım bırakır.
                var levels = Math.Max(1, request.LevelsPerShelf);

                for (var levelNo = 1; levelNo <= levels; levelNo++)
                {
                    shelf.Levels.Add(new WarehouseShelfLevel
                    {
                        Code = $"KAT {levelNo}",
                        SortOrder = levelNo * 10
                    });
                }

                zone.Shelves.Add(shelf);
            }
        }
        else if (request.ShelfCount > 0)
        {
            return BadRequest(new
            {
                message = "AÇIK bölgede raf tanımlanmaz — konum yalnız bölge seviyesindedir."
            });
        }

        db.WarehouseZones.Add(zone);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Bölge oluşturuldu.", zone.Id, zone.Code });
    }

    public sealed record DefaultLocationRequest(
        Guid CategoryId,
        Guid ZoneId,
        Guid? ShelfId,
        Guid? LevelId);

    /// <summary>
    /// Bu depoda bu kategori nereye gider.
    ///
    /// Kategori SİSTEM GENELİ olduğu için varsayılan konum kategoride
    /// tutulamıyor: konum belirli bir şirketin belirli bir deposundaki
    /// fiziksel yer. Eşleştirme burada.
    /// </summary>
    [HttpPut("defaults")]
    [RequirePermission(PermissionCatalog.Keys.InventoryManage)]
    public async Task<IActionResult> SetDefault(
        Guid warehouseId, DefaultLocationRequest request, CancellationToken cancellationToken)
    {
        var zone = await db.WarehouseZones
            .Include(x => x.Shelves).ThenInclude(x => x.Levels)
            .SingleOrDefaultAsync(
                x => x.Id == request.ZoneId && x.WarehouseId == warehouseId, cancellationToken);

        if (zone is null)
            return BadRequest(new { message = "Bölge bu depoya ait değil." });

        var categoryExists = await db.InventoryCategories
            .AnyAsync(x => x.Id == request.CategoryId, cancellationToken);

        if (!categoryExists) return BadRequest(new { message = "Kategori bulunamadı." });

        // BÖLGE TİPİ BELİRLEYİCİ: raflı bölgede raf+kat zorunlu,
        // açık bölgede verilemez.
        if (zone.Kind == WarehouseZoneKind.Shelved)
        {
            if (request.ShelfId is null || request.LevelId is null)
                return BadRequest(new
                {
                    message = $"'{zone.Name}' raflı bir bölge; raf ve kat seçilmelidir."
                });

            var shelf = zone.Shelves.SingleOrDefault(x => x.Id == request.ShelfId.Value);

            if (shelf is null)
                return BadRequest(new { message = "Raf bu bölgeye ait değil." });

            if (shelf.Levels.All(x => x.Id != request.LevelId.Value))
                return BadRequest(new { message = "Kat bu rafa ait değil." });
        }
        else if (request.ShelfId is not null || request.LevelId is not null)
        {
            return BadRequest(new
            {
                message = $"'{zone.Name}' açık bir bölge; raf ve kat seçilemez."
            });
        }

        var existing = await db.WarehouseCategoryLocations.SingleOrDefaultAsync(
            x => x.WarehouseId == warehouseId && x.InventoryCategoryId == request.CategoryId,
            cancellationToken);

        if (existing is null)
        {
            db.WarehouseCategoryLocations.Add(new WarehouseCategoryLocation
            {
                WarehouseId = warehouseId,
                InventoryCategoryId = request.CategoryId,
                WarehouseZoneId = request.ZoneId,
                WarehouseShelfId = request.ShelfId,
                WarehouseShelfLevelId = request.LevelId
            });
        }
        else
        {
            existing.WarehouseZoneId = request.ZoneId;
            existing.WarehouseShelfId = request.ShelfId;
            existing.WarehouseShelfLevelId = request.LevelId;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Varsayılan konum kaydedildi." });
    }

    /// <summary>
    /// RAF QR'I OKUTULUNCA: "bu rafta ne var".
    ///
    /// Rafın QR'ı bu uca gider; depo görevlisi telefonla okutup
    /// karşısındaki rafın içeriğini görür.
    /// </summary>
    [HttpGet("shelves/{shelfId:guid}/items")]
    [RequirePermission(PermissionCatalog.Keys.InventoryView)]
    public async Task<IActionResult> ShelfItems(
        Guid warehouseId, Guid shelfId, CancellationToken cancellationToken)
    {
        var shelf = await db.WarehouseShelves.AsNoTracking()
            .Where(x => x.Id == shelfId && x.WarehouseZone.WarehouseId == warehouseId)
            .Select(x => new
            {
                x.Id,
                x.Code,
                ZoneName = x.WarehouseZone.Name,
                WarehouseName = x.WarehouseZone.Warehouse.Name
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (shelf is null) return NotFound(new { message = "Raf bulunamadı." });

        var items = await db.InventoryItems.AsNoTracking()
            .Where(x => x.WarehouseShelfId == shelfId && x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                x.Code,
                x.Name,
                x.Unit,
                LevelCode = x.WarehouseShelfLevel != null ? x.WarehouseShelfLevel.Code : null,
                OnHand = x.WarehouseStocks.Sum(stock => (decimal?)stock.Quantity) ?? 0m
            })
            .ToListAsync(cancellationToken);

        return Ok(new { shelf, items });
    }
}
