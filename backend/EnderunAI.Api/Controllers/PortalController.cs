using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
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
    IUploadService uploadService,
    EnderunAI.Api.Services.Hakedis.IContractSummaryProgressService progressService)
    : ControllerBase
{
    private const string PhotoCategory = "site-daily-reports";

    /// <summary>
    /// İşverene açık fiziksel ilerleme — proje ve kısım bazında yüzde.
    ///
    /// TUTAR SIZMAZ: bu uç birim fiyat, kalem tutarı, sözleşme bedeli
    /// veya ağırlık DÖNDÜRMEZ. Kısım ve proje yüzdesi sunucuda sözleşme
    /// tutarıyla ağırlıklandırılır ama ağırlığın kendisi yanıta hiç
    /// girmez; işveren yalnızca "işin ne kadarı bitti" görür.
    ///
    /// Yüzde YALNIZCA onaylı saha raporlarından hesaplanır. Hakedişte
    /// işverenin kabul ettiği miktar buraya karışmaz — portal bizim
    /// fiziksel ilerlememizi gösterir, mutabakatı değil.
    /// </summary>
    [HttpGet("ilerleme")]
    public async Task<IActionResult> GetProgress(
        string token, CancellationToken cancellationToken)
    {
        var link = await ResolveActiveLink(token, cancellationToken);
        if (link is null)
            return NotFound();

        var view = await progressService.BuildAsync(link.ProjectId, cancellationToken);

        if (!view.HasContractSummary)
        {
            // İcmalsiz projede yüzde uydurulmaz; ekran bunu yazar.
            return Ok(new
            {
                hasProgress = false,
                message = "Bu proje için sözleşme icmali tanımlı değil."
            });
        }

        return Ok(new
        {
            hasProgress = true,
            completionRate = view.FieldRate,
            sections = view.Sections.Select(section => new
            {
                name = section.Name,
                completionRate = section.FieldRate,
                itemCount = section.Items.Count,
                completedItemCount = section.Items.Count(x => x.FieldRate >= 100m)
            })
        });
    }

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

        var hasCompanyLogo = await db.Companies.AsNoTracking()
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => x.LogoPath != null)
            .FirstOrDefaultAsync(cancellationToken);

        return Ok(new
        {
            projectName = project.Name,
            projectCode = project.Code,
            sites,
            companyLogoUrl = hasCompanyLogo
                ? "/api/backend/company-settings/logo"
                : null
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
            .Where(x =>
                x.ProjectSite.ProjectId == link.ProjectId &&
                x.Status == ProjectSiteDailyReportStatus.Approved);

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
                x.DailyReport.Status == ProjectSiteDailyReportStatus.Approved &&
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

        // İki şart birden: IsActive VE iptal damgasının boş olması.
        // Önceden yalnızca IsActive bakılıyordu, RevokedAtUtc karar
        // vermede hiç kullanılmıyordu. Uygulama ikisini birlikte
        // yazdığı için pratikte sorun çıkmamıştı; ama yalnızca iptal
        // damgası basılan bir kayıt (veri düzeltmesi, başka bir kod
        // yolu) portalı açık bırakırdı. Kimliği doğrulanmamış bir uçta
        // bu farkı bırakmamak gerekir.
        return await db.EmployerPortalLinks.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Token == token && x.IsActive && x.RevokedAtUtc == null,
                cancellationToken);
    }

    private static DateTime ToUtcDate(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
}
