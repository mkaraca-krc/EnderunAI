using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Services.HumanResources;
using EnderunAI.Api.Services.Projects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

public sealed record ProjectSurveyOutcomeRequest(
    /// <summary>1 kazanıldı · 2 kaybedildi.</summary>
    int Outcome,
    string? Note);

/// <summary>
/// Keşfin sonucu: kazanıldı / kaybedildi.
///
/// GİDER AYRIMI buradan çıkar:
/// - KAZANILDI → proje aktife alınır. Keşif masrafı OLDUĞU YERDE
///   KALIR; taşınmaz. Aynı proje olduğu için "gerçek projeye
///   bağlanma" kendiliğinden gerçekleşir. Taşınsaydı aynı harcamanın
///   iki defterde görünme riski doğardı.
/// - KAYBEDİLDİ → proje iptale çekilir ve keşif masrafı "proje adı —
///   Proje Keşfi" gideri olarak okunur. Satırlar SİLİNMEZ: gerçek
///   para harcandı ve şirket giderinden düşmemeli. Saha raporu da
///   KALIR — bir sonraki benzer teklifte okunacak tek kayıt odur.
///
/// Sonuç proje STATÜSÜNE yeni bir değer eklemez; "kaybedildi" statü
/// değil keşfin sonucudur ve iptalden ayrı tutulur.
/// </summary>
[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}")]
public sealed class ProjectSurveyOutcomeController(
    AppDbContext db,
    DutyExpensePostingService expensePosting,
    ICurrentUserService currentUser) : ControllerBase
{
    [HttpPost("kesif-sonucu")]
    [RequirePermission(PermissionCatalog.Keys.ProjectsEdit)]
    public async Task<IActionResult> SetOutcome(
        Guid projectId,
        ProjectSurveyOutcomeRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Outcome != (int)ProjectSurveyOutcome.Won &&
            request.Outcome != (int)ProjectSurveyOutcome.Lost)
        {
            return BadRequest(new
            {
                message = "Keşif sonucu ya kazanıldı ya kaybedildi olabilir."
            });
        }

        var outcome = (ProjectSurveyOutcome)request.Outcome;
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();

        // Kaybetme gerekçesi zorunlu: gerekçesiz kaybedilen teklif,
        // bir sonraki teklifin öğrenebileceği hiçbir şey bırakmaz.
        if (outcome == ProjectSurveyOutcome.Lost && note is null)
            return BadRequest(new { message = "Kaybetme gerekçesi zorunludur." });

        var project = await db.Projects
            .SingleOrDefaultAsync(x => x.Id == projectId, cancellationToken);

        if (project is null)
            return NotFound(new { message = "Proje bulunamadı." });

        // Sonuç kontrolü statüden ÖNCE: karar projeyi zaten keşiften
        // çıkardığı için statü kontrolü önce çalışsaydı ikinci deneme
        // "keşif statüsünde değil" derdi ve asıl sebebi gizlerdi.
        if (project.SurveyOutcome != ProjectSurveyOutcome.Pending)
        {
            return Conflict(new
            {
                message = "Bu keşfin sonucu zaten girilmiş."
            });
        }

        if (project.Status != ProjectStatus.Kesif)
        {
            return BadRequest(new
            {
                message = "Keşif sonucu yalnızca keşif statüsündeki projede girilir."
            });
        }

        if (outcome == ProjectSurveyOutcome.Won)
        {
            // Aktife geçen projenin işvereni onaylı müşteri cari kartı
            // olmak zorunda; kural proje ekranıyla ORTAK.
            var (_, employerError) = await ProjectEmployerRule.ValidateAsync(
                db, ProjectStatus.Active, project.EmployerCurrentAccountId,
                project.CompanyId, cancellationToken);

            if (employerError is not null)
            {
                return BadRequest(new
                {
                    message = $"İş kazanıldı olarak işaretlenemedi: {employerError}"
                });
            }
        }

        project.SurveyOutcome = outcome;
        project.SurveyOutcomeAtUtc = DateTime.UtcNow;
        project.SurveyOutcomeByUserId = currentUser.UserId;
        project.SurveyOutcomeNote = note;

        project.Status = outcome == ProjectSurveyOutcome.Won
            ? ProjectStatus.Active
            : ProjectStatus.Cancelled;

        project.UpdatedAtUtc = DateTime.UtcNow;
        project.UpdatedByUserId = currentUser.UserId;

        // Sonucu önce yaz: defter satırlarının adı projenin güncel
        // sonucundan okunuyor.
        await db.SaveChangesAsync(cancellationToken);

        // Tutarlara dokunmaz; yalnızca satırların ne olduğu değişir.
        await expensePosting.RepostForProjectAsync(projectId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = outcome == ProjectSurveyOutcome.Won
                ? "İş kazanıldı; proje aktife alındı ve keşif masrafı projenin " +
                  "maliyetinde kaldı."
                : "Teklif kaybedildi; keşif masrafı proje keşif gideri olarak " +
                  "kaldı, saha raporu arşivde duruyor.",
            project.Id,
            status = (int)project.Status,
            surveyOutcome = (int)project.SurveyOutcome,
            project.SurveyOutcomeAtUtc
        });
    }

    /// <summary>
    /// Projenin keşif dosyası: sonuç, saha raporları ve keşif
    /// masrafının kategori kırılımı.
    ///
    /// Masraf TUTARLARI YOK: kırılım ve toplam extra_payment.view'e
    /// tabi olan görevlendirme uçlarından okunur. Burada yalnızca
    /// "hangi kalemde kaç satır var" bilgisi döner ki keşif dosyası
    /// teknik tarafa da açılabilsin.
    /// </summary>
    [HttpGet("kesif-dosyasi")]
    [RequirePermission(PermissionCatalog.Keys.ProjectsView)]
    public async Task<IActionResult> GetDossier(
        Guid projectId, CancellationToken cancellationToken)
    {
        var project = await db.Projects
            .AsNoTracking()
            .Where(x => x.Id == projectId)
            .Select(x => new
            {
                x.Id,
                x.Code,
                x.Name,
                Status = (int)x.Status,
                SurveyOutcome = (int)x.SurveyOutcome,
                x.SurveyOutcomeAtUtc,
                x.SurveyOutcomeNote
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (project is null)
            return NotFound(new { message = "Proje bulunamadı." });

        // Rapor kaybedilen işte de duruyor; sorgu sonuca bakmıyor.
        var reports = await db.DutySurveyReports
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.ReportDate)
            .Select(x => new
            {
                x.Id,
                x.DutyId,
                x.ReportDate,
                x.Summary,
                x.RecommendBid,
                MeasurementCount = x.Measurements.Count,
                PhotoCount = x.Photos.Count
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            project,
            surveyOutcomeName = SurveyOutcomeName(
                (ProjectSurveyOutcome)project.SurveyOutcome),
            reports
        });
    }

    private static string SurveyOutcomeName(ProjectSurveyOutcome outcome) => outcome switch
    {
        ProjectSurveyOutcome.Won => "Kazanıldı",
        ProjectSurveyOutcome.Lost => "Kaybedildi",
        _ => "Sonuç bekliyor"
    };
}
