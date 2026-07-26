using System.Security.Claims;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Procurement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Route("api/project-budgets")]
[Authorize]
public sealed class ProjectBudgetsController(
    ProjectBudgetDbContext db,
    IProjectBudgetService service) : ControllerBase
{
    public sealed record BudgetItemInput(
        string Code,
        string Name,
        Guid? MaterialId,
        string? Category,
        decimal PlannedAmount,
        string CurrencyCode,
        int SequenceNo);

    public sealed record CreateBudgetRequest(
        Guid CompanyId,
        Guid ProjectId,
        string BudgetNumber,
        string Name,
        string CurrencyCode,
        decimal BaseAmount,
        decimal WarningThresholdPercent,
        decimal CriticalThresholdPercent,
        DateTime? EffectiveDateUtc,
        string? Description,
        IReadOnlyList<BudgetItemInput> Items);

    public sealed record RevisionRequest(decimal RevisedAmount, string Reason);

    [HttpGet]
    public async Task<ActionResult> List(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] BudgetStatus? status,
        CancellationToken cancellationToken)
    {
        var query = db.Budgets.AsNoTracking().Include(x => x.Items).AsQueryable();
        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);
        if (projectId.HasValue)
            query = query.Where(x => x.ProjectId == projectId.Value);
        if (status.HasValue)
            query = query.Where(x => x.Status == status.Value);

        var result = await query
            .OrderByDescending(x => x.EffectiveDateUtc)
            .ToListAsync(cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var entity = await db.Budgets
            .AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Revisions)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return entity is null ? NotFound() : Ok(entity);
    }

    [HttpPost]
    public async Task<ActionResult> Create(CreateBudgetRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.BudgetNumber) || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Bütçe numarası ve adı zorunludur.");
        if (request.BaseAmount <= 0m)
            return BadRequest("Bütçe tutarı sıfırdan büyük olmalıdır.");
        if (request.WarningThresholdPercent <= 0m ||
            request.CriticalThresholdPercent < request.WarningThresholdPercent)
            return BadRequest("Uyarı ve kritik eşikleri geçersizdir.");
        if (request.Items.Any(x => x.PlannedAmount < 0m || string.IsNullOrWhiteSpace(x.Code)))
            return BadRequest("Bütçe kalemleri geçersizdir.");

        var duplicate = await db.Budgets.AnyAsync(
            x => x.CompanyId == request.CompanyId &&
                 x.ProjectId == request.ProjectId &&
                 x.BudgetNumber == request.BudgetNumber,
            cancellationToken);
        if (duplicate)
            return Conflict("Bu bütçe numarası daha önce kullanılmış.");

        var entity = new ProjectBudget
        {
            CompanyId = request.CompanyId,
            ProjectId = request.ProjectId,
            BudgetNumber = request.BudgetNumber.Trim(),
            Name = request.Name.Trim(),
            CurrencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode)
                ? "TRY"
                : request.CurrencyCode.Trim().ToUpperInvariant(),
            BaseAmount = request.BaseAmount,
            WarningThresholdPercent = request.WarningThresholdPercent,
            CriticalThresholdPercent = request.CriticalThresholdPercent,
            EffectiveDateUtc = request.EffectiveDateUtc ?? DateTime.UtcNow,
            Description = request.Description?.Trim(),
            Status = BudgetStatus.Draft,
            Items = request.Items.Select(x => new ProjectBudgetItem
            {
                Code = x.Code.Trim(),
                Name = x.Name.Trim(),
                MaterialId = x.MaterialId,
                Category = x.Category?.Trim(),
                PlannedAmount = x.PlannedAmount,
                CurrencyCode = string.IsNullOrWhiteSpace(x.CurrencyCode)
                    ? "TRY"
                    : x.CurrencyCode.Trim().ToUpperInvariant(),
                SequenceNo = x.SequenceNo
            }).ToList()
        };

        db.Budgets.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = entity.Id }, entity);
    }

    [HttpPost("{id:guid}/activate")]
    public async Task<ActionResult> Activate(Guid id, CancellationToken cancellationToken)
    {
        var entity = await db.Budgets.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
            return NotFound();
        if (entity.Status != BudgetStatus.Draft)
            return Conflict("Yalnızca taslak bütçe aktifleştirilebilir.");

        var activeBudgets = await db.Budgets
            .Where(x => x.ProjectId == entity.ProjectId && x.Status == BudgetStatus.Active && x.Id != id)
            .ToListAsync(cancellationToken);
        foreach (var active in activeBudgets)
        {
            active.Status = BudgetStatus.Closed;
            active.UpdatedAtUtc = DateTime.UtcNow;
        }

        entity.Status = BudgetStatus.Active;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { entity.Id, entity.Status });
    }

    [HttpPost("{id:guid}/revisions")]
    public async Task<ActionResult> Revise(Guid id, RevisionRequest request, CancellationToken cancellationToken)
    {
        if (request.RevisedAmount <= 0m || string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest("Revize tutar ve gerekçe zorunludur.");

        var entity = await db.Budgets
            .Include(x => x.Revisions)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
            return NotFound();
        if (entity.Status is BudgetStatus.Closed or BudgetStatus.Cancelled)
            return Conflict("Kapalı veya iptal edilmiş bütçe revize edilemez.");

        var revisionNumber = entity.Revisions.Count == 0
            ? 1
            : entity.Revisions.Max(x => x.RevisionNumber) + 1;
        entity.Revisions.Add(new ProjectBudgetRevision
        {
            RevisionNumber = revisionNumber,
            PreviousAmount = entity.BaseAmount,
            RevisedAmount = request.RevisedAmount,
            Reason = request.Reason.Trim(),
            CreatedByUserId = GetUserId(),
            CreatedByName = User.Identity?.Name ?? "Bilinmeyen kullanıcı"
        });
        entity.BaseAmount = request.RevisedAmount;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { entity.Id, entity.BaseAmount, RevisionNumber = revisionNumber });
    }

    [HttpGet("projects/{projectId:guid}/summary")]
    public async Task<ActionResult> ProjectSummary(Guid projectId, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.CheckProjectAsync(projectId, 0m, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
    }

    [HttpGet("purchase-orders/{purchaseOrderId:guid}/check")]
    public async Task<ActionResult> CheckPurchaseOrder(Guid purchaseOrderId, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.CheckPurchaseOrderAsync(purchaseOrderId, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    [HttpGet("alerts")]
    public async Task<ActionResult> Alerts(
        [FromQuery] Guid? projectId,
        [FromQuery] bool unresolvedOnly = true,
        CancellationToken cancellationToken = default)
    {
        var query = db.Alerts.AsNoTracking().AsQueryable();
        if (projectId.HasValue)
            query = query.Where(x => x.ProjectId == projectId.Value);
        if (unresolvedOnly)
            query = query.Where(x => !x.IsResolved);

        return Ok(await query.OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken));
    }

    private Guid? GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var parsed) ? parsed : null;
    }
}
