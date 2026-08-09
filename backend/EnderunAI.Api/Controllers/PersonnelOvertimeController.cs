using EnderunAI.Api.Data;
using EnderunAI.Api.Data.HumanResources;
using EnderunAI.Api.Models.HumanResources;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Services.HumanResources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Personel kartının fazla mesai bölümü.
///
/// Hesap YENİDEN YAZILMADI: yıllık kümülatif ve tür kırılımı fazla
/// mesai köprüsündeki kuralın aynısıdır — sayıma yalnızca FAZLA
/// ÇALIŞMA girer, hafta tatili ve genel tatil çalışması yasal sınırın
/// konusu değildir. Saatlik ücret de bordroyla aynı kaynaktan
/// (ActualDailyWageService) okunur.
///
/// GİZLİLİK: saat, döküm ve muvafakat personnel.view ile açıktır —
/// şantiye şefi ve formen kendi ekibinin mesaisini görmeden
/// çalışamaz. TL TUTAR ise yalnızca payroll.view ile döner; sahaya
/// mesai tutarı sızmamalı. Tutar alanları gizlenmez, sorgudan hiç
/// çıkmaz.
/// </summary>
[ApiController]
[Authorize]
[Route("api/hr/personel/{personnelId:guid}/fazla-mesai")]
public sealed class PersonnelOvertimeController(
    AppDbContext db,
    HrDbContext hrDb,
    ActualDailyWageService dailyWage,
    ICurrentUserService currentUser,
    IUserAuthorizationService authorization) : ControllerBase
{
    /// <summary>Sınıra yaklaşma eşiği — köprüdeki uyarıyla aynı.</summary>
    private const decimal NearLimitRatio = 0.9m;

    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.PersonnelView)]
    public async Task<IActionResult> Get(
        Guid personnelId,
        [FromQuery] int? year,
        CancellationToken cancellationToken)
    {
        var targetYear = year ?? DateTime.UtcNow.Year;

        if (targetYear is < 2000 or > 2100)
            return BadRequest(new { message = "Geçersiz yıl." });

        var personnel = await db.Personnel
            .AsNoTracking()
            .Where(x => x.Id == personnelId)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                FullName = x.FirstName + " " + x.LastName,
                x.OvertimeConsentYear,
                x.OvertimeConsentDate
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (personnel is null)
            return NotFound(new { message = "Personel bulunamadı." });

        var yearStart = new DateTime(targetYear, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var yearEnd = new DateTime(targetYear, 12, 31, 0, 0, 0, DateTimeKind.Utc);

        var approved = await hrDb.OvertimeRequests
            .AsNoTracking()
            .Where(x => x.PersonnelId == personnelId &&
                        x.Status == HrApprovalStatus.Approved &&
                        x.ApprovedHours > 0m &&
                        x.WorkDate >= yearStart && x.WorkDate <= yearEnd)
            .OrderByDescending(x => x.WorkDate)
            .Select(x => new
            {
                x.Id,
                x.WorkDate,
                x.ApprovedHours,
                x.IsSundayWork,
                x.IsPublicHolidayWork,
                x.AttendanceRecordId,
                x.Reason,
                x.ApprovedAtUtc
            })
            .ToListAsync(cancellationToken);

        // Köprüdeki eşleme: genel tatil hafta tatilinden önce bakılır.
        static int KindOf(bool sunday, bool holiday) =>
            holiday ? 2 : sunday ? 1 : 0;

        var limit = await db.CompanyPayrollSettings
            .AsNoTracking()
            .Where(x => x.CompanyId == personnel.CompanyId && x.Year == targetYear)
            .Select(x => x.AnnualOvertimeHourLimit)
            .SingleOrDefaultAsync(cancellationToken);

        var overtimeHours = approved
            .Where(x => KindOf(x.IsSundayWork, x.IsPublicHolidayWork) == 0)
            .Sum(x => x.ApprovedHours);

        var sundayHours = approved
            .Where(x => KindOf(x.IsSundayWork, x.IsPublicHolidayWork) == 1)
            .Sum(x => x.ApprovedHours);

        var publicHolidayHours = approved
            .Where(x => KindOf(x.IsSundayWork, x.IsPublicHolidayWork) == 2)
            .Sum(x => x.ApprovedHours);

        var (limitStatus, limitStatusName) = ResolveLimitStatus(limit, overtimeHours);

        // --- Tutar: yalnız payroll.view ---
        var canViewAmounts = await HasPermissionAsync(
            PermissionCatalog.Keys.PayrollView, cancellationToken);

        decimal? hourlyRate = null;
        var multipliers = (Overtime: 1.5m, Sunday: 2m, PublicHoliday: 2m);

        if (canViewAmounts && approved.Count > 0)
        {
            var wage = await dailyWage.ResolveAsync(
                personnelId, approved[0].WorkDate, cancellationToken);

            hourlyRate = wage?.OfficialHourlyRate;

            var card = await hrDb.SalaryDefinitions
                .AsNoTracking()
                .Where(x => x.PersonnelId == personnelId)
                .OrderByDescending(x => x.EffectiveStartDate)
                .Select(x => new
                {
                    x.OvertimeMultiplier,
                    x.SundayMultiplier,
                    x.PublicHolidayMultiplier
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (card is not null)
            {
                multipliers = (card.OvertimeMultiplier, card.SundayMultiplier,
                    card.PublicHolidayMultiplier);
            }
        }

        decimal MultiplierOf(int kind) => kind switch
        {
            2 => multipliers.PublicHoliday,
            1 => multipliers.Sunday,
            _ => multipliers.Overtime
        };

        decimal? AmountOf(int kind, decimal hours) =>
            hourlyRate is decimal rate
                ? decimal.Round(hours * rate * MultiplierOf(kind), 2)
                : null;

        var lines = approved.Select(x =>
        {
            var kind = KindOf(x.IsSundayWork, x.IsPublicHolidayWork);

            return new
            {
                x.Id,
                x.WorkDate,
                hours = x.ApprovedHours,
                kind,
                kindName = KindName(kind),
                multiplier = MultiplierOf(kind),
                // Puantaja düşmediyse (ör. o günün puantajı onaylıydı)
                // saat bordroya girmez; kartta görünmesi gerekir.
                landedOnAttendance = x.AttendanceRecordId != null,
                attendanceMonth = x.AttendanceRecordId != null
                    ? x.WorkDate.ToString("yyyy-MM")
                    : null,
                x.Reason,
                x.ApprovedAtUtc,
                amount = AmountOf(kind, x.ApprovedHours)
            };
        }).ToList();

        return Ok(new
        {
            personnelId = personnel.Id,
            personnelName = personnel.FullName,
            year = targetYear,

            annualLimit = limit,
            overtimeHours,
            sundayHours,
            publicHolidayHours,
            limitStatus,
            limitStatusName,
            // Tatil çalışması sınır sayımına GİRMEZ; ekranda bunun
            // yazması gerekiyor ki toplam farkı soru işareti olmasın.
            limitCountsOvertimeOnly = true,

            consent = new
            {
                year = personnel.OvertimeConsentYear,
                date = personnel.OvertimeConsentDate,
                isValid = personnel.OvertimeConsentYear == targetYear
            },

            amountsHidden = !canViewAmounts,
            totalAmount = canViewAmounts
                ? lines.Sum(x => x.amount ?? 0m)
                : (decimal?)null,

            notLandedCount = lines.Count(x => !x.landedOnAttendance),
            lines
        });
    }

    /// <summary>
    /// Sınır durumu. Sınır tanımsızsa "belirsiz" döner — koda gömülü
    /// bir 270 varsayılmaz; bordro ön kontrolüyle aynı dil.
    /// </summary>
    private static (string Status, string Name) ResolveLimitStatus(
        decimal? limit, decimal overtimeHours)
    {
        if (limit is not decimal cap || cap <= 0m)
            return ("undefined", "Yıllık sınır girilmedi");

        if (overtimeHours > cap)
            return ("exceeded", "Yıllık sınır aşıldı");

        if (overtimeHours >= cap * NearLimitRatio)
            return ("near", "Yıllık sınıra yaklaşıldı");

        return ("ok", "Sınır içinde");
    }

    private static string KindName(int kind) => kind switch
    {
        2 => "Genel tatil çalışması",
        1 => "Hafta tatili çalışması",
        _ => "Fazla çalışma"
    };

    /// <summary>
    /// Birden çok RequirePermission VEYA anlamına geldiği için ikinci
    /// koşul kod içinde denetleniyor.
    /// </summary>
    private async Task<bool> HasPermissionAsync(
        string permissionKey, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
            return false;

        var snapshot = await authorization.GetAsync(userId, cancellationToken);

        if (snapshot is null || !snapshot.IsActive)
            return false;

        if (snapshot.RoleNames.Contains("Admin", StringComparer.OrdinalIgnoreCase))
            return true;

        return snapshot.Permissions.Contains(
            permissionKey, StringComparer.OrdinalIgnoreCase);
    }
}
