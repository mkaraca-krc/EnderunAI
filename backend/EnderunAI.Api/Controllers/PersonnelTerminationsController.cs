using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Services.HumanResources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

public sealed record CreateTerminationRequest(
    Guid PersonnelId,
    TerminationReason Reason,
    DateTime TerminationDate,
    decimal? UnusedLeaveDays,
    string? Note);

/// <summary>
/// Personel çıkışı ve tazminat hesabı.
///
/// Tutarlar ücret bilgisi taşıdığı için salary.view ile korunur.
/// Elden ödeme farkı ayrıca extra_payment.view ister ve yetkisiz
/// kullanıcıya null döner — bkz. PersonnelTerminationService.
/// </summary>
[ApiController]
[Authorize]
[Route("api/personnel-terminations")]
public sealed class PersonnelTerminationsController(
    AppDbContext db,
    IPersonnelTerminationService service,
    ICurrentUserService currentUser) : ControllerBase
{
    /// <summary>
    /// "Şu an bu nedenle çıksa ne öderim" — kayıt oluşturmaz.
    /// Gizli yükümlülüğün aktif personel için görünür olmasını sağlar.
    /// </summary>
    [HttpGet("simulation")]
    [RequirePermission(PermissionCatalog.Keys.SalaryView)]
    public async Task<IActionResult> Simulate(
        [FromQuery] Guid personnelId,
        [FromQuery] TerminationReason reason,
        [FromQuery] DateTime? terminationDate,
        [FromQuery] decimal? unusedLeaveDays,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.SimulateAsync(
                personnelId,
                reason,
                terminationDate ?? DateTime.UtcNow.Date,
                unusedLeaveDays,
                cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary>Ayrılış türü → tazminat hakkı matrisi (ekran bunu gösterir).</summary>
    [HttpGet("reasons")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelView)]
    public IActionResult Reasons() =>
        Ok(TerminationRightsMatrix.All
            .Select(pair => new
            {
                reason = (int)pair.Key,
                name = PersonnelTerminationService.ReasonName(pair.Key),
                hasSeverance = pair.Value.Severance,
                hasNotice = pair.Value.Notice,
                hasUnusedLeave = pair.Value.UnusedLeave
            })
            .OrderBy(x => x.reason)
            .ToList());

    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.SalaryView)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok(await db.PersonnelTerminations
            .AsNoTracking()
            .OrderByDescending(x => x.TerminationDate)
            .Select(x => new
            {
                x.Id,
                x.PersonnelId,
                PersonnelFullName = x.Personnel.FirstName + " " + x.Personnel.LastName,
                x.TerminationDate,
                Reason = (int)x.Reason,
                Status = (int)x.Status,
                x.ServiceDays,
                x.UnusedLeaveDays,
                x.OfficialNetTotal,
                x.SeveranceCeilingApplied,
                x.FinalizedAtUtc
            })
            .ToListAsync(cancellationToken));

    /// <summary>
    /// Ayrılış değerlendirmesi: tekrar işe alım kodu ve gerekçesi.
    ///
    /// Yasal çıkış nedenine DOKUNMAZ; ayrı katmandır. Geçmiş çıkışlara
    /// sonradan da işaretlenebilir — değerlendirme çıkış anında
    /// yapılamamış olabilir.
    ///
    /// Kırmızı ve sarıda gerekçe ZORUNLU: gerekçesiz bir engel, itiraz
    /// edilemez bir engeldir ve işe alan kişi neyi geçtiğini bilmeden
    /// karar veremez.
    /// </summary>
    [HttpPost("{id:guid}/rehire-degerlendirmesi")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelEdit)]
    public async Task<IActionResult> SetRehireAssessment(
        Guid id,
        SetRehireAssessmentRequest request,
        CancellationToken cancellationToken)
    {
        var termination = await db.PersonnelTerminations
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (termination is null)
            return NotFound(new { message = "Çıkış kaydı bulunamadı." });

        if (request.RehireCode is int code &&
            !Enum.IsDefined(typeof(RehireCode), code))
        {
            return BadRequest(new { message = "Geçersiz tekrar işe alım kodu." });
        }

        var note = request.RehireNote?.Trim();

        var needsNote = request.RehireCode is (int)Models.RehireCode.Red
            or (int)Models.RehireCode.Yellow;

        if (needsNote && string.IsNullOrWhiteSpace(note))
        {
            return BadRequest(new
            {
                message = "Kırmızı ve sarı değerlendirmede gerekçe zorunludur."
            });
        }

        termination.RehireCode = request.RehireCode is int value
            ? (RehireCode)value
            : null;

        termination.RehireNote = string.IsNullOrWhiteSpace(note) ? null : note;

        // Damga her işaretlemede tazelenir: son değerlendirmenin kime
        // ve ne zamana ait olduğu sorulan şeydir.
        if (termination.RehireCode is null)
        {
            termination.RehireMarkedByUserId = null;
            termination.RehireMarkedAtUtc = null;
        }
        else
        {
            termination.RehireMarkedByUserId = currentUser.UserId;
            termination.RehireMarkedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = termination.RehireCode is null
                ? "Değerlendirme kaldırıldı."
                : "Ayrılış değerlendirmesi kaydedildi.",
            termination.Id,
            rehireCode = (int?)termination.RehireCode,
            rehireCodeName = RehireCodeName(termination.RehireCode),
            termination.RehireNote,
            termination.RehireMarkedAtUtc
        });
    }

    /// <summary>
    /// Değerlendirme okuma. Ayrı uçta, çünkü çıkış listesi salary.view
    /// ile açık ve oraya konsaydı ücret yetkisi olan herkes gerekçeyi
    /// görürdü — değerlendirme İK'nın kaydıdır.
    /// </summary>
    [HttpGet("{id:guid}/rehire-degerlendirmesi")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelEdit)]
    public async Task<IActionResult> GetRehireAssessment(
        Guid id, CancellationToken cancellationToken)
    {
        var row = await db.PersonnelTerminations
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.PersonnelId,
                PersonnelFullName = x.Personnel.FirstName + " " + x.Personnel.LastName,
                x.TerminationDate,
                Reason = (int)x.Reason,
                RehireCode = (int?)x.RehireCode,
                x.RehireNote,
                x.RehireMarkedAtUtc,
                x.RehireMarkedByUserId
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
            return NotFound(new { message = "Çıkış kaydı bulunamadı." });

        return Ok(new
        {
            row.Id,
            row.PersonnelId,
            row.PersonnelFullName,
            row.TerminationDate,
            row.Reason,
            row.RehireCode,
            rehireCodeName = RehireCodeName((RehireCode?)row.RehireCode),
            row.RehireNote,
            row.RehireMarkedAtUtc,
            row.RehireMarkedByUserId
        });
    }

    internal static string RehireCodeName(RehireCode? code) => code switch
    {
        Models.RehireCode.Green => "Yeşil — sorunsuz",
        Models.RehireCode.Yellow => "Sarı — dikkat, şartlı",
        Models.RehireCode.Red => "Kırmızı — işe alınamaz",
        _ => "Değerlendirilmedi"
    };

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.SalaryManage)]
    public async Task<IActionResult> Create(
        CreateTerminationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var id = await service.CreateAsync(
                request.PersonnelId,
                request.Reason,
                request.TerminationDate,
                request.UnusedLeaveDays,
                request.Note,
                cancellationToken);

            return Ok(new { id, message = "Çıkış kaydı taslak olarak oluşturuldu." });
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    /// <summary>
    /// Çıkışı kesinleştirir. Personel "Ayrıldı" durumuna geçer ve
    /// hesaplanan tutarlar donar.
    /// </summary>
    [HttpPost("{id:guid}/finalize")]
    [RequirePermission(PermissionCatalog.Keys.AttendancePayrollApprove)]
    public async Task<IActionResult> Finalize(
        Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await service.FinalizeAsync(id, cancellationToken);
            return Ok(new { message = "Çıkış kesinleştirildi." });
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }
}

public sealed record SetRehireAssessmentRequest(
    /// <summary>0 Yeşil · 1 Sarı · 2 Kırmızı · null "değerlendirilmedi".</summary>
    int? RehireCode,
    string? RehireNote);
