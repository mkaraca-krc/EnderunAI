using System.Security.Claims;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Route("api/purchase-requests")]
[Authorize]
public sealed class PurchaseRequestsController(AppDbContext db) : ControllerBase
{
    public sealed record PurchaseRequestItemInput(Guid MaterialId, decimal Quantity, string Unit, string? Description);
    public sealed record CreatePurchaseRequestRequest(Guid CompanyId, Guid ProjectId, string RequestNumber, DateTime? RequiredDateUtc, string? Description, Guid? RequestedByUserId, IReadOnlyList<PurchaseRequestItemInput> Items);
    public sealed record RejectPurchaseRequestRequest(string Reason);

    [HttpGet]
    public async Task<ActionResult> List([FromQuery] Guid? companyId, [FromQuery] Guid? projectId, [FromQuery] PurchaseRequestStatus? status, CancellationToken cancellationToken)
    {
        var query = db.PurchaseRequests.AsNoTracking().Include(x => x.Items).ThenInclude(x => x.Material).AsQueryable();
        if (companyId.HasValue) query = query.Where(x => x.CompanyId == companyId.Value);
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId.Value);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);

        var result = await query.OrderByDescending(x => x.RequestDateUtc).Select(x => new
        {
            x.Id, x.CompanyId, x.ProjectId, x.RequestNumber, x.RequestDateUtc, x.RequiredDateUtc, x.Status,
            x.Description, x.RequestedByUserId, ItemCount = x.Items.Count, TotalQuantity = x.Items.Sum(i => i.Quantity)
        }).ToListAsync(cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var entity = await db.PurchaseRequests.AsNoTracking().Include(x => x.Items).ThenInclude(x => x.Material).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return entity is null ? NotFound() : Ok(entity);
    }

    [HttpPost]
    public async Task<ActionResult> Create(CreatePurchaseRequestRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RequestNumber)) return BadRequest("Talep numarası zorunludur.");
        if (request.Items.Count == 0) return BadRequest("En az bir talep kalemi eklenmelidir.");
        if (request.Items.Any(x => x.Quantity <= 0)) return BadRequest("Talep miktarları sıfırdan büyük olmalıdır.");

        var duplicate = await db.PurchaseRequests.AnyAsync(x => x.CompanyId == request.CompanyId && x.RequestNumber == request.RequestNumber, cancellationToken);
        if (duplicate) return Conflict("Bu talep numarası daha önce kullanılmış.");

        var materialIds = request.Items.Select(x => x.MaterialId).Distinct().ToList();
        var existingMaterialCount = await db.Materials.CountAsync(x => materialIds.Contains(x.Id), cancellationToken);
        if (existingMaterialCount != materialIds.Count) return BadRequest("Talep kalemlerinden en az biri geçerli bir malzeme kartına bağlı değil.");

        var requesterId = ResolveRequesterId(request.RequestedByUserId);
        if (!requesterId.HasValue) return BadRequest("Talep eden kullanıcı kimliği belirlenemedi.");

        var entity = new PurchaseRequest
        {
            CompanyId = request.CompanyId,
            ProjectId = request.ProjectId,
            RequestNumber = request.RequestNumber.Trim(),
            RequiredDateUtc = request.RequiredDateUtc,
            Description = request.Description?.Trim(),
            RequestedByUserId = requesterId.Value,
            Status = PurchaseRequestStatus.Draft,
            Items = request.Items.Select(x => new PurchaseRequestItem
            {
                MaterialId = x.MaterialId,
                Quantity = x.Quantity,
                Unit = string.IsNullOrWhiteSpace(x.Unit) ? "Adet" : x.Unit.Trim(),
                Description = x.Description?.Trim()
            }).ToList()
        };

        db.PurchaseRequests.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = entity.Id }, new { entity.Id, entity.RequestNumber, entity.Status });
    }

    [HttpPost("{id:guid}/submit")]
    public async Task<ActionResult> Submit(Guid id, CancellationToken cancellationToken)
    {
        var entity = await db.PurchaseRequests.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null) return NotFound();
        if (entity.Status != PurchaseRequestStatus.Draft) return Conflict("Yalnızca taslak talepler onaya gönderilebilir.");
        if (entity.Items.Count == 0) return Conflict("Kalemsiz talep onaya gönderilemez.");
        entity.Status = PurchaseRequestStatus.PendingApproval;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { entity.Id, entity.Status });
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<ActionResult> Approve(Guid id, CancellationToken cancellationToken)
    {
        var entity = await db.PurchaseRequests.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null) return NotFound();
        if (entity.Status != PurchaseRequestStatus.PendingApproval) return Conflict("Yalnızca onay bekleyen talepler onaylanabilir.");
        entity.Status = PurchaseRequestStatus.Approved;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { entity.Id, entity.Status });
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<ActionResult> Reject(Guid id, RejectPurchaseRequestRequest request, CancellationToken cancellationToken)
    {
        var entity = await db.PurchaseRequests.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null) return NotFound();
        if (entity.Status != PurchaseRequestStatus.PendingApproval) return Conflict("Yalnızca onay bekleyen talepler reddedilebilir.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return BadRequest("Red nedeni zorunludur.");
        entity.Status = PurchaseRequestStatus.Rejected;
        entity.Description = string.IsNullOrWhiteSpace(entity.Description) ? $"Red nedeni: {request.Reason.Trim()}" : $"{entity.Description}\nRed nedeni: {request.Reason.Trim()}";
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { entity.Id, entity.Status });
    }

    private Guid? ResolveRequesterId(Guid? requestedByUserId)
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (Guid.TryParse(raw, out var authenticatedUserId)) return authenticatedUserId;
        return requestedByUserId is { } fallback && fallback != Guid.Empty ? fallback : null;
    }
}
