using EnderunAI.Api.Data;
using EnderunAI.Api.Services.Upload;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[AllowAnonymous]
[EnableRateLimiting("portal")]
[Route("api/portal/{token}")]
public sealed class PortalController(
    AppDbContext db,
    IUploadService uploadService) : ControllerBase
{
    private const string PhotoCategory = "site-daily-reports";

    [HttpGet]
    public async Task<IActionResult> GetProject(string token, CancellationToken cancellationToken)
    {
        var link = await ResolveActiveLink(token, cancellationToken);
        if (link is null)
            return NotFound();

        var project = await db.Projects.AsNoTracking()
            .Where(x => x.Id == link.ProjectId)
            .Select(x => new { x.Name, x.Code })
            .SingleOrDefaultAsync(cancellationToken);

        if (project is null)
            return NotFound();

        var sites = await db.ProjectSites.AsNoTracking()
            .Where(s => s.ProjectId == link.ProjectId)
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.Location
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            projectName = project.Name,
            projectCode = project.Code,
            sites
        });
    }

    [HttpGet("reports")]
    public async Task<IActionResult> GetReports(
        string token,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] Guid? siteId,
        CancellationToken cancellationToken)
    {
        var link = await ResolveActiveLink(token, cancellationToken);
        if (link is null)
            return NotFound();

        var query = db.ProjectSiteDailyReports.AsNoTracking()
            .Where(x => x.ProjectSite.ProjectId == link.ProjectId);

        if (siteId.HasValue)
            query = query.Where(x => x.ProjectSiteId == siteId.Value);
        if (from.HasValue)
            query = query.Where(x => x.ReportDate >= ToUtcDate(from.Value));
        if (to.HasValue)
            query = query.Where(x => x.ReportDate <= ToUtcDate(to.Value));

        var reports = await query
            .OrderByDescending(x => x.ReportDate)
            .Select(x => new
            {
                x.Id,
                x.ProjectSiteId,
                SiteName = x.ProjectSite.Name,
                x.ReportDate,
                x.WeatherCondition,
                x.EngineerCount,
                x.ForemanCount,
                x.CraftsmanCount,
                x.WorkerCount,
                x.OtherCount,
                x.Notes,
                WorkItems = x.WorkItems.Select(w => new
                {
                    w.Description,
                    w.Quantity,
                    w.Unit
                }),
                Photos = x.Photos
                    .Where(p => p.IsVisibleToEmployer)
                    .Select(p => new
                    {
                        p.Id,
                        p.Caption
                    })
            })
            .ToListAsync(cancellationToken);

        return Ok(reports);
    }

    [HttpGet("photos/{photoId:guid}")]
    public async Task<IActionResult> GetPhoto(
        string token,
        Guid photoId,
        CancellationToken cancellationToken)
    {
        var link = await ResolveActiveLink(token, cancellationToken);
        if (link is null)
            return NotFound();

        var photo = await db.ProjectSiteDailyReportPhotos.AsNoTracking()
            .Include(x => x.DailyReport)
            .ThenInclude(x => x.ProjectSite)
            .SingleOrDefaultAsync(x =>
                x.Id == photoId &&
                x.IsVisibleToEmployer &&
                x.DailyReport.ProjectSite.ProjectId == link.ProjectId,
                cancellationToken);

        if (photo is null)
            return NotFound();

        var file = uploadService.GetFile(PhotoCategory, photo.StoredFileName);
        if (file is null)
            return NotFound();

        var stream = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, file.ContentType, enableRangeProcessing: true);
    }

    private async Task<Models.EmployerPortalLink?> ResolveActiveLink(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        return await db.EmployerPortalLinks.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Token == token && x.IsActive, cancellationToken);
    }

    private static DateTime ToUtcDate(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
}
