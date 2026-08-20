using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

public sealed record StartStockCountRequest(
    Guid CompanyId, Guid WarehouseId, Guid? WarehouseZoneId,
    string Name, DateTime CountDate);

public sealed record SaveStockCountRequest(
    IReadOnlyCollection<StockCountLineInput> Lines);

public sealed record StockCountDecisionRequest(string Reason);

/// <summary>
/// DÖNEMSEL SAYIM UÇLARI.
///
/// SAYMAK ile ONAYLAMAK ayrı izinlerde: sayan depo görevlisi
/// (`inventory.edit`), onaylayan yetkili (`accounting.approve` —
/// Genel Müdür ve Finans Sorumlusu). Aynı kişi hem sayıp hem
/// onaylayabilseydi fark, gerekçesi hiç sorgulanmadan stoğa ve
/// muhasebeye işlenirdi.
/// </summary>
[ApiController]
[Authorize]
[Route("api/stock-counts")]
public sealed class StockCountsController(
    AppDbContext db, IStockCountService counts) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.InventoryView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? warehouseId,
        [FromQuery] int? status,
        CancellationToken cancellationToken)
    {
        var query = db.StockCountSessions.AsNoTracking();

        if (warehouseId is Guid id) query = query.Where(x => x.WarehouseId == id);
        if (status is int s) query = query.Where(x => (int)x.Status == s);

        var rows = await query
            .OrderByDescending(x => x.CountDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.DocumentNumber,
                x.Name,
                x.CountDate,
                Status = (int)x.Status,
                x.WarehouseId,
                WarehouseName = x.Warehouse.Name,
                x.WarehouseZoneId,
                ZoneName = x.WarehouseZone != null ? x.WarehouseZone.Name : null,
                LineCount = x.Lines.Count,
                CountedCount = x.Lines.Count(l => l.CountedQuantity != null),
                VarianceCount = x.Lines.Count(l =>
                    l.CountedQuantity != null && l.CountedQuantity != l.SystemQuantity),
                x.AccountingVoucherId,
                x.DecisionReason
            })
            .ToListAsync(cancellationToken);

        return Ok(rows);
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.InventoryView)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var session = await db.StockCountSessions
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.DocumentNumber,
                x.Name,
                x.CountDate,
                Status = (int)x.Status,
                x.WarehouseId,
                WarehouseName = x.Warehouse.Name,
                x.WarehouseZoneId,
                ZoneName = x.WarehouseZone != null ? x.WarehouseZone.Name : null,
                x.AccountingVoucherId,
                x.DecisionReason,
                x.SubmittedAtUtc,
                x.DecidedAtUtc,
                Lines = x.Lines
                    .OrderBy(l => l.InventoryItem.Name)
                    .Select(l => new
                    {
                        l.Id,
                        l.InventoryItemId,
                        Code = l.InventoryItem.Code,
                        Name = l.InventoryItem.Name,
                        Unit = l.InventoryItem.Unit,
                        Barcode = l.InventoryItem.Barcode,
                        CategoryName = l.InventoryItem.InventoryCategory != null
                            ? l.InventoryItem.InventoryCategory.Name
                            : null,
                        ZoneName = l.InventoryItem.WarehouseZone != null
                            ? l.InventoryItem.WarehouseZone.Name
                            : null,
                        l.SystemQuantity,
                        l.CountedQuantity,
                        l.UnitCostAtCount,
                        VarianceReason = l.VarianceReason != null
                            ? (int)l.VarianceReason
                            : (int?)null,
                        l.Note
                    })
                    .ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);

        return session is null
            ? NotFound(new { message = "Sayım oturumu bulunamadı." })
            : Ok(session);
    }

    /// <summary>
    /// FARK RAPORU — hangi depo/bölge/kategoride ne kadar fark var.
    ///
    /// Takip amaçlı: tekrar eden kayıp aynı bölgede ya da aynı
    /// kategoride toplanıyorsa sebebi oradadır. Tek tek satırlara
    /// bakarak bu görülemez.
    /// </summary>
    [HttpGet("{id:guid}/fark-raporu")]
    [RequirePermission(PermissionCatalog.Keys.InventoryView)]
    public async Task<IActionResult> VarianceReport(Guid id, CancellationToken cancellationToken)
    {
        var session = await db.StockCountSessions
            .AsNoTracking()
            .Include(x => x.Lines).ThenInclude(x => x.InventoryItem).ThenInclude(x => x.InventoryCategory)
            .Include(x => x.Lines).ThenInclude(x => x.InventoryItem).ThenInclude(x => x.WarehouseZone)
            .Include(x => x.Warehouse)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (session is null)
            return NotFound(new { message = "Sayım oturumu bulunamadı." });

        var counted = session.Lines.Where(x => x.CountedQuantity is not null).ToList();
        var variances = counted.Where(x => x.Difference != 0m).ToList();

        decimal Value(StockCountLine line) =>
            decimal.Round(line.UnitCostAtCount * (line.Difference ?? 0m), 2);

        return Ok(new
        {
            session.DocumentNumber,
            session.Name,
            session.CountDate,
            WarehouseName = session.Warehouse.Name,

            // SAYILMAYAN SATIR SAYISI AÇIKÇA BİLDİRİLİYOR: atlanan
            // satırlar stoğu değiştirmiyor ve bunun sessiz kalmaması
            // gerekiyor.
            TotalLines = session.Lines.Count,
            CountedLines = counted.Count,
            UncountedLines = session.Lines.Count - counted.Count,
            VarianceLines = variances.Count,

            ShortageValue = decimal.Round(
                variances.Where(x => x.Difference < 0m).Sum(Value), 2),
            SurplusValue = decimal.Round(
                variances.Where(x => x.Difference > 0m).Sum(Value), 2),
            NetValue = decimal.Round(variances.Sum(Value), 2),

            ByZone = variances
                .GroupBy(x => x.InventoryItem.WarehouseZone?.Name ?? "Bölgesiz")
                .Select(g => new
                {
                    Zone = g.Key,
                    Lines = g.Count(),
                    Value = decimal.Round(g.Sum(Value), 2)
                })
                .OrderBy(x => x.Value)
                .ToList(),

            ByCategory = variances
                .GroupBy(x => x.InventoryItem.InventoryCategory?.Name ?? "Kategorisiz")
                .Select(g => new
                {
                    Category = g.Key,
                    Lines = g.Count(),
                    Value = decimal.Round(g.Sum(Value), 2)
                })
                .OrderBy(x => x.Value)
                .ToList(),

            ByReason = variances
                .GroupBy(x => x.VarianceReason)
                .Select(g => new
                {
                    Reason = g.Key is not null ? (int)g.Key : (int?)null,
                    ReasonLabel = StockCountService.ReasonLabel(g.Key),
                    Lines = g.Count(),
                    Value = decimal.Round(g.Sum(Value), 2)
                })
                .OrderBy(x => x.Value)
                .ToList()
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.InventoryEdit)]
    public async Task<IActionResult> Start(
        StartStockCountRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var session = await counts.StartAsync(
                request.CompanyId, request.WarehouseId, request.WarehouseZoneId,
                request.Name, request.CountDate, cancellationToken);

            return Ok(new
            {
                session.Id,
                session.DocumentNumber,
                LineCount = session.Lines.Count,
                message = "Sayım başlatıldı; bölge sayım bitene kadar kilitli."
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}/miktarlar")]
    [RequirePermission(PermissionCatalog.Keys.InventoryEdit)]
    public async Task<IActionResult> SaveCounts(
        Guid id, SaveStockCountRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await counts.SaveCountsAsync(id, request.Lines, cancellationToken);
            return Ok(new { message = "Sayım miktarları kaydedildi." });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("{id:guid}/onaya-gonder")]
    [RequirePermission(PermissionCatalog.Keys.InventoryEdit)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await counts.SubmitAsync(id, cancellationToken);
            return Ok(new { message = "Sayım onaya gönderildi." });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>
    /// ONAY — stoğu düzeltir ve muhasebe fişini keser.
    ///
    /// `accounting.approve` iznine bağlı: bu işlem yalnız depo miktarını
    /// değil MALİ TABLOYU da değiştiriyor. Depo iznine bağlansaydı,
    /// sayan kişi kendi farkını onaylayıp gidere yazabilirdi.
    /// </summary>
    [HttpPost("{id:guid}/onayla")]
    [RequirePermission(PermissionCatalog.Keys.AccountingApprove)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var session = await counts.ApproveAsync(id, cancellationToken);

            return Ok(new
            {
                session.AccountingVoucherId,
                message = session.AccountingVoucherId is null
                    ? "Sayım onaylandı; fark bulunmadığı (ya da maliyeti sıfır olduğu) "
                      + "için muhasebe fişi kesilmedi."
                    : "Sayım onaylandı; stok düzeltildi ve düzeltme fişi kesildi."
            });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("{id:guid}/reddet")]
    [RequirePermission(PermissionCatalog.Keys.AccountingApprove)]
    public async Task<IActionResult> Reject(
        Guid id, StockCountDecisionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await counts.RejectAsync(id, request.Reason, cancellationToken);
            return Ok(new { message = "Sayım reddedildi; stok değişmedi." });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("{id:guid}/iptal")]
    [RequirePermission(PermissionCatalog.Keys.InventoryEdit)]
    public async Task<IActionResult> Cancel(
        Guid id, StockCountDecisionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await counts.CancelAsync(id, request.Reason, cancellationToken);
            return Ok(new { message = "Sayım iptal edildi; bölge kilidi kalktı." });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }
}
