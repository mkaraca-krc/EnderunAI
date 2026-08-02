using EnderunAI.Api.Data;
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
    public async Task<IActionResult> GetRecent(
        Guid projectId,
        [FromQuery] int take,
        CancellationToken cancellationToken)
    {
        var limit = take > 0 && take <= 100 ? take : 10;

        var items = await db.ProjectSiteDailyReports.AsNoTracking()
            .Where(x => x.ProjectSite.ProjectId == projectId)
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

        return Ok(items);
    }
}
