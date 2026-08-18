using EnderunAI.Api.Contracts.Core;
using EnderunAI.Api.Data;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/daily-reports")]
public sealed class ProjectDailyReportsRollupController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.SiteReportsView)]
    public async Task<IActionResult> GetRecent(
        Guid projectId,
        [FromQuery] int take,
        CancellationToken cancellationToken)
    {
        var limit = take > 0 && take <= 100 ? take : 10;

        var matches = db.ProjectSiteDailyReports.AsNoTracking()
            .Where(x => x.ProjectSite.ProjectId == projectId);

        // "Son N rapor" gösterilirken projede kaç rapor olduğu da
        // söylenir; yoksa 10 rapor gören kullanıcı projenin tamamının
        // 10 rapordan ibaret olduğunu sanıyor.
        var total = await matches.CountAsync(cancellationToken);

        var items = await matches
            .OrderByDescending(x => x.ReportDate)
            .Take(limit)
            .Select(x => new
            {
                x.Id,
                x.ProjectSiteId,
                SiteName = x.ProjectSite.Name,
                x.ReportDate,
                x.WeatherCondition,
                TotalHeadcount = x.EngineerCount + x.ForemanCount + x.CraftsmanCount + x.WorkerCount + x.OtherCount,
                x.Notes
            })
            .ToListAsync(cancellationToken);

        return Ok(PagedResult<object>.From(items, total, limit));
    }
}
