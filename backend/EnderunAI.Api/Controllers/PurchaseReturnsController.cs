using EnderunAI.Api.Data;
using EnderunAI.Api.Models.GoodsReceipt;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

/// <summary>Alış iadesinin durumunu ilerletme isteği.</summary>
/// <param name="Status">Hedef durum.</param>
/// <param name="Note">Gerekçe / açıklama.</param>
public sealed record AdvancePurchaseReturnRequest(int Status, string? Note);

/// <summary>
/// Alış iadesi belgeleri.
///
/// Mal kabulde reddedilen ya da hasarlı gelen miktar için kabul
/// kesinleşirken OTOMATİK doğar. Bu denetleyici belgeyi okumak ve
/// tedarikçiyle olan süreci (gönderildi / kapandı / iptal)
/// yürütmek için.
/// </summary>
[ApiController]
[Authorize]
[Route("api/purchase-returns")]
public sealed class PurchaseReturnsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.PurchasingReceiptsView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] Guid? goodsReceiptId,
        [FromQuery] int? status,
        [FromQuery] bool? openOnly,
        CancellationToken cancellationToken)
    {
        var query = db.PurchaseReturns.AsNoTracking();

        if (companyId is Guid cid) query = query.Where(x => x.CompanyId == cid);
        if (projectId is Guid pid) query = query.Where(x => x.ProjectId == pid);

        if (goodsReceiptId is Guid grid)
            query = query.Where(x => x.GoodsReceiptId == grid);

        if (status is int s)
        {
            if (!Enum.IsDefined(typeof(PurchaseReturnStatus), s))
                return BadRequest(new { message = "Geçersiz iade durumu." });

            query = query.Where(x => x.Status == (PurchaseReturnStatus)s);
        }

        // Bekleyen iade: henüz tedarikçiyle kapanmamış belge.
        if (openOnly == true)
        {
            query = query.Where(x =>
                x.Status == PurchaseReturnStatus.Draft ||
                x.Status == PurchaseReturnStatus.Sent);
        }

        return Ok(await query
            .OrderByDescending(x => x.ReturnDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                x.ReturnNumber,
                x.ReturnDate,
                Status = (int)x.Status,
                StatusName = StatusName(x.Status),
                x.CurrencyCode,
                x.TotalAmount,
                x.GoodsReceiptId,
                ReceiptNumber = x.GoodsReceipt.ReceiptNumber,
                x.PurchaseOrderId,
                OrderNumber = x.PurchaseOrder.OrderNumber,
                x.SupplierCurrentAccountId,
                SupplierName = x.SupplierCurrentAccount.Title,
                x.ProjectId,
                ProjectCode = x.Project.Code,
                ProjectName = x.Project.Name,
                ItemCount = x.Items.Count,
                TotalQuantity = x.Items.Sum(i => i.Quantity)
            })
            .ToListAsync(cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PurchasingReceiptsView)]
    public async Task<IActionResult> GetById(
        Guid id, CancellationToken cancellationToken)
    {
        var row = await db.PurchaseReturns
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                x.ReturnNumber,
                x.ReturnDate,
                Status = (int)x.Status,
                StatusName = StatusName(x.Status),
                x.CurrencyCode,
                x.ExchangeRate,
                x.TotalAmount,
                x.Notes,
                x.SentAtUtc,
                x.CompletedAtUtc,
                x.CancelledAtUtc,
                x.CancellationReason,
                x.GoodsReceiptId,
                ReceiptNumber = x.GoodsReceipt.ReceiptNumber,
                x.PurchaseOrderId,
                OrderNumber = x.PurchaseOrder.OrderNumber,
                x.SupplierCurrentAccountId,
                SupplierName = x.SupplierCurrentAccount.Title,
                x.ProjectId,
                ProjectCode = x.Project.Code,
                ProjectName = x.Project.Name,
                Items = x.Items
                    .OrderBy(i => i.LineNumber)
                    .Select(i => new
                    {
                        i.Id,
                        i.LineNumber,
                        i.MaterialDescription,
                        i.Unit,
                        i.Quantity,
                        i.UnitPrice,
                        i.LineTotal,
                        ReasonKind = (int)i.ReasonKind,
                        ReasonKindName = i.ReasonKind == PurchaseReturnReasonKind.Damaged
                            ? "Hasarlı"
                            : "Reddedildi",
                        i.Reason
                    })
            })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? NotFound(new { message = "Alış iadesi belgesi bulunamadı." })
            : Ok(row);
    }

    /// <summary>
    /// İade sürecini ilerletir: Taslak → Gönderildi → Kapandı, ya da
    /// İptal.
    ///
    /// İPTAL, malın iade edilmediği anlamına gelir (yerinde çözüldü);
    /// gerekçe zorunlu çünkü reddedilmiş bir mal sessizce ortadan
    /// kaybolmamalı.
    /// </summary>
    [HttpPost("{id:guid}/durum")]
    [RequirePermission(PermissionCatalog.Keys.PurchasingReceiptsEdit)]
    public async Task<IActionResult> Advance(
        Guid id,
        AdvancePurchaseReturnRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(typeof(PurchaseReturnStatus), request.Status))
            return BadRequest(new { message = "Geçersiz iade durumu." });

        var target = (PurchaseReturnStatus)request.Status;

        var entity = await db.PurchaseReturns
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
            return NotFound(new { message = "Alış iadesi belgesi bulunamadı." });

        var problem = ValidateTransition(entity.Status, target, request.Note);

        if (problem is not null)
            return BadRequest(new { message = problem });

        var raw = User.FindFirst("sub")?.Value
            ?? User.FindFirst(
                System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        var actorId = Guid.TryParse(raw, out var parsed) ? parsed : (Guid?)null;
        var now = DateTime.UtcNow;

        switch (target)
        {
            case PurchaseReturnStatus.Sent:
                entity.SentAtUtc = now;
                entity.SentByUserId = actorId;
                break;

            case PurchaseReturnStatus.Completed:
                entity.CompletedAtUtc = now;
                entity.CompletedByUserId = actorId;
                break;

            case PurchaseReturnStatus.Cancelled:
                entity.CancelledAtUtc = now;
                entity.CancelledByUserId = actorId;
                entity.CancellationReason = request.Note?.Trim();
                break;
        }

        entity.Status = target;
        entity.UpdatedAtUtc = now;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = $"Alış iadesi {StatusName(target)} olarak işaretlendi.",
            status = (int)target
        });
    }

    private static string? ValidateTransition(
        PurchaseReturnStatus from, PurchaseReturnStatus to, string? note)
    {
        if (from == to)
            return "Belge zaten bu durumda.";

        if (from is PurchaseReturnStatus.Completed or PurchaseReturnStatus.Cancelled)
            return $"{StatusName(from)} durumundaki belge değiştirilemez.";

        var allowed = from switch
        {
            PurchaseReturnStatus.Draft =>
                to is PurchaseReturnStatus.Sent or PurchaseReturnStatus.Cancelled,

            PurchaseReturnStatus.Sent =>
                to is PurchaseReturnStatus.Completed or PurchaseReturnStatus.Cancelled,

            _ => false
        };

        if (!allowed)
            return $"{StatusName(from)} durumundan {StatusName(to)} durumuna geçilemez.";

        if (to == PurchaseReturnStatus.Cancelled && string.IsNullOrWhiteSpace(note))
        {
            return "İptal gerekçesi zorunludur; reddedilmiş mal sessizce " +
                   "kaybolmamalı.";
        }

        return null;
    }

    private static string StatusName(PurchaseReturnStatus status) => status switch
    {
        PurchaseReturnStatus.Draft => "Taslak",
        PurchaseReturnStatus.Sent => "Tedarikçiye gönderildi",
        PurchaseReturnStatus.Completed => "Kapandı",
        _ => "İptal"
    };
}
