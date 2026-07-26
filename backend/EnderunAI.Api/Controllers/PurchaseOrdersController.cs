using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Route("api/purchase-orders")]
[Authorize]
public sealed class PurchaseOrdersController(AppDbContext db) : ControllerBase
{
    public sealed record PurchaseOrderItemInput(
        Guid MaterialId,
        decimal Quantity,
        string Unit,
        decimal UnitPrice,
        decimal DiscountRate,
        string? Description);

    public sealed record CreatePurchaseOrderRequest(
        Guid CompanyId,
        Guid ProjectId,
        Guid SupplierCurrentAccountId,
        Guid? PurchaseRequestId,
        string OrderNumber,
        DateTime? DeliveryDateUtc,
        string CurrencyCode,
        decimal ExchangeRate,
        decimal VatRate,
        string? Description,
        IReadOnlyList<PurchaseOrderItemInput> Items);

    [HttpGet]
    public async Task<ActionResult> List(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] PurchaseOrderStatus? status,
        CancellationToken cancellationToken)
    {
        var query = db.PurchaseOrders
            .AsNoTracking()
            .Include(x => x.SupplierCurrentAccount)
            .Include(x => x.Items)
            .AsQueryable();

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);
        if (projectId.HasValue)
            query = query.Where(x => x.ProjectId == projectId.Value);
        if (status.HasValue)
            query = query.Where(x => x.Status == status.Value);

        var result = await query
            .OrderByDescending(x => x.OrderDateUtc)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                x.ProjectId,
                x.OrderNumber,
                x.OrderDateUtc,
                x.DeliveryDateUtc,
                x.Status,
                x.CurrencyCode,
                x.ExchangeRate,
                x.VatRate,
                SupplierId = x.SupplierCurrentAccountId,
                SupplierTitle = x.SupplierCurrentAccount.Title,
                NetAmount = x.Items.Sum(i => i.Quantity * i.UnitPrice * (1 - i.DiscountRate / 100m)),
                ReceivedQuantity = x.Items.Sum(i => i.ReceivedQuantity)
            })
            .ToListAsync(cancellationToken);

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var entity = await db.PurchaseOrders
            .AsNoTracking()
            .Include(x => x.SupplierCurrentAccount)
            .Include(x => x.Items)
            .ThenInclude(x => x.Material)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        return entity is null ? NotFound() : Ok(entity);
    }

    [HttpPost]
    public async Task<ActionResult> Create(
        CreatePurchaseOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.OrderNumber))
            return BadRequest("Sipariş numarası zorunludur.");
        if (request.Items.Count == 0)
            return BadRequest("En az bir sipariş kalemi eklenmelidir.");
        if (request.Items.Any(x => x.Quantity <= 0 || x.UnitPrice < 0))
            return BadRequest("Miktar sıfırdan büyük, fiyat sıfır veya daha büyük olmalıdır.");
        if (request.ExchangeRate <= 0)
            return BadRequest("Kur sıfırdan büyük olmalıdır.");

        var duplicate = await db.PurchaseOrders.AnyAsync(
            x => x.CompanyId == request.CompanyId && x.OrderNumber == request.OrderNumber,
            cancellationToken);
        if (duplicate)
            return Conflict("Bu sipariş numarası daha önce kullanılmış.");

        if (request.PurchaseRequestId.HasValue)
        {
            var purchaseRequest = await db.PurchaseRequests.FirstOrDefaultAsync(
                x => x.Id == request.PurchaseRequestId.Value,
                cancellationToken);
            if (purchaseRequest is null)
                return BadRequest("Satın alma talebi bulunamadı.");
            if (purchaseRequest.Status != PurchaseRequestStatus.Approved)
                return Conflict("Yalnızca onaylı satın alma taleplerinden sipariş oluşturulabilir.");
        }

        var entity = new PurchaseOrder
        {
            CompanyId = request.CompanyId,
            ProjectId = request.ProjectId,
            SupplierCurrentAccountId = request.SupplierCurrentAccountId,
            PurchaseRequestId = request.PurchaseRequestId,
            OrderNumber = request.OrderNumber.Trim(),
            DeliveryDateUtc = request.DeliveryDateUtc,
            CurrencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode)
                ? "TRY"
                : request.CurrencyCode.Trim().ToUpperInvariant(),
            ExchangeRate = request.ExchangeRate,
            VatRate = request.VatRate,
            Description = request.Description?.Trim(),
            Status = PurchaseOrderStatus.Draft,
            Items = request.Items.Select(x => new PurchaseOrderItem
            {
                MaterialId = x.MaterialId,
                Quantity = x.Quantity,
                Unit = string.IsNullOrWhiteSpace(x.Unit) ? "Adet" : x.Unit.Trim(),
                UnitPrice = x.UnitPrice,
                DiscountRate = x.DiscountRate,
                Description = x.Description?.Trim()
            }).ToList()
        };

        db.PurchaseOrders.Add(entity);

        if (request.PurchaseRequestId.HasValue)
        {
            var purchaseRequest = await db.PurchaseRequests.FirstAsync(
                x => x.Id == request.PurchaseRequestId.Value,
                cancellationToken);
            purchaseRequest.Status = PurchaseRequestStatus.ConvertedToOrder;
            purchaseRequest.UpdatedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = entity.Id }, new
        {
            entity.Id,
            entity.OrderNumber,
            entity.Status
        });
    }

    [HttpPost("{id:guid}/submit")]
    public async Task<ActionResult> Submit(Guid id, CancellationToken cancellationToken)
    {
        var entity = await db.PurchaseOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
            return NotFound();
        if (entity.Status != PurchaseOrderStatus.Draft)
            return Conflict("Yalnızca taslak siparişler onaya gönderilebilir.");
        if (entity.Items.Count == 0)
            return Conflict("Kalemsiz sipariş onaya gönderilemez.");

        entity.Status = PurchaseOrderStatus.PendingApproval;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { entity.Id, entity.Status });
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<ActionResult> Approve(Guid id, CancellationToken cancellationToken)
    {
        var entity = await db.PurchaseOrders.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
            return NotFound();
        if (entity.Status != PurchaseOrderStatus.PendingApproval)
            return Conflict("Yalnızca onay bekleyen siparişler onaylanabilir.");

        entity.Status = PurchaseOrderStatus.Approved;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { entity.Id, entity.Status });
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var entity = await db.PurchaseOrders.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
            return NotFound();
        if (entity.Status is PurchaseOrderStatus.Completed or PurchaseOrderStatus.Cancelled)
            return Conflict("Tamamlanmış veya iptal edilmiş sipariş tekrar iptal edilemez.");

        entity.Status = PurchaseOrderStatus.Cancelled;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { entity.Id, entity.Status });
    }
}
