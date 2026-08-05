using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Services.Upload;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/project-sites/{siteId:guid}/daily-reports")]
public sealed class ProjectSiteDailyReportsController(
    AppDbContext db,
    IUploadService uploadService,
    ICurrentUserService currentUser,
    ICurrentDataScopeService dataScope) : ControllerBase
{
    private const string PhotoCategory = "site-daily-reports";

    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.SiteReportsView)]
    public async Task<IActionResult> GetAll(
        Guid siteId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessSiteAsync(siteId, cancellationToken))
            return NotFound(new { message = "Şantiye bulunamadı." });

        var query = db.ProjectSiteDailyReports.AsNoTracking()
            .Where(x => x.ProjectSiteId == siteId);

        if (from.HasValue)
            query = query.Where(x => x.ReportDate >= ToUtcDate(from.Value));
        if (to.HasValue)
            query = query.Where(x => x.ReportDate <= ToUtcDate(to.Value));

        var items = await query
            .OrderByDescending(x => x.ReportDate)
            .Select(x => new
            {
                x.Id,
                x.ReportDate,
                x.WeatherCondition,
                TotalHeadcount = x.EngineerCount + x.ForemanCount + x.CraftsmanCount + x.WorkerCount + x.OtherCount,
                WorkItemCount = x.WorkItems.Count,
                PhotoCount = x.Photos.Count,
                Status = (int)x.Status
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("by-date/{date}")]
    [RequirePermission(PermissionCatalog.Keys.SiteReportsView)]
    public async Task<IActionResult> GetByDate(
        Guid siteId,
        DateTime date,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessSiteAsync(siteId, cancellationToken))
            return NotFound(new { message = "Şantiye bulunamadı." });

        var report = await LoadDetail(siteId, ToUtcDate(date), cancellationToken);
        return report is null
            ? NotFound(new { message = "Bu tarih için günlük rapor bulunamadı." })
            : Ok(report);
    }

    [HttpGet("{reportId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.SiteReportsView)]
    public async Task<IActionResult> GetById(
        Guid siteId,
        Guid reportId,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessSiteAsync(siteId, cancellationToken))
            return NotFound(new { message = "Şantiye bulunamadı." });

        var report = await LoadDetail(siteId, reportId, cancellationToken);
        return report is null
            ? NotFound(new { message = "Günlük rapor bulunamadı." })
            : Ok(report);
    }

    [HttpGet("suggested-headcount")]
    [RequirePermission(PermissionCatalog.Keys.SiteReportsView)]
    public async Task<IActionResult> GetSuggestedHeadcount(
        Guid siteId,
        [FromQuery] DateTime date,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessSiteAsync(siteId, cancellationToken))
            return NotFound(new { message = "Şantiye bulunamadı." });

        var day = ToUtcDate(date);

        var roles = await db.ProjectSiteAssignments.AsNoTracking()
            .Where(x =>
                x.ProjectSiteId == siteId &&
                x.StartDate <= day &&
                (x.EndDate == null || x.EndDate >= day))
            .Select(x => x.Role)
            .ToListAsync(cancellationToken);

        int engineer = 0, foreman = 0, craftsman = 0, worker = 0, other = 0;

        foreach (var role in roles)
        {
            var normalized = (role ?? string.Empty).Trim().ToLowerInvariant();

            if (normalized.Contains("mühendis") || normalized.Contains("muhendis"))
                engineer++;
            else if (normalized.Contains("formen") || normalized.Contains("şef") || normalized.Contains("sef"))
                foreman++;
            else if (normalized.Contains("usta"))
                craftsman++;
            else if (normalized.Contains("işçi") || normalized.Contains("isci"))
                worker++;
            else
                other++;
        }

        return Ok(new
        {
            engineerCount = engineer,
            foremanCount = foreman,
            craftsmanCount = craftsman,
            workerCount = worker,
            otherCount = other
        });
    }

    /// <summary>
    /// Günlük rapora kalem eklerken kullanılan HIZLI SEÇİM listesi.
    ///
    /// Telefonda iki dokunuşta seçilebilsin diye iki bölüm döner:
    /// bu şantiyede son 30 günde en çok girilen kalemler önce, sonra
    /// icmalin kalanı. Arama verilirse yalnızca eşleşenler döner.
    ///
    /// İcmali olmayan projede boş liste döner ve arayüz serbest metne
    /// düşer — icmalsiz proje çalışmaya devam etmeli.
    /// </summary>
    [HttpGet("icmal-kalemleri")]
    [RequirePermission(PermissionCatalog.Keys.SiteReportsView)]
    public async Task<IActionResult> GetSummaryItems(
        Guid siteId,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessSiteAsync(siteId, cancellationToken))
            return NotFound(new { message = "Şantiye bulunamadı." });

        var projectId = await db.ProjectSites
            .AsNoTracking()
            .Where(x => x.Id == siteId)
            .Select(x => x.ProjectId)
            .SingleAsync(cancellationToken);

        var boqId = await ResolveSummaryBoqIdAsync(projectId, cancellationToken);

        if (boqId is null)
            return Ok(new { hasContractSummary = false, frequent = Array.Empty<object>(), items = Array.Empty<object>() });

        var query = db.ProjectBoqItems
            .AsNoTracking()
            .Where(x => x.ProjectBoqId == boqId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x =>
                x.PositionCode.ToLower().Contains(term) ||
                x.Description.ToLower().Contains(term));
        }

        var items = await query
            .OrderBy(x => x.LineNumber)
            .Select(x => new
            {
                x.Id,
                x.PositionCode,
                x.Description,
                x.Unit,
                x.ContractQuantity,
                SectionName = x.ProjectHakedisSection != null
                    ? x.ProjectHakedisSection.Name
                    : null
            })
            .Take(500)
            .ToListAsync(cancellationToken);

        // Sık kullanılanlar: bu şantiyede son 30 günde girilmiş kalemler,
        // kullanım sayısına göre. Rapor onaylı olmak zorunda değil —
        // burası bir kolaylık listesi, gerçekleşme hesabı değil.
        var since = DateTime.UtcNow.Date.AddDays(-30);

        var frequentIds = await db.ProjectSiteDailyReportWorkItems
            .AsNoTracking()
            .Where(x =>
                x.ProjectBoqItemId != null &&
                x.DailyReport.ProjectSiteId == siteId &&
                x.DailyReport.ReportDate >= since)
            .GroupBy(x => x.ProjectBoqItemId!.Value)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .Take(8)
            .ToListAsync(cancellationToken);

        var byId = items.ToDictionary(x => x.Id);

        var frequent = frequentIds
            .Where(byId.ContainsKey)
            .Select(id => byId[id])
            .ToList();

        return Ok(new
        {
            hasContractSummary = true,
            frequent,
            items
        });
    }

    /// <summary>
    /// Projenin geçerli icmali: önce sözleşme tabanı işaretli olan,
    /// yoksa onaylı güncel revizyon. ContractSummaryProgressService ile
    /// aynı sıra — iki yerde farklı icmale bakmak, sahada seçilen
    /// kalemin ilerlemede görünmemesine yol açardı.
    /// </summary>
    private async Task<Guid?> ResolveSummaryBoqIdAsync(
        Guid projectId, CancellationToken cancellationToken)
    {
        var baseline = await db.ProjectBoqs
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.IsContractBaseline)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (baseline is not null)
            return baseline;

        return await db.ProjectBoqs
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId &&
                        x.IsCurrentRevision &&
                        x.Status == ProjectBoqStatus.Approved)
            .OrderByDescending(x => x.RevisionNumber)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.SiteReportsCreate)]
    public async Task<IActionResult> Create(
        Guid siteId,
        UpsertDailyReportRequest request,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessSiteAsync(siteId, cancellationToken))
            return NotFound(new { message = "Şantiye bulunamadı." });

        var day = ToUtcDate(request.ReportDate);

        var existing = await db.ProjectSiteDailyReports.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ProjectSiteId == siteId && x.ReportDate == day, cancellationToken);

        if (existing is not null)
        {
            return Conflict(new
            {
                message = "Bu şantiye ve tarih için zaten bir günlük rapor var.",
                existingReportId = existing.Id
            });
        }

        var report = new ProjectSiteDailyReport
        {
            ProjectSiteId = siteId,
            ReportDate = day,
            WeatherCondition = request.WeatherCondition?.Trim(),
            EngineerCount = request.EngineerCount,
            ForemanCount = request.ForemanCount,
            CraftsmanCount = request.CraftsmanCount,
            WorkerCount = request.WorkerCount,
            OtherCount = request.OtherCount,
            Notes = request.Notes?.Trim(),
            CreatedByUserId = currentUser.UserId
        };

        foreach (var item in request.WorkItems ?? [])
        {
            if (string.IsNullOrWhiteSpace(item.Description))
                continue;

            report.WorkItems.Add(new ProjectSiteDailyReportWorkItem
            {
                ProjectBoqItemId = item.ProjectBoqItemId,
                Description = item.Description.Trim(),
                Quantity = item.Quantity,
                Unit = item.Unit?.Trim()
            });
        }

        db.ProjectSiteDailyReports.Add(report);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Günlük rapor oluşturuldu.", report.Id });
    }

    [HttpPut("{reportId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.SiteReportsEdit)]
    public async Task<IActionResult> Update(
        Guid siteId,
        Guid reportId,
        UpsertDailyReportRequest request,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessSiteAsync(siteId, cancellationToken))
            return NotFound(new { message = "Şantiye bulunamadı." });

        var report = await db.ProjectSiteDailyReports
            .Include(x => x.WorkItems)
            .SingleOrDefaultAsync(x => x.Id == reportId && x.ProjectSiteId == siteId, cancellationToken);

        if (report is null)
            return NotFound(new { message = "Günlük rapor bulunamadı." });

        if (report.Status == ProjectSiteDailyReportStatus.Approved)
        {
            return Conflict(new
            {
                message = "Onaylanmış rapor artık düzenlenemez."
            });
        }

        report.WeatherCondition = request.WeatherCondition?.Trim();
        report.EngineerCount = request.EngineerCount;
        report.ForemanCount = request.ForemanCount;
        report.CraftsmanCount = request.CraftsmanCount;
        report.WorkerCount = request.WorkerCount;
        report.OtherCount = request.OtherCount;
        report.Notes = request.Notes?.Trim();
        report.UpdatedAtUtc = DateTime.UtcNow;
        report.UpdatedByUserId = currentUser.UserId;

        db.ProjectSiteDailyReportWorkItems.RemoveRange(report.WorkItems);
        report.WorkItems.Clear();

        foreach (var item in request.WorkItems ?? [])
        {
            if (string.IsNullOrWhiteSpace(item.Description))
                continue;

            report.WorkItems.Add(new ProjectSiteDailyReportWorkItem
            {
                ProjectBoqItemId = item.ProjectBoqItemId,
                Description = item.Description.Trim(),
                Quantity = item.Quantity,
                Unit = item.Unit?.Trim()
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Günlük rapor güncellendi." });
    }

    [HttpPost("{reportId:guid}/approve")]
    [RequirePermission(PermissionCatalog.Keys.SiteReportsApprove)]
    public async Task<IActionResult> Approve(
        Guid siteId,
        Guid reportId,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessSiteAsync(siteId, cancellationToken))
            return NotFound(new { message = "Şantiye bulunamadı." });

        var report = await db.ProjectSiteDailyReports
            .SingleOrDefaultAsync(x => x.Id == reportId && x.ProjectSiteId == siteId, cancellationToken);

        if (report is null)
            return NotFound(new { message = "Günlük rapor bulunamadı." });

        if (report.Status == ProjectSiteDailyReportStatus.Approved)
            return Conflict(new { message = "Rapor zaten onaylanmış." });

        report.Status = ProjectSiteDailyReportStatus.Approved;
        report.ApprovedAtUtc = DateTime.UtcNow;
        report.ApprovedByUserId = currentUser.UserId;
        report.UpdatedAtUtc = DateTime.UtcNow;
        report.UpdatedByUserId = currentUser.UserId;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Günlük rapor onaylandı; işveren portalına düşecek.",
            report.Id
        });
    }

    [HttpPost("{reportId:guid}/photos")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    [RequirePermission(PermissionCatalog.Keys.SiteReportsEdit)]
    public async Task<IActionResult> UploadPhoto(
        Guid siteId,
        Guid reportId,
        [FromForm] IFormFile file,
        [FromForm] bool isVisibleToEmployer,
        [FromForm] string? caption,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessSiteAsync(siteId, cancellationToken))
            return NotFound(new { message = "Şantiye bulunamadı." });

        var report = await db.ProjectSiteDailyReports
            .SingleOrDefaultAsync(x => x.Id == reportId && x.ProjectSiteId == siteId, cancellationToken);

        if (report is null)
            return NotFound(new { message = "Günlük rapor bulunamadı." });

        try
        {
            var uploaded = await uploadService.SaveAsync(file, PhotoCategory, cancellationToken);

            var photo = new ProjectSiteDailyReportPhoto
            {
                DailyReportId = reportId,
                StoredFileName = uploaded.StoredName,
                OriginalName = uploaded.OriginalName,
                ContentType = uploaded.ContentType,
                Caption = caption?.Trim(),
                IsVisibleToEmployer = isVisibleToEmployer
            };

            db.ProjectSiteDailyReportPhotos.Add(photo);
            await db.SaveChangesAsync(cancellationToken);

            return Ok(new { message = "Fotoğraf yüklendi.", photo.Id, photo.IsVisibleToEmployer });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPatch("{reportId:guid}/photos/{photoId:guid}/visibility")]
    [RequirePermission(PermissionCatalog.Keys.SiteReportsEdit)]
    public async Task<IActionResult> SetPhotoVisibility(
        Guid siteId,
        Guid reportId,
        Guid photoId,
        [FromQuery] bool isVisibleToEmployer,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessSiteAsync(siteId, cancellationToken))
            return NotFound(new { message = "Şantiye bulunamadı." });

        var photo = await db.ProjectSiteDailyReportPhotos
            .Include(x => x.DailyReport)
            .SingleOrDefaultAsync(x =>
                x.Id == photoId &&
                x.DailyReportId == reportId &&
                x.DailyReport.ProjectSiteId == siteId,
                cancellationToken);

        if (photo is null)
            return NotFound(new { message = "Fotoğraf bulunamadı." });

        photo.IsVisibleToEmployer = isVisibleToEmployer;
        photo.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Fotoğraf görünürlüğü güncellendi.", photo.IsVisibleToEmployer });
    }

    [HttpGet("{reportId:guid}/photos/{photoId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.SiteReportsView)]
    public async Task<IActionResult> DownloadPhoto(
        Guid siteId,
        Guid reportId,
        Guid photoId,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessSiteAsync(siteId, cancellationToken))
            return NotFound(new { message = "Şantiye bulunamadı." });

        var photo = await db.ProjectSiteDailyReportPhotos.AsNoTracking()
            .Include(x => x.DailyReport)
            .SingleOrDefaultAsync(x =>
                x.Id == photoId &&
                x.DailyReportId == reportId &&
                x.DailyReport.ProjectSiteId == siteId,
                cancellationToken);

        if (photo is null)
            return NotFound(new { message = "Fotoğraf bulunamadı." });

        var file = uploadService.GetFile(PhotoCategory, photo.StoredFileName);
        if (file is null)
            return NotFound(new { message = "Fotoğraf dosyası bulunamadı." });

        var stream = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, file.ContentType, file.StoredName, enableRangeProcessing: true);
    }

    [HttpDelete("{reportId:guid}/photos/{photoId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.SiteReportsDelete)]
    public async Task<IActionResult> DeletePhoto(
        Guid siteId,
        Guid reportId,
        Guid photoId,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessSiteAsync(siteId, cancellationToken))
            return NotFound(new { message = "Şantiye bulunamadı." });

        var photo = await db.ProjectSiteDailyReportPhotos
            .Include(x => x.DailyReport)
            .SingleOrDefaultAsync(x =>
                x.Id == photoId &&
                x.DailyReportId == reportId &&
                x.DailyReport.ProjectSiteId == siteId,
                cancellationToken);

        if (photo is null)
            return NotFound(new { message = "Fotoğraf bulunamadı." });

        uploadService.DeleteFile(PhotoCategory, photo.StoredFileName);
        db.ProjectSiteDailyReportPhotos.Remove(photo);
        await db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private async Task<bool> CanAccessSiteAsync(
        Guid siteId,
        CancellationToken cancellationToken)
    {
        var site = await db.ProjectSites
            .AsNoTracking()
            .Where(x => x.Id == siteId)
            .Select(x => new
            {
                x.Project.CompanyId,
                x.Project.BranchId,
                x.ProjectId
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (site is null)
            return false;

        var scope = await dataScope.GetAsync(cancellationToken);
        return scope is not null &&
            scope.CanAccessSite(site.CompanyId, site.BranchId, site.ProjectId, siteId);
    }

    private async Task<object?> LoadDetail(Guid siteId, Guid reportId, CancellationToken cancellationToken) =>
        await db.ProjectSiteDailyReports.AsNoTracking()
            .Where(x => x.Id == reportId && x.ProjectSiteId == siteId)
            .Select(ProjectionExpression())
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<object?> LoadDetail(Guid siteId, DateTime reportDate, CancellationToken cancellationToken) =>
        await db.ProjectSiteDailyReports.AsNoTracking()
            .Where(x => x.ProjectSiteId == siteId && x.ReportDate == reportDate)
            .Select(ProjectionExpression())
            .SingleOrDefaultAsync(cancellationToken);

    private static System.Linq.Expressions.Expression<Func<ProjectSiteDailyReport, object>> ProjectionExpression() =>
        x => new
        {
            x.Id,
            x.ProjectSiteId,
            x.ReportDate,
            x.WeatherCondition,
            x.EngineerCount,
            x.ForemanCount,
            x.CraftsmanCount,
            x.WorkerCount,
            x.OtherCount,
            x.Notes,
            Status = (int)x.Status,
            x.ApprovedAtUtc,
            WorkItems = x.WorkItems.Select(w => new
            {
                w.Id,
                w.ProjectBoqItemId,
                BoqPositionCode = w.ProjectBoqItem != null
                    ? w.ProjectBoqItem.PositionCode
                    : null,
                w.Description,
                w.Quantity,
                w.Unit
            }),
            Photos = x.Photos.Select(p => new
            {
                p.Id,
                p.OriginalName,
                p.Caption,
                p.IsVisibleToEmployer,
                p.CreatedAtUtc
            })
        };

    private static DateTime ToUtcDate(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
}

public sealed record UpsertDailyReportRequest(
    DateTime ReportDate,
    string? WeatherCondition,
    int EngineerCount,
    int ForemanCount,
    int CraftsmanCount,
    int WorkerCount,
    int OtherCount,
    string? Notes,
    List<DailyReportWorkItemRequest>? WorkItems);

public sealed record DailyReportWorkItemRequest(
    string Description,
    decimal? Quantity,
    string? Unit,
    /// <summary>
    /// Sözleşme icmali kalemi. Opsiyonel — icmalde olmayan iş de
    /// yazılabilmeli. Seçilirse onaylı miktar o kaleme birikir.
    /// </summary>
    Guid? ProjectBoqItemId = null);
