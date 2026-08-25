namespace EnderunAI.Api.Controllers;

using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// DEPODAN ZİMMET UÇLARI.
///
/// Mevcut `HrAssetsController` serbest metinli zimmet tutuyor (marka,
/// model elle yazılıyor) ve depoyla bağı yok. Bu uçlar ONUN YERİNE
/// GEÇMİYOR, yanına geliyor: stoktan çıkan zimmet ayrı bir iş.
/// Eskisini bu paket kapsamında değiştirmedim — orada kapsam süzgeci
/// ve RowVersion yok, ikisi ayrı borç olarak duruyor.
/// </summary>
[ApiController]
[Authorize]
// YOL ÖN YÜZÜN ZATEN ÇAĞIRDIĞI YOL.
//
// `hr-asset.service.ts` bu uçları çağırıyordu ve uç yazılmadığı için
// kırık servis çağrısı çizgisinde duruyorlardı. Sunucuya ayrı bir yol
// koymak, aynı iş için iki sözleşme yaşatır ve ön yüzü değiştirmeden
// çizgiyi kapatamazdım.
[Route("api/hr/assets")]
public sealed class DepodanZimmetController(
    AppDbContext db,
    IDepodanZimmetService zimmetler,
    ICurrentDataScopeService dataScope) : ControllerBase
{
    [HttpPost("from-inventory")]
    [RequirePermission(PermissionCatalog.Keys.InventoryEdit)]
    public async Task<IActionResult> ZimmetVer(
        DepodanZimmetIstegi istek, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await zimmetler.ZimmetVerAsync(istek, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/return-to-warehouse")]
    [RequirePermission(PermissionCatalog.Keys.InventoryEdit)]
    public async Task<IActionResult> Iade(
        Guid id, ZimmetIadeIstegi istek, CancellationToken cancellationToken)
    {
        try
        {
            await zimmetler.IadeAlAsync(id, istek, cancellationToken);
            return Ok(new { message = "İade alındı." });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// İPTAL — yanlış kişiye verilmiş kaydın düzeltilmesi.
    ///
    /// İade ile aynı stok işini yapıyor ama ayrı uç: iade normal iş
    /// akışı, iptal bir DÜZELTME. Aynı uca "durum" parametresiyle
    /// bindirmek, denetim kaydında ikisini ayırt edilemez yapardı.
    /// </summary>
    [HttpPost("{id:guid}/cancel-assignment")]
    [RequirePermission(PermissionCatalog.Keys.InventoryEdit)]
    public async Task<IActionResult> Iptal(
        Guid id, ZimmetIptalIstegi istek, CancellationToken cancellationToken)
    {
        try
        {
            await zimmetler.IptalEtAsync(id, istek, cancellationToken);
            return Ok(new { message = "Zimmet iptal edildi." });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// ZİMMETTE OLANLAR — keyset sayfalama.
    ///
    /// COUNT(*) yok: zimmet kayıtları personel sayısı × kalem çeşidi
    /// kadar büyüyor ve sayfa başına toplam saymak, tablonun en hızlı
    /// büyüyen tarafında her istekte tam tarama demek.
    /// </summary>
    [HttpGet("from-inventory")]
    [RequirePermission(PermissionCatalog.Keys.InventoryView)]
    public async Task<IActionResult> Listele(
        [FromQuery] Guid companyId,
        [FromQuery] DateTime? sonTarih,
        [FromQuery] Guid? sonId,
        [FromQuery] int limit,
        CancellationToken cancellationToken)
    {
        var kapsam = await dataScope.GetAsync(cancellationToken);
        if (kapsam is null)
            return Forbid();

        if (!kapsam.HasGlobalAccess && !kapsam.CompanyIds.Contains(companyId))
            return Forbid();

        var sayfa = limit is <= 0 or > 200 ? 50 : limit;

        var sorgu = db.HrAssetAssignments
            .AsNoTracking()
            .ApplyScope(kapsam)
            .Where(x => x.CompanyId == companyId)
            .Where(x => x.InventoryItemId != null)
            .Where(x => x.Status == HrAssetAssignmentStatus.Assigned);

        if (sonTarih is not null && sonId is not null)
        {
            sorgu = sorgu.Where(x =>
                x.CreatedAtUtc < sonTarih ||
                (x.CreatedAtUtc == sonTarih && x.Id.CompareTo(sonId.Value) < 0));
        }

        var kayitlar = await sorgu
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(sayfa)
            .Select(x => new
            {
                x.Id,
                x.PersonnelId,
                x.AssetCode,
                x.AssetName,
                x.AssignmentDate,
                x.WarehouseId,
                Miktar = db.StockMovements
                    .Where(m => m.Id == x.IssueStockMovementId)
                    .Select(m => (decimal?)m.Quantity)
                    .FirstOrDefault(),
                RowVersion = x.UpdatedAtUtc ?? x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            kayitlar,
            sonrakiVar = kayitlar.Count == sayfa
        });
    }
}
