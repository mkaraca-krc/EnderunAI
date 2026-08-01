using EnderunAI.Api.Contracts.ProjectSites;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/sites")]
public sealed class ProjectSitesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var projectExists = await db.Projects.AsNoTracking()
            .AnyAsync(x => x.Id == projectId, cancellationToken);

        if (!projectExists)
            return NotFound(new { message = "Proje bulunamadı." });

        var items = await db.ProjectSites
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderBy(x => x.Code)
            .Select(x => new
            {
                x.Id,
                x.ProjectId,
                x.Code,
                x.Name,
                x.Location,
                x.Notes,
                x.IsActive,
                x.CreatedAtUtc,
                AssignmentCount = x.Assignments
                    .Count(a => a.IsActive && !a.IsDeleted && a.EndDate == null),
                WarehouseCount = x.Warehouses.Count(w => !w.IsDeleted)
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        Guid projectId,
        CreateProjectSiteRequest request,
        CancellationToken cancellationToken)
    {
        var projectExists = await db.Projects.AsNoTracking()
            .AnyAsync(x => x.Id == projectId, cancellationToken);

        if (!projectExists)
            return NotFound(new { message = "Proje bulunamadı." });

        if (string.IsNullOrWhiteSpace(request.Code) ||
            string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Şantiye kodu ve adı zorunludur." });
        }

        var code = request.Code.Trim().ToUpperInvariant();

        var duplicate = await db.ProjectSites.AnyAsync(
            x => x.ProjectId == projectId && x.Code == code,
            cancellationToken);

        if (duplicate)
        {
            return Conflict(new
            {
                message = "Bu proje için aynı kodda bir şantiye zaten var."
            });
        }

        var site = new ProjectSite
        {
            ProjectId = projectId,
            Code = code,
            Name = request.Name.Trim(),
            Location = request.Location?.Trim(),
            Notes = request.Notes?.Trim()
        };

        db.ProjectSites.Add(site);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Şantiye oluşturuldu.",
            site.Id,
            site.Code,
            site.Name
        });
    }
}

