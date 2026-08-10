using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Services.Upload;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

public sealed record SurveyMeasurementRequest(
    string Description,
    decimal? Quantity,
    string? Unit,
    string? Note);

public sealed record SaveSurveyReportRequest(
    DateTime? ReportDate,
    string Summary,
    string? SiteConditions,
    string? AccessNotes,
    string? Risks,
    bool? RecommendBid,
    List<SurveyMeasurementRequest>? Measurements);

/// <summary>
/// Keşif saha raporu.
///
/// Rapor keşif görevine bağlıdır ve ARŞİVDE KALIR: iş kaybedilse de
/// silinmez. Bir sonraki benzer teklifte okunacak tek kayıt odur.
///
/// TUTAR YOK: rapor teknik bir belgedir; harcırah ve masraf
/// görevlendirme uçlarında ve extra_payment.view maskelemesine tabi.
/// Bu ayrım sayesinde keşfe giden teknik personel raporunu yazarken
/// kimsenin ödemesini görmez.
/// </summary>
[ApiController]
[Authorize]
[Route("api/hr/gorevlendirmeler/{dutyId:guid}/saha-raporu")]
public sealed class DutySurveyReportsController(
    AppDbContext db,
    IUploadService uploadService,
    ICurrentUserService currentUser) : ControllerBase
{
    private const string PhotoCategory = "duty-survey-reports";

    /// <summary>
    /// Okuma: projeyi görebilen ya da görevlendirmeyi görebilen
    /// herkes. Raporda tutar bulunmadığı için saha personelinin
    /// okuması bir sızıntı değil.
    /// </summary>
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.ProjectsView)]
    [RequirePermission(PermissionCatalog.Keys.PersonnelView)]
    public async Task<IActionResult> Get(Guid dutyId, CancellationToken cancellationToken)
    {
        var report = await db.DutySurveyReports
            .AsNoTracking()
            .Include(x => x.Measurements)
            .Include(x => x.Photos)
            .SingleOrDefaultAsync(x => x.DutyId == dutyId, cancellationToken);

        if (report is null)
            return NotFound(new { message = "Bu görevin saha raporu henüz yazılmamış." });

        return Ok(ToDto(report));
    }

    /// <summary>
    /// Yazma: raporu keşfe giden teknik taraf yazar. Görev başına tek
    /// rapor — ikinci çağrı aynı kaydın üzerine yazar, yeni rapor
    /// açmaz.
    /// </summary>
    [HttpPut]
    [RequirePermission(PermissionCatalog.Keys.ProjectsEdit)]
    [RequirePermission(PermissionCatalog.Keys.SiteReportsEdit)]
    public async Task<IActionResult> Save(
        Guid dutyId, SaveSurveyReportRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Summary))
            return BadRequest(new { message = "Rapor özeti zorunludur." });

        var duty = await db.PersonnelDuties
            .AsNoTracking()
            .Where(x => x.Id == dutyId)
            .Select(x => new { x.Id, x.DutyType, x.Status, x.TargetProjectId, x.StartDate })
            .SingleOrDefaultAsync(cancellationToken);

        if (duty is null)
            return NotFound(new { message = "Görevlendirme bulunamadı." });

        if (duty.DutyType != PersonnelDutyType.Survey)
        {
            return BadRequest(new
            {
                message = "Saha raporu yalnızca keşif görevine yazılır."
            });
        }

        // Onaysız görev henüz yapılmamıştır; raporu da olamaz.
        if (duty.Status is not (PersonnelDutyStatus.Approved or
            PersonnelDutyStatus.Completed))
        {
            return BadRequest(new
            {
                message = "Saha raporu yalnızca onaylı görevlendirmeye yazılır."
            });
        }

        var report = await db.DutySurveyReports
            .SingleOrDefaultAsync(x => x.DutyId == dutyId, cancellationToken);

        if (report is null)
        {
            report = new DutySurveyReport
            {
                DutyId = dutyId,
                ProjectId = duty.TargetProjectId,
                CreatedByUserId = currentUser.UserId
            };

            db.DutySurveyReports.Add(report);
        }

        report.ReportDate = ToUtcDate(request.ReportDate ?? duty.StartDate);
        report.Summary = request.Summary.Trim();
        report.SiteConditions = Clean(request.SiteConditions);
        report.AccessNotes = Clean(request.AccessNotes);
        report.Risks = Clean(request.Risks);
        report.RecommendBid = request.RecommendBid;
        report.UpdatedAtUtc = DateTime.UtcNow;
        report.UpdatedByUserId = currentUser.UserId;

        // Ölçümler bütün olarak yenilenir: raporu düzelten kişi
        // listeyi ekranda gördüğü haliyle gönderir, satır satır
        // eşleştirme yapmaz.
        //
        // Eskiler AYRI SORGUYLA okunuyor: raporun kendi koleksiyonu
        // üzerinden silinseydi, silme sırasında koleksiyon değiştiği
        // için satırların bir kısmı silinmeden kalırdı.
        var previous = await db.DutySurveyMeasurements
            .Where(x => x.SurveyReportId == report.Id)
            .ToListAsync(cancellationToken);

        db.DutySurveyMeasurements.RemoveRange(previous);

        var sortOrder = 0;

        foreach (var item in request.Measurements ?? [])
        {
            if (string.IsNullOrWhiteSpace(item.Description))
                continue;

            db.DutySurveyMeasurements.Add(new DutySurveyMeasurement
            {
                SurveyReportId = report.Id,
                SortOrder = sortOrder++,
                Description = item.Description.Trim(),
                Quantity = item.Quantity,
                Unit = Clean(item.Unit),
                Note = Clean(item.Note),
                CreatedByUserId = currentUser.UserId
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Saha raporu kaydedildi.",
            report.Id,
            measurementCount = sortOrder
        });
    }

    [HttpPost("fotograf")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    [RequirePermission(PermissionCatalog.Keys.ProjectsEdit)]
    [RequirePermission(PermissionCatalog.Keys.SiteReportsEdit)]
    public async Task<IActionResult> UploadPhoto(
        Guid dutyId,
        [FromForm] IFormFile file,
        [FromForm] string? caption,
        CancellationToken cancellationToken)
    {
        var report = await db.DutySurveyReports
            .SingleOrDefaultAsync(x => x.DutyId == dutyId, cancellationToken);

        if (report is null)
        {
            return NotFound(new
            {
                message = "Önce saha raporunu kaydedin, sonra fotoğraf ekleyin."
            });
        }

        try
        {
            var uploaded = await uploadService.SaveAsync(
                file, PhotoCategory, cancellationToken);

            var photo = new DutySurveyPhoto
            {
                SurveyReportId = report.Id,
                StoredFileName = uploaded.StoredName,
                OriginalName = uploaded.OriginalName,
                ContentType = uploaded.ContentType,
                Caption = Clean(caption),
                CreatedByUserId = currentUser.UserId
            };

            db.DutySurveyPhotos.Add(photo);
            await db.SaveChangesAsync(cancellationToken);

            return Ok(new { message = "Fotoğraf yüklendi.", photo.Id });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpGet("fotograf/{photoId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.ProjectsView)]
    [RequirePermission(PermissionCatalog.Keys.PersonnelView)]
    public async Task<IActionResult> DownloadPhoto(
        Guid dutyId, Guid photoId, CancellationToken cancellationToken)
    {
        var photo = await db.DutySurveyPhotos
            .AsNoTracking()
            .Include(x => x.SurveyReport)
            .SingleOrDefaultAsync(
                x => x.Id == photoId && x.SurveyReport.DutyId == dutyId,
                cancellationToken);

        if (photo is null)
            return NotFound(new { message = "Fotoğraf bulunamadı." });

        var file = uploadService.GetFile(PhotoCategory, photo.StoredFileName);

        if (file is null)
            return NotFound(new { message = "Fotoğraf dosyası bulunamadı." });

        var stream = new FileStream(
            file.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        return File(stream, file.ContentType, file.StoredName,
            enableRangeProcessing: true);
    }

    [HttpDelete("fotograf/{photoId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.ProjectsEdit)]
    [RequirePermission(PermissionCatalog.Keys.SiteReportsEdit)]
    public async Task<IActionResult> DeletePhoto(
        Guid dutyId, Guid photoId, CancellationToken cancellationToken)
    {
        var photo = await db.DutySurveyPhotos
            .Include(x => x.SurveyReport)
            .SingleOrDefaultAsync(
                x => x.Id == photoId && x.SurveyReport.DutyId == dutyId,
                cancellationToken);

        if (photo is null)
            return NotFound(new { message = "Fotoğraf bulunamadı." });

        db.DutySurveyPhotos.Remove(photo);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Fotoğraf silindi." });
    }

    internal static object ToDto(DutySurveyReport report) => new
    {
        report.Id,
        report.DutyId,
        report.ProjectId,
        report.ReportDate,
        report.Summary,
        report.SiteConditions,
        report.AccessNotes,
        report.Risks,
        report.RecommendBid,
        // Sahada yazıldığı sırayla: sıra kaybolursa rapor okunmaz.
        measurements = report.Measurements
            .OrderBy(m => m.SortOrder)
            .Select(m => new { m.Id, m.Description, m.Quantity, m.Unit, m.Note })
            .ToList(),
        photos = report.Photos
            .Select(p => new { p.Id, p.OriginalName, p.ContentType, p.Caption })
            .ToList()
    };

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime ToUtcDate(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
}
