using EnderunAI.Api.Data.HumanResources;
using EnderunAI.Api.Services.HumanResources;
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

public sealed record SaveDutyExpenseRequest(
    decimal TravelCost,
    decimal AccommodationCost,
    decimal ReceiptAmount);

public sealed record ReviseDutyAllowanceRequest(
    decimal DailyAllowance,
    string? Note);

public sealed record SettleDutyRequest(
    /// <summary>0 personelden düş · 1 gider kabul et.</summary>
    int Decision,
    string? Note,
    /// <summary>Personelden düşmede taksit sayısı.</summary>
    int InstallmentCount = 1);

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
    HrDbContext hrDb,
    DutyExpensePostingService expensePosting,
    ICurrentUserService currentUser,
    IUserAuthorizationService authorization,
    IExtraPaymentVisibilityService extraPaymentVisibility) : ControllerBase
{
    /// <summary>Görevi onaylayabilen roller.</summary>
    private static readonly string[] ApprovalRoles = ["Admin", "Genel Müdür"];

    /// <summary>
    /// Tutar YAZMA kapısı: personnel.edit yetmiyor, ek ödeme yetkisi
    /// de aranıyor.
    ///
    /// Görmediği bir tutarı yazabilen kullanıcı, yazdığının doğru
    /// olup olmadığını da denetleyemez — kaydettiği rakamı bir daha
    /// göremez, yanlışını fark edemez. Yazma ile görme aynı kapıda
    /// olmalı. Görevin kendisini açmak bu kapıya tabi DEĞİL: talebi
    /// İК açar, tutarı yetkili girer.
    /// </summary>
    private async Task<bool> CanWriteAmountsAsync(CancellationToken cancellationToken) =>
        await extraPaymentVisibility.CanViewExtraPaymentAsync(cancellationToken);

    private ObjectResult AmountWriteForbidden() => StatusCode(403, new
    {
        message = "Harcırah ve masraf tutarlarını yalnızca ek ödeme " +
                  "yetkisi olan kullanıcı girebilir."
    });

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
                x.ReceiptAmount,
                SettlementDecided = x.SettlementDecision != null,
                x.Purpose,
                Status = (int)x.Status,
                // Rapor yazıldı mı: ekran keşif satırında "rapor
                // bekliyor" uyarısını buradan çıkarıyor.
                HasSurveyReport = db.DutySurveyReports
                    .Any(r => r.DutyId == x.Id),
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
            // Mahsup bekliyor bilgisi de mali bir durum: "bu kişiye
            // fark borcu var" demektir, tutar kadar olmasa da
            // maskelemeye tabi.
            settlementPending = canViewAmounts
                ? x.Status == (int)PersonnelDutyStatus.Approved &&
                  !x.SettlementDecided &&
                  x.DailyAllowance * DayCountOf(x.StartDate, x.EndDate) >
                      x.ReceiptAmount
                : (bool?)null,
            x.HasSurveyReport,
            x.RequestedAtUtc,
            x.ApprovedAtUtc,
            x.DecisionNote
        }));
    }

    /// <summary>
    /// Tek görevin tümü: masraf kalemleri, fiş ve mahsup durumu.
    ///
    /// Listede taşınmıyor çünkü liste her satırda masraf okumak
    /// zorunda kalırdı; ekran yalnızca açtığı görevin detayını
    /// ister. Tutarlar burada da extra_payment.view'e tabi.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelView)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var duty = await db.PersonnelDuties
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                x.PersonnelId,
                PersonnelFullName = x.Personnel.FirstName + " " + x.Personnel.LastName,
                DutyType = (int)x.DutyType,
                x.TargetProjectId,
                TargetProjectCode = x.TargetProject.Code,
                TargetProjectName = x.TargetProject.Name,
                TargetProjectStatus = (int)x.TargetProject.Status,
                TargetProjectSurveyOutcome = (int)x.TargetProject.SurveyOutcome,
                x.TargetProjectSiteId,
                x.SourceProjectId,
                x.StartDate,
                x.EndDate,
                x.IsOutOfCity,
                x.DailyAllowance,
                x.TravelCost,
                x.AccommodationCost,
                x.ReceiptAmount,
                x.Purpose,
                x.Notes,
                Status = (int)x.Status,
                x.AllowanceRevisedAtUtc,
                x.AllowanceRevisionNote,
                SettlementDecision = (int?)x.SettlementDecision,
                x.SettlementNote,
                x.SettlementAtUtc,
                x.SettlementAdvanceId,
                x.RequestedAtUtc,
                x.ApprovedAtUtc,
                x.DecisionNote,
                HasSurveyReport = db.DutySurveyReports.Any(r => r.DutyId == x.Id)
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (duty is null)
            return NotFound(new { message = "Görevlendirme bulunamadı." });

        var canViewAmounts = await extraPaymentVisibility
            .CanViewExtraPaymentAsync(cancellationToken);

        var dayCount = DayCountOf(duty.StartDate, duty.EndDate);
        var totalAllowance = duty.DailyAllowance * dayCount;
        var gap = Math.Max(0m, totalAllowance - duty.ReceiptAmount);

        return Ok(new
        {
            duty.Id,
            duty.CompanyId,
            duty.PersonnelId,
            duty.PersonnelFullName,
            duty.DutyType,
            dutyTypeName = DutyTypeName((PersonnelDutyType)duty.DutyType),
            shiftsLaborCost =
                (PersonnelDutyType)duty.DutyType == PersonnelDutyType.Work,
            duty.TargetProjectId,
            duty.TargetProjectCode,
            duty.TargetProjectName,
            duty.TargetProjectStatus,
            duty.TargetProjectSurveyOutcome,
            duty.TargetProjectSiteId,
            duty.SourceProjectId,
            duty.StartDate,
            duty.EndDate,
            dayCount,
            duty.IsOutOfCity,
            duty.Purpose,
            duty.Notes,
            duty.Status,
            statusName = StatusName((PersonnelDutyStatus)duty.Status),
            duty.RequestedAtUtc,
            duty.ApprovedAtUtc,
            duty.DecisionNote,
            duty.HasSurveyReport,

            // Tutarlar gizlenmiyor, hiç gelmiyor.
            amountsHidden = !canViewAmounts,
            dailyAllowance = canViewAmounts ? duty.DailyAllowance : (decimal?)null,
            totalAllowance = canViewAmounts ? totalAllowance : (decimal?)null,
            travelCost = canViewAmounts ? duty.TravelCost : (decimal?)null,
            accommodationCost = canViewAmounts ? duty.AccommodationCost : (decimal?)null,
            receiptAmount = canViewAmounts ? duty.ReceiptAmount : (decimal?)null,
            totalExpense = canViewAmounts
                ? duty.TravelCost + duty.AccommodationCost + totalAllowance
                : (decimal?)null,
            settlementGap = canViewAmounts ? gap : (decimal?)null,
            settlementPending = canViewAmounts
                ? duty.Status == (int)PersonnelDutyStatus.Approved &&
                  gap > 0m && duty.SettlementDecision is null
                : (bool?)null,
            settlementDecision = canViewAmounts ? duty.SettlementDecision : null,
            settlementNote = canViewAmounts ? duty.SettlementNote : null,
            settlementAtUtc = canViewAmounts ? duty.SettlementAtUtc : null,
            settlementAdvanceId = canViewAmounts ? duty.SettlementAdvanceId : null,

            // Düzeltme izi de tutar bilgisidir: "harcırah sonradan
            // değiştirildi" demek, değişen rakamı görebilecek kişiye
            // ait bir bilgi.
            allowanceRevisedAtUtc = canViewAmounts
                ? duty.AllowanceRevisedAtUtc
                : null,
            allowanceRevisionNote = canViewAmounts
                ? duty.AllowanceRevisionNote
                : null,

            // Ekran düğmeyi buna göre çiziyor; kapı yine uçta.
            canWriteAmounts = canViewAmounts
        });
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

        // Talebi İК açar ama tutarı yetkili girer: yetkisiz kullanıcının
        // yazdığı harcırah SESSİZCE DÜŞMEZ, hiç alınmaz ve cevapta
        // söylenir. Görev sıfır harcırahla açılır, yetkili sonradan
        // harcırah ucundan düzeltir.
        var canWriteAmounts = await CanWriteAmountsAsync(cancellationToken);
        var allowance = canWriteAmounts ? request.DailyAllowance : 0m;
        var allowanceDeferred = !canWriteAmounts && request.DailyAllowance > 0m;

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
            DailyAllowance = allowance,
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
            message = allowanceDeferred
                ? "Görevlendirme talebi açıldı; Genel Müdür onayı bekliyor. " +
                  "Harcırah tutarı kaydedilmedi — girmek için ek ödeme " +
                  "yetkisi gerekiyor, yetkili kullanıcı harcırahı sonradan " +
                  "girecek."
                : "Görevlendirme talebi açıldı; Genel Müdür onayı bekliyor.",
            status = (int)duty.Status,
            allowanceDeferred
        });
    }

    /// <summary>
    /// Harcırah düzeltme.
    ///
    /// Tutar görev kartında sabit tutulduğu için yanlış girilen bir
    /// harcırahın tek çaresi görevi iptal edip yeniden açmaktı; bu da
    /// onay izini ve varsa masraf satırlarını çöpe atıyordu. Düzeltme
    /// İZ BIRAKARAK yapılır: kim, ne zaman, hangi gerekçeyle.
    ///
    /// MAHSUP SONRASI KAPALI: fark bir kez karara bağlandıysa
    /// (personelden kesildiyse ya da gider kabul edildiyse) harcırahın
    /// geriye dönük değişmesi, kapanmış bir hesabı sessizce açardı.
    /// Bu durumda düzeltme reddedilir.
    /// </summary>
    [HttpPost("{id:guid}/harcirah")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelEdit)]
    public async Task<IActionResult> ReviseAllowance(
        Guid id, ReviseDutyAllowanceRequest request,
        CancellationToken cancellationToken)
    {
        if (!await CanWriteAmountsAsync(cancellationToken))
            return AmountWriteForbidden();

        if (request.DailyAllowance < 0m)
            return BadRequest(new { message = "Harcırah negatif olamaz." });

        var note = request.Note?.Trim();

        if (string.IsNullOrWhiteSpace(note))
            return BadRequest(new { message = "Düzeltme gerekçesi zorunludur." });

        var duty = await db.PersonnelDuties
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (duty is null)
            return NotFound(new { message = "Görevlendirme bulunamadı." });

        if (duty.Status is not (PersonnelDutyStatus.Requested or
            PersonnelDutyStatus.Approved))
        {
            return BadRequest(new
            {
                message = "Yalnızca talep ya da onay durumundaki görevde " +
                          "harcırah düzeltilir."
            });
        }

        if (duty.SettlementDecision is not null)
        {
            return Conflict(new
            {
                message = "Mahsubu karara bağlanmış görevin harcırahı " +
                          "değiştirilemez; kapanmış hesabı geriye dönük açardı."
            });
        }

        var previous = duty.DailyAllowance;

        duty.DailyAllowance = request.DailyAllowance;
        duty.AllowanceRevisedAtUtc = DateTime.UtcNow;
        duty.AllowanceRevisedByUserId = currentUser.UserId;
        duty.AllowanceRevisionNote = note;

        // Onaylı görevde defterdeki harcırah satırı da yeni tutara
        // çekilir; satır güncellenir, ikincisi açılmaz.
        await expensePosting.PostAsync(duty, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Harcırah düzeltildi.",
            duty.Id,
            previousDailyAllowance = previous,
            dailyAllowance = duty.DailyAllowance,
            totalAllowance = duty.TotalAllowance,
            settlementGap = duty.SettlementGap,
            settlementPending = duty.SettlementPending
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

        // Masraf onayla birlikte hedef projeye yansır; onaysız görev
        // maliyet üretmez.
        await expensePosting.PostAsync(duty, cancellationToken);

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

    /// <summary>
    /// Masraf kalemleri: yol, konaklama ve fiş toplamı. Harcırah
    /// buradan girilmez — görev kartındaki sabit tutardan hesaplanır.
    /// </summary>
    [HttpPost("{id:guid}/masraf")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelEdit)]
    public async Task<IActionResult> SaveExpense(
        Guid id, SaveDutyExpenseRequest request, CancellationToken cancellationToken)
    {
        if (!await CanWriteAmountsAsync(cancellationToken))
            return AmountWriteForbidden();

        if (request.TravelCost < 0m || request.AccommodationCost < 0m ||
            request.ReceiptAmount < 0m)
        {
            return BadRequest(new { message = "Tutarlar negatif olamaz." });
        }

        var duty = await db.PersonnelDuties
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (duty is null)
            return NotFound(new { message = "Görevlendirme bulunamadı." });

        duty.TravelCost = request.TravelCost;
        duty.AccommodationCost = request.AccommodationCost;
        duty.ReceiptAmount = request.ReceiptAmount;

        // Onaylı görevde defter güncellenir; onaysızda hiçbir şey
        // yansımaz. Yeniden yansıtma satırı günceller, ikincisini
        // açmaz.
        await expensePosting.PostAsync(duty, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Görev masrafı kaydedildi.",
            duty.Id,
            travelCost = duty.TravelCost,
            accommodationCost = duty.AccommodationCost,
            allowance = duty.TotalAllowance,
            totalExpense = duty.TotalExpense,
            receiptAmount = duty.ReceiptAmount,
            settlementGap = duty.SettlementGap,
            settlementPending = duty.SettlementPending
        });
    }

    /// <summary>
    /// Harcırah mahsubu: fişle karşılanmayan fark için karar.
    ///
    /// Sabit kural yok — fark ya personelden düşülür ya şirket gideri
    /// kabul edilir; ikisi de meşru ve karar GM/İK'nın. Ama karar
    /// KAYIT ALTINA alınır: kim, ne zaman, hangi gerekçeyle.
    ///
    /// "Personelden düş" seçilirse kesinti YENİ BİR YOLDAN değil,
    /// bordroda zaten çalışan avans zincirinden yürür. Kayıt
    /// "Harcırah Mahsubu" etiketiyle açılır ki personelin gerçek avans
    /// talepleriyle karışmasın.
    /// </summary>
    [HttpPost("{id:guid}/mahsup")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelEdit)]
    public async Task<IActionResult> Settle(
        Guid id, SettleDutyRequest request, CancellationToken cancellationToken)
    {
        if (!await CanWriteAmountsAsync(cancellationToken))
            return AmountWriteForbidden();

        if (!Enum.IsDefined(typeof(DutySettlementDecision), request.Decision))
            return BadRequest(new { message = "Geçersiz mahsup kararı." });

        var note = request.Note?.Trim();

        if (string.IsNullOrWhiteSpace(note))
            return BadRequest(new { message = "Mahsup gerekçesi zorunludur." });

        var duty = await db.PersonnelDuties
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (duty is null)
            return NotFound(new { message = "Görevlendirme bulunamadı." });

        if (duty.Status != PersonnelDutyStatus.Approved)
        {
            return BadRequest(new
            {
                message = "Yalnızca onaylı görevlendirmede mahsup yapılır."
            });
        }

        if (duty.SettlementGap <= 0m)
        {
            return BadRequest(new
            {
                message = "Mahsup edilecek fark yok: fişler harcırahı karşılıyor."
            });
        }

        if (duty.SettlementDecision is not null)
        {
            return Conflict(new
            {
                message = "Bu görevin mahsubu zaten karara bağlanmış."
            });
        }

        var decision = (DutySettlementDecision)request.Decision;

        duty.SettlementDecision = decision;
        duty.SettlementNote = note;
        duty.SettlementByUserId = currentUser.UserId;
        duty.SettlementAtUtc = DateTime.UtcNow;

        if (decision == DutySettlementDecision.DeductFromPersonnel)
        {
            var installments = request.InstallmentCount < 1
                ? 1
                : request.InstallmentCount;

            // Avans zinciri: kesinti bordroda zaten çalışan tek
            // yoldan yürür. Etiket kaynağı ayırıyor.
            var advance = new Models.HumanResources.HrAdvanceRequest
            {
                CompanyId = duty.CompanyId,
                PersonnelId = duty.PersonnelId,
                ProjectId = duty.TargetProjectId,
                RequestDate = DateTime.UtcNow.Date,
                RequestedAmount = duty.SettlementGap,
                ApprovedAmount = duty.SettlementGap,
                CurrencyCode = "TRY",
                DeductionInstallmentCount = installments,
                Reason = $"Harcırah Mahsubu — {duty.Purpose}",
                Status = Models.HumanResources.HrApprovalStatus.Paid,
                ApprovedByUserId = currentUser.UserId,
                ApprovedAtUtc = DateTime.UtcNow,
                PaidAtUtc = DateTime.UtcNow
            };

            hrDb.AdvanceRequests.Add(advance);
            await hrDb.SaveChangesAsync(cancellationToken);

            duty.SettlementAdvanceId = advance.Id;
        }

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = decision == DutySettlementDecision.DeductFromPersonnel
                ? "Fark personelden kesilmek üzere harcırah mahsubu açıldı."
                : "Fark şirket gideri olarak kabul edildi.",
            duty.Id,
            settlementGap = duty.SettlementGap,
            decision = (int)decision,
            duty.SettlementAdvanceId
        });
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