[ApiController]
[Authorize]
[Route("api/project-sites")]
public sealed class ProjectSiteController(AppDbContext db) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var site = await db.ProjectSites
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.ProjectId,
                ProjectCode = x.Project.Code,
                ProjectName = x.Project.Name,
                x.Code,
                x.Name,
                x.Location,
                x.Notes,
                x.IsActive,
                x.CreatedAtUtc,
                Assignments = x.Assignments
                    .OrderByDescending(a => a.IsActive)
                    .ThenByDescending(a => a.StartDate)
                    .Select(a => new
                    {
                        a.Id,
                        a.PersonnelId,
                        a.Personnel.EmployeeNumber,
                        FullName = a.Personnel.FirstName + " " + a.Personnel.LastName,
                        a.Personnel.JobTitle,
                        a.Role,
                        a.Notes,
                        a.StartDate,
                        a.EndDate,
                        a.IsActive
                    }),
                Warehouses = x.Warehouses
                    .Where(w => !w.IsDeleted)
                    .Select(w => new
                    {
                        w.Id,
                        w.Code,
                        w.Name
                    })
            })
            .SingleOrDefaultAsync(cancellationToken);

        return site is null
            ? NotFound(new { message = "Şantiye bulunamadı." })
            : Ok(site);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateProjectSiteRequest request,
        CancellationToken cancellationToken)
    {
        var site = await db.ProjectSites
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (site is null)
            return NotFound(new { message = "Şantiye bulunamadı." });

        if (string.IsNullOrWhiteSpace(request.Code) ||
            string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Şantiye kodu ve adı zorunludur." });
        }

        var code = request.Code.Trim().ToUpperInvariant();

        var duplicate = await db.ProjectSites.AnyAsync(
            x => x.Id != id && x.ProjectId == site.ProjectId && x.Code == code,
            cancellationToken);

        if (duplicate)
        {
            return Conflict(new
            {
                message = "Bu proje için aynı kodda bir şantiye zaten var."
            });
        }

        site.Code = code;
        site.Name = request.Name.Trim();
        site.Location = request.Location?.Trim();
        site.Notes = request.Notes?.Trim();
        site.IsActive = request.IsActive;
        site.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Şantiye güncellendi." });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        var site = await db.ProjectSites
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (site is null)
            return NotFound(new { message = "Şantiye bulunamadı." });

        var hasActiveAssignments = await db.ProjectSiteAssignments.AnyAsync(
            x => x.ProjectSiteId == id && x.IsActive && x.EndDate == null,
            cancellationToken);

        var hasWarehouses = await db.Warehouses.AnyAsync(
            x => x.ProjectSiteId == id, cancellationToken);

        if (hasActiveAssignments || hasWarehouses)
        {
            return Conflict(new
            {
                message = "Aktif personel ataması veya deposu olan şantiye silinemez."
            });
        }

        site.IsActive = false;
        site.IsDeleted = true;
        site.DeletedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpGet("{id:guid}/assignable-personnel")]
    public async Task<IActionResult> GetAssignablePersonnel(
        Guid id,
        CancellationToken cancellationToken)
    {
        var site = await db.ProjectSites
            .AsNoTracking()
            .Include(x => x.Project)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (site is null)
            return NotFound(new { message = "Şantiye bulunamadı." });

        var items = await db.Personnel
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == site.Project.CompanyId &&
                x.IsActive &&
                !x.SiteAssignments.Any(a => a.IsActive && a.EndDate == null))
            .OrderBy(x => x.FirstName)
            .ThenBy(x => x.LastName)
            .Select(x => new
            {
                x.Id,
                x.EmployeeNumber,
                FullName = x.FirstName + " " + x.LastName,
                x.JobTitle,
                x.Profession
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpPost("{id:guid}/assignments")]
    public async Task<IActionResult> AssignPersonnel(
        Guid id,
        AssignPersonnelToSiteRequest request,
        CancellationToken cancellationToken)
    {
        var site = await db.ProjectSites
            .Include(x => x.Project)
            .SingleOrDefaultAsync(x => x.Id == id && x.IsActive, cancellationToken);

        if (site is null)
            return NotFound(new { message = "Aktif şantiye bulunamadı." });

        var personnel = await db.Personnel
            .SingleOrDefaultAsync(
                x => x.Id == request.PersonnelId && x.IsActive,
                cancellationToken);

        if (personnel is null)
            return NotFound(new { message = "Aktif personel bulunamadı." });

        if (personnel.CompanyId != site.Project.CompanyId)
        {
            return BadRequest(new
            {
                message = "Personel ve şantiye aynı şirkete ait olmalıdır."
            });
        }

        var hasActiveAssignment = await db.ProjectSiteAssignments.AnyAsync(
            x => x.PersonnelId == request.PersonnelId &&
                 x.IsActive &&
                 x.EndDate == null,
            cancellationToken);

        if (hasActiveAssignment)
        {
            return Conflict(new
            {
                message = "Personelin zaten aktif bir şantiye ataması var."
            });
        }

        var assignment = new ProjectSiteAssignment
        {
            PersonnelId = request.PersonnelId,
            ProjectSiteId = id,
            StartDate = UtcDate(request.StartDate),
            EndDate = UtcDate(request.EndDate),
            Role = request.Role?.Trim(),
            Notes = request.Notes?.Trim()
        };

        db.ProjectSiteAssignments.Add(assignment);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Personel şantiyeye atandı.",
            assignment.Id
        });
    }

    [HttpPut("assignments/{assignmentId:guid}/close")]
    public async Task<IActionResult> CloseAssignment(
        Guid assignmentId,
        [FromQuery] DateTime? endDate,
        CancellationToken cancellationToken)
    {
        var assignment = await db.ProjectSiteAssignments
            .SingleOrDefaultAsync(x => x.Id == assignmentId, cancellationToken);

        if (assignment is null)
            return NotFound(new { message = "Şantiye görevlendirmesi bulunamadı." });

        assignment.EndDate = endDate.HasValue
            ? UtcDate(endDate.Value)
            : UtcDate(DateTime.UtcNow);
        assignment.IsActive = false;
        assignment.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Şantiye görevlendirmesi kapatıldı." });
    }

    private static DateTime UtcDate(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);

    private static DateTime? UtcDate(DateTime? value) =>
        value.HasValue ? UtcDate(value.Value) : null;
}
