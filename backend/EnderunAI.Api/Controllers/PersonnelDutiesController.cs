using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

public sealed record CreatePersonnelDutyRequest(
    Guid CompanyId,
    Guid PersonnelId,
    int DutyType,
    Guid TargetProjectId,
    Guid? TargetProjectSiteId,
    Guid? SourceProjectId,
    DateTime StartDate,
    DateTime EndDate,
    bool IsOutOfCity,
    decimal DailyAllowance,
    string Purpose,
    string? Notes);

public sealed record DecidePersonnelDutyRequest(string? DecisionNote);

/// <summary>
/// Personel görevlendirmeleri.
///
/// ONAY AKIŞI: İK talebi açar (personnel.edit), GM onaylar. Onaysız
/// görev maliyet ve harcırah üretmez — talep aşamasındaki bir görev
/// projenin kârını değiştirmemeli. Açılış ve karar ayrı damgalanır.
///
/// GÖREV TÜRÜ maliyet yolunu belirler:
/// - Çalışma: gün maliyeti hedef projeye kayar (+ masraf)
/// - Keşif ve Ziyaret: YALNIZ masraf
/// Türler karıştırılırsa ziyaretçinin günlük ücreti gitmediği bir
/// imalata yazılırdı.
///
/// TUTAR GİZLİLİĞİ: harcırah elden ödeme niteliğindedir; tutarlar
/// yalnızca extra_payment.view olan kullanıcıya döner. Saha personeli
/// görevin varlığını ve tarihlerini görür, tutarını görmez.
/// </summary>
[ApiController]
[Authorize]
[Route("api/hr/gorevlendirmeler")]
public sealed class PersonnelDutiesController(
    AppDbContext db,
    ICurrentUserService currentUser,
    IUserAuthorizationService authorization,
    IExtraPaymentVisibilityService extraPaymentVisibility) : ControllerBase
{
    /// <summary>Görevi onaylayabilen roller.</summary>
    private static readonly string[] ApprovalRoles = ["Admin", "Genel Müdür"];

    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.PersonnelView)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? personnelId,
        [FromQuery] Guid? projectId,
        [FromQuery] int? status,
        CancellationToken cancellationToken)
    {
        var query = db.PersonnelDuties.AsNoTracking();

        if (companyId is Guid company) query = query.Where(x => x.CompanyId == company);
        if (personnelId is Guid person) query = query.Where(x => x.PersonnelId == person);
        if (projectId is Guid project) query = query.Where(x => x.TargetProjectId == project);
        if (status is int state) query = query.Where(x => (int)x.Status == state);

        var canViewAmounts = await extraPaymentVisibility
            .CanViewExtraPaymentAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(x => x.StartDate)
            .Select(x => new
            {
                x.Id,
                x.PersonnelId,
                PersonnelFullName = x.Personnel.FirstName + " " + x.Personnel.LastName,
                DutyType = (int)x.DutyType,
                x.TargetProjectId,
                TargetProjectCode = x.TargetProject.Code,
                TargetProjectName = x.TargetProject.Name,
                x.SourceProjectId,
                x.StartDate,
                x.EndDate,
                x.IsOutOfCity,
                x.DailyAllowance,
                x.Purpose,
                Status = (int)x.Status,
                x.RequestedAtUtc,
                x.ApprovedAtUtc,
                x.DecisionNote
            })
            .ToListAsync(cancellationToken);

        return Ok(rows.Select(x => new
        {
            x.Id,
            x.PersonnelId,
            x.PersonnelFullName,
            x.DutyType,
            dutyTypeName = DutyTypeName((PersonnelDutyType)x.DutyType),
            shiftsLaborCost =
                (PersonnelDutyType)x.DutyType == PersonnelDutyType.Work,
            x.TargetProjectId,
            x.TargetProjectCode,
            x.TargetProjectName,
            x.SourceProjectId,
            x.StartDate,
            x.EndDate,
            dayCount = DayCountOf(x.StartDate, x.EndDate),
            x.IsOutOfCity,
            // Tutarlar elden izolasyonuna tabi: gizlenmez, hiç gelmez.
            dailyAllowance = canViewAmounts ? x.DailyAllowance : (decimal?)null,
            totalAllowance = canViewAmounts
                ? x.DailyAllowance * DayCountOf(x.StartDate, x.EndDate)
                : (decimal?)null,
            amountsHidden = !canViewAmounts,
            x.Purpose,
            x.Status,
            statusName = StatusName((PersonnelDutyStatus)x.Status),
            x.RequestedAtUtc,
            x.ApprovedAtUtc,
            x.DecisionNote
        }));
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.PersonnelEdit)]
    public async Task<IActionResult> Create(
        CreatePersonnelDutyRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(typeof(PersonnelDutyType), request.DutyType))
            return BadRequest(new { message = "Geçersiz görev türü." });

        if (string.IsNullOrWhiteSpace(request.Purpose))
            return BadRequest(new { message = "Görev amacı zorunludur." });

        var start = ToUtc(request.StartDate);
        var end = ToUtc(request.EndDate);

        if (end < start)
        {
            return BadRequest(new
            {
                message = "Görev bitişi başlangıcından önce olamaz."
            });
        }

        if (request.DailyAllowance < 0m)
            return BadRequest(new { message = "Harcırah negatif olamaz." });

        var personnel = await db.Personnel
            .AsNoTracking()
            .Where(x => x.Id == request.PersonnelId)
            .Select(x => new { x.Id, x.CompanyId })
            .SingleOrDefaultAsync(cancellationToken);

        if (personnel is null)
            return BadRequest(new { message = "Personel bulunamadı." });

        var target = await db.Projects
            .AsNoTracking()
            .Where(x => x.Id == request.TargetProjectId)
            .Select(x => new { x.Id, x.Status })
            .SingleOrDefaultAsync(cancellationToken);

        if (target is null)
            return BadRequest(new { message = "Hedef proje bulunamadı." });

        var dutyType = (PersonnelDutyType)request.DutyType;

        // Keşif görevi keşif statüsündeki projeye açılır: aday proje
        // ayrı bir kavram değil, keşif statüsündeki projenin kendisi.
        if (dutyType == PersonnelDutyType.Survey &&
            target.Status != ProjectStatus.Kesif)
        {
            return BadRequest(new
            {
                message = "Keşif görevi ancak keşif statüsündeki bir projeye " +
                          "açılabilir. İş kazanılınca proje aktife alınır ve " +
                          "keşif masrafı olduğu yerde kalır."
            });
        }

        // Çalışma ve ziyaret mevcut işe gider; keşif statüsündeki
        // projede henüz yapılacak iş yok.
        if (dutyType != PersonnelDutyType.Survey &&
            target.Status == ProjectStatus.Kesif)
        {
            return BadRequest(new
            {
                message = "Keşif statüsündeki projeye çalışma ya da ziyaret " +
                          "görevlendirmesi açılamaz; keşif görevi kullanın."
            });
        }

        // ÇAKIŞMA: aynı personelin aynı güne iki onaylı/bekleyen görevi
        // olamaz. Çalışma görevlendirmesinde gün maliyeti tek projeye
        // sayılmalı; ziyaret ve keşifte de kişi aynı anda iki yerde
        // olamaz.
        var overlaps = await db.PersonnelDuties
            .AsNoTracking()
            .AnyAsync(x => x.PersonnelId == request.PersonnelId &&
                           (x.Status == PersonnelDutyStatus.Requested ||
                            x.Status == PersonnelDutyStatus.Approved) &&
                           x.StartDate <= end && x.EndDate >= start,
                cancellationToken);

        if (overlaps)
        {
            return Conflict(new
            {
                message = "Bu personelin aynı tarihlerde bekleyen ya da onaylı " +
                          "başka bir görevlendirmesi var."
            });
        }

        var duty = new PersonnelDuty
        {
            CompanyId = request.CompanyId == Guid.Empty
                ? personnel.CompanyId
                : request.CompanyId,
            PersonnelId = request.PersonnelId,
            DutyType = dutyType,
            TargetProjectId = request.TargetProjectId,
            TargetProjectSiteId = request.TargetProjectSiteId,
            SourceProjectId = request.SourceProjectId,
            StartDate = start,
            EndDate = end,
            IsOutOfCity = request.IsOutOfCity,
            DailyAllowance = request.DailyAllowance,
            Purpose = request.Purpose.Trim(),
            Notes = request.Notes?.Trim(),
            Status = PersonnelDutyStatus.Requested,
            RequestedByUserId = currentUser.UserId,
            RequestedAtUtc = DateTime.UtcNow
        };

        db.PersonnelDuties.Add(duty);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            duty.Id,
            message = "Görevlendirme talebi açıldı; Genel Müdür onayı bekliyor.",
            status = (int)duty.Status
        });
    }

    /// <summary>
    /// Onay. Yalnız GM ve Admin: görevlendirme maliyet ve harcırah
    /// doğurduğu için onay yetkisi izinle değil ROLLE sınırlı.
    /// </summary>
    [HttpPost("{id:guid}/onayla")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelView)]
    public async Task<IActionResult> Approve(
        Guid id, DecidePersonnelDutyRequest request,
        CancellationToken cancellationToken)
    {
        if (!await CanApproveAsync(cancellationToken))
        {
            return StatusCode(403, new
            {
                message = "Görevlendirmeyi yalnızca Genel Müdür onaylayabilir."
            });
        }

        var duty = await db.PersonnelDuties
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (duty is null)
            return NotFound(new { message = "Görevlendirme bulunamadı." });

        if (duty.Status != PersonnelDutyStatus.Requested)
        {
            return BadRequest(new
            {
                message = "Yalnızca talep durumundaki görevlendirme onaylanabilir."
            });
        }

        duty.Status = PersonnelDutyStatus.Approved;
        duty.ApprovedByUserId = currentUser.UserId;
        duty.ApprovedAtUtc = DateTime.UtcNow;
        duty.DecisionNote = request.DecisionNote?.Trim();

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Görevlendirme onaylandı.",
            duty.Id,
            status = (int)duty.Status,
            duty.ApprovedAtUtc
        });
    }

    [HttpPost("{id:guid}/reddet")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelView)]
    public async Task<IActionResult> Reject(
        Guid id, DecidePersonnelDutyRequest request,
        CancellationToken cancellationToken)
    {
        if (!await CanApproveAsync(cancellationToken))
        {
            return StatusCode(403, new
            {
                message = "Görevlendirmeyi yalnızca Genel Müdür reddedebilir."
            });
        }

        var reason = request.DecisionNote?.Trim();

        // Gerekçesiz ret, talebi açana hiçbir bilgi vermez.
        if (string.IsNullOrWhiteSpace(reason))
            return BadRequest(new { message = "Ret gerekçesi zorunludur." });

        var duty = await db.PersonnelDuties
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (duty is null)
            return NotFound(new { message = "Görevlendirme bulunamadı." });

        if (duty.Status != PersonnelDutyStatus.Requested)
        {
            return BadRequest(new
            {
                message = "Yalnızca talep durumundaki görevlendirme reddedilebilir."
            });
        }

        duty.Status = PersonnelDutyStatus.Rejected;
        duty.ApprovedByUserId = currentUser.UserId;
        duty.ApprovedAtUtc = DateTime.UtcNow;
        duty.DecisionNote = reason;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Görevlendirme reddedildi.", duty.Id });
    }

    private async Task<bool> CanApproveAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
            return false;

        var snapshot = await authorization.GetAsync(userId, cancellationToken);

        if (snapshot is null || !snapshot.IsActive)
            return false;

        return snapshot.RoleNames.Any(
            role => ApprovalRoles.Contains(role, StringComparer.OrdinalIgnoreCase));
    }

    private static DateTime ToUtc(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);

    private static int DayCountOf(DateTime start, DateTime end) =>
        end.Date < start.Date ? 0 : (end.Date - start.Date).Days + 1;

    internal static string DutyTypeName(PersonnelDutyType type) => type switch
    {
        PersonnelDutyType.Work => "Çalışma görevlendirmesi",
        PersonnelDutyType.Survey => "Keşif görevi",
        PersonnelDutyType.Visit => "Ziyaret / denetim / görüşme",
        _ => "Bilinmiyor"
    };

    private static string StatusName(PersonnelDutyStatus status) => status switch
    {
        PersonnelDutyStatus.Requested => "Onay bekliyor",
        PersonnelDutyStatus.Approved => "Onaylandı",
        PersonnelDutyStatus.Rejected => "Reddedildi",
        PersonnelDutyStatus.Completed => "Tamamlandı",
        PersonnelDutyStatus.Cancelled => "İptal",
        _ => "Bilinmiyor"
    };
}
