using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Şantiye bazlı günlük rapor onay kuyruğu — Teknik Koordinatör gibi
/// birden fazla (veya tüm) şantiyeye erişimi olan kullanıcıların, tek
/// tek şantiye açmadan onay bekleyen raporları görebilmesi için.
/// </summary>
[ApiController]
[Authorize]
[Route("api/site-reports")]
public sealed class SiteReportApprovalsController(
    AppDbContext db,
    ICurrentDataScopeService dataScope) : ControllerBase
{
    [HttpGet("pending-approval")]
    [RequirePermission(PermissionCatalog.Keys.SiteReportsApprove)]
    public async Task<IActionResult> GetPendingApproval(
        CancellationToken cancellationToken)
    {
        var scope = await dataScope.GetAsync(cancellationToken);
        if (scope is null)
            return Ok(Array.Empty<object>());

        var query = db.ProjectSiteDailyReports
            .AsNoTracking()
            .Where(x => x.Status == ProjectSiteDailyReportStatus.Draft);

        if (!scope.HasGlobalAccess)
        {
            query = query.Where(x =>
                scope.CompanyIds.Contains(x.ProjectSite.Project.CompanyId) ||
                scope.BranchIds.Contains(x.ProjectSite.Project.BranchId) ||
                scope.ProjectIds.Contains(x.ProjectSite.ProjectId) ||
                scope.SiteIds.Contains(x.ProjectSiteId));
        }

        var items = await query
            .OrderByDescending(x => x.ReportDate)
            .Select(x => new
            {
                x.Id,
                x.ProjectSiteId,
                ProjectId = x.ProjectSite.ProjectId,
                SiteCode = x.ProjectSite.Code,
                SiteName = x.ProjectSite.Name,
                ProjectCode = x.ProjectSite.Project.Code,
                ProjectName = x.ProjectSite.Project.Name,
                x.ReportDate,
                TotalHeadcount = x.EngineerCount + x.ForemanCount + x.CraftsmanCount + x.WorkerCount + x.OtherCount,
                x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }
}
