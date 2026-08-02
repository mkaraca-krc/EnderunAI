using EnderunAI.Api.Contracts.ProjectSites;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}")]
public sealed class HrProjectLaborCostsController(AppDbContext db) : ControllerBase
{
    [HttpGet("labor-costs")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelView)]
    public async Task<IActionResult> GetAll(
        Guid projectId,
        [FromQuery] Guid? siteId,
        CancellationToken cancellationToken)
    {
        var projectExists = await db.Projects.AsNoTracking()
            .AnyAsync(x => x.Id == projectId, cancellationToken);

        if (!projectExists)
            return NotFound(new { message = "Proje bulunamadı." });

        var query = db.HrProjectLaborCosts
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId);

        if (siteId.HasValue)
            query = query.Where(x => x.ProjectSiteId == siteId.Value);

        var rows = await query
            .OrderByDescending(x => x.WorkDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.ProjectId,
                x.PersonnelId,
                x.ProjectSiteId,
                SiteCode = x.ProjectSite != null ? x.ProjectSite.Code : null,
                SiteName = x.ProjectSite != null ? x.ProjectSite.Name : null,
                x.WorkDate,
                x.NormalHours,
                x.OvertimeHours,
                x.NormalCost,
                x.OvertimeCost,
                x.OtherCost,
                x.TotalLaborCost,
                x.CurrencyCode
            })
            .ToListAsync(cancellationToken);

        var personnelIds = rows.Select(x => x.PersonnelId).Distinct().ToArray();
        var personnelNames = await db.Personnel.AsNoTracking()
            .Where(x => personnelIds.Contains(x.Id))
            .ToDictionaryAsync(
                x => x.Id,
                x => x.FirstName + " " + x.LastName,
                cancellationToken);

        return Ok(rows.Select(x => new
        {
            x.Id,
            x.ProjectId,
            x.PersonnelId,
            PersonnelName = personnelNames.GetValueOrDefault(x.PersonnelId, "—"),
            x.ProjectSiteId,
            x.SiteCode,
            x.SiteName,
            x.WorkDate,
            x.NormalHours,
            x.OvertimeHours,
            x.NormalCost,
            x.OvertimeCost,
            x.OtherCost,
            x.TotalLaborCost,
            x.CurrencyCode
        }));
    }

    [HttpPost("labor-costs")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelCreate)]
    public async Task<IActionResult> Create(
        Guid projectId,
        CreateHrProjectLaborCostRequest request,
        CancellationToken cancellationToken)
    {
        var project = await db.Projects.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == projectId, cancellationToken);

        if (project is null)
            return NotFound(new { message = "Proje bulunamadı." });

        var personnel = await db.Personnel.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.PersonnelId, cancellationToken);

        if (personnel is null)
            return NotFound(new { message = "Personel bulunamadı." });

        if (request.ProjectSiteId.HasValue)
        {
            var siteBelongsToProject = await db.ProjectSites.AsNoTracking().AnyAsync(
                x => x.Id == request.ProjectSiteId.Value && x.ProjectId == projectId,
                cancellationToken);

            if (!siteBelongsToProject)
            {
                return BadRequest(new
                {
                    message = "Seçilen şantiye bu projeye ait değil."
                });
            }
        }

        var totalLaborCost = request.NormalCost + request.OvertimeCost + request.OtherCost;

        var item = new HrProjectLaborCost
        {
            CompanyId = personnel.CompanyId,
            ProjectId = projectId,
            PersonnelId = request.PersonnelId,
            ProjectSiteId = request.ProjectSiteId,
            WorkDate = DateTime.SpecifyKind(request.WorkDate.Date, DateTimeKind.Utc),
            NormalHours = request.NormalHours,
            OvertimeHours = request.OvertimeHours,
            NormalCost = request.NormalCost,
            OvertimeCost = request.OvertimeCost,
            OtherCost = request.OtherCost,
            TotalLaborCost = totalLaborCost,
            CurrencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode)
                ? "TRY"
                : request.CurrencyCode.Trim().ToUpperInvariant()
        };

        db.HrProjectLaborCosts.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Personel maliyet kaydı oluşturuldu.",
            item.Id,
            item.TotalLaborCost
        });
    }

    [HttpGet("labor-cost-breakdown")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelView)]
    public async Task<IActionResult> GetBreakdown(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var projectExists = await db.Projects.AsNoTracking()
            .AnyAsync(x => x.Id == projectId, cancellationToken);

        if (!projectExists)
            return NotFound(new { message = "Proje bulunamadı." });

        var rows = await db.HrProjectLaborCosts
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .Select(x => new { x.ProjectSiteId, x.TotalLaborCost })
            .ToListAsync(cancellationToken);

        var sites = await db.ProjectSites
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderBy(x => x.Code)
            .Select(x => new { x.Id, x.Code, x.Name })
            .ToListAsync(cancellationToken);

        var siteBreakdown = sites.Select(site => new
        {
            site.Id,
            site.Code,
            site.Name,
            Amount = rows
                .Where(x => x.ProjectSiteId == site.Id)
                .Sum(x => x.TotalLaborCost)
        }).ToList();

        var sharedCost = rows
            .Where(x => x.ProjectSiteId == null)
            .Sum(x => x.TotalLaborCost);

        var projectTotal = rows.Sum(x => x.TotalLaborCost);

        return Ok(new
        {
            projectId,
            sites = siteBreakdown,
            sharedCost,
            projectTotal
        });
    }
}
