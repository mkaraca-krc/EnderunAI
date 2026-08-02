using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security.CurrentUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/project-boqs")]
public sealed class ProjectBoqController(
    AppDbContext db,
    ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] ProjectBoqStatus? status,
        CancellationToken cancellationToken)
    {
        var query = db.ProjectBoqs.AsNoTracking().AsQueryable();

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        if (projectId.HasValue)
            query = query.Where(x => x.ProjectId == projectId.Value);

        if (status.HasValue)
            query = query.Where(x => x.Status == status.Value);

        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                x.ProjectId,
                x.Project.Code,
                ProjectName = x.Project.Name,
                x.BoqNumber,
                x.Name,
                x.RevisionNumber,
                x.Status,
                x.IsCurrentRevision,
                x.CurrencyCode,
                x.TotalAmount,
                x.CreatedAtUtc,
                ItemCount = x.Items.Count
            })
            .ToListAsync(cancellationToken);

        return Ok(items.Select(x => new
        {
            x.Id,
            x.CompanyId,
            x.ProjectId,
            ProjectCode = x.Code,
            x.ProjectName,
            x.BoqNumber,
            x.Name,
            x.RevisionNumber,
            RevisionCode = $"R{x.RevisionNumber}",
            x.Status,
            x.IsCurrentRevision,
            x.CurrencyCode,
            x.TotalAmount,
            x.ItemCount,
            x.CreatedAtUtc
        }));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var item = await db.ProjectBoqs
            .AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Project)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (item is null)
            return NotFound(new { message = "Metraj (BOQ) kaydı bulunamadı." });

        return Ok(new
        {
            item.Id,
            item.CompanyId,
            item.ProjectId,
            ProjectCode = item.Project.Code,
            ProjectName = item.Project.Name,
            item.BoqNumber,
            item.Name,
            item.RevisionNumber,
            RevisionCode = $"R{item.RevisionNumber}",
            item.Status,
            item.IsCurrentRevision,
            item.CurrencyCode,
            item.TotalAmount,
            ItemCount = item.Items.Count,
            item.CreatedAtUtc,
            item.Description,
            item.Notes,
            item.ApprovedAtUtc,
            Items = item.Items
                .OrderBy(x => x.LineNumber)
                .Select(x => new
                {
                    x.Id,
                    x.EngineeringPositionId,
                    x.LineNumber,
                    x.PositionCode,
                    x.Description,
                    x.Unit,
                    x.ContractQuantity,
                    x.UnitPrice,
                    x.TotalAmount,
                    x.ItemType,
                    x.Category,
                    x.Notes
                })
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        CreateProjectBoqRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            return BadRequest(new
            {
                message = "Metraj en az bir kalem içermelidir."
            });
        }

        var duplicate = await db.ProjectBoqs.AnyAsync(
            x => x.CompanyId == request.CompanyId &&
                 x.BoqNumber == request.BoqNumber &&
                 x.RevisionNumber == request.RevisionNumber,
            cancellationToken);
        if (duplicate)
        {
            return Conflict(new
            {
                message = "Bu numara ve revizyon için metraj zaten mevcut."
            });
        }

        var items = request.Items.Select((line, index) =>
        {
            var totalAmount = line.ContractQuantity * line.UnitPrice;
            return new ProjectBoqItem
            {
                EngineeringPositionId = line.EngineeringPositionId,
                LineNumber = index + 1,
                PositionCode = line.PositionCode,
                Description = line.Description,
                Unit = line.Unit,
                ContractQuantity = line.ContractQuantity,
                UnitPrice = line.UnitPrice,
                TotalAmount = totalAmount,
                ItemType = line.ItemType,
                Category = line.Category,
                Notes = line.Notes
            };
        }).ToList();

        var boq = new ProjectBoq
        {
            CompanyId = request.CompanyId,
            ProjectId = request.ProjectId,
            BoqNumber = request.BoqNumber,
            Name = request.Name,
            RevisionNumber = request.RevisionNumber,
            Status = ProjectBoqStatus.Draft,
            IsCurrentRevision = true,
            CurrencyCode = request.CurrencyCode,
            TotalAmount = items.Sum(x => x.TotalAmount),
            Description = request.Description,
            Notes = request.Notes,
            Items = items
        };

        db.ProjectBoqs.Add(boq);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            boq.Id,
            boq.BoqNumber,
            boq.RevisionNumber,
            boq.Status,
            boq.TotalAmount
        });
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(
        Guid id,
        CancellationToken cancellationToken)
    {
        var boq = await db.ProjectBoqs.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (boq is null)
            return NotFound(new { message = "Metraj (BOQ) kaydı bulunamadı." });

        if (boq.Status == ProjectBoqStatus.Approved)
        {
            return Conflict(new
            {
                message = "Metraj zaten onaylanmış."
            });
        }

        boq.Status = ProjectBoqStatus.Approved;
        boq.ApprovedAtUtc = DateTime.UtcNow;
        boq.ApprovedByUserId = currentUser.UserId;
        boq.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            boq.Id,
            boq.BoqNumber,
            boq.RevisionNumber,
            boq.Status,
            message = "Metraj onaylandı."
        });
    }

    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> Archive(
        Guid id,
        CancellationToken cancellationToken)
    {
        var boq = await db.ProjectBoqs.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (boq is null)
            return NotFound(new { message = "Metraj (BOQ) kaydı bulunamadı." });

        boq.Status = ProjectBoqStatus.Archived;
        boq.IsCurrentRevision = false;
        boq.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            boq.Id,
            boq.BoqNumber,
            boq.RevisionNumber,
            boq.Status,
            message = "Metraj arşivlendi."
        });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        var boq = await db.ProjectBoqs.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (boq is null)
            return NotFound(new { message = "Metraj (BOQ) kaydı bulunamadı." });

        if (boq.Status != ProjectBoqStatus.Draft)
        {
            return Conflict(new
            {
                message = "Yalnızca taslak durumundaki metrajlar silinebilir."
            });
        }

        boq.IsActive = false;
        boq.IsDeleted = true;
        boq.DeletedAtUtc = DateTime.UtcNow;
        boq.DeletedByUserId = currentUser.UserId;

        await db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }
}

public sealed record ProjectBoqItemRequest(
    Guid? EngineeringPositionId,
    string PositionCode,
    string Description,
    string Unit,
    decimal ContractQuantity,
    decimal UnitPrice,
    ProjectBoqItemType ItemType,
    string? Category,
    string? Notes);

public sealed record CreateProjectBoqRequest(
    Guid CompanyId,
    Guid ProjectId,
    string BoqNumber,
    string Name,
    int RevisionNumber,
    string CurrencyCode,
    string? Description,
    string? Notes,
    List<ProjectBoqItemRequest> Items);
