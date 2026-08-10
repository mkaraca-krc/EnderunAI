using EnderunAI.Api.Formatting;
using EnderunAI.Api.Data;
using EnderunAI.Api.Data.HumanResources;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.HumanResources;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.HumanResources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Bordro öncesi ön kontrol ve SGK bildirim dökümü.
///
/// ÖN KONTROL: bordro bugün eksik verili personeli sessizce içine alıp
/// üretiyor. Sorun ancak bordro çıktıktan ve resmî bildirim
/// reddedildikten sonra görülüyor; o noktada bordronun iptal edilip
/// yeniden üretilmesi gerekiyor. Bu uç, üretmeden ÖNCE neyin eksik
/// olduğunu söylüyor.
///
/// SGK DÖKÜMÜ: bildirim SGK'nın kendi ekranına ELLE giriliyor. Bu
/// yüzden dosya biçimi üretilmiyor; girişte gereken alanlar
/// eksiksiz ve kopyalanabilir biçimde listeleniyor, eksik alanı
/// olanlar ayrıca işaretleniyor. Bildirimin yapılıp yapılmadığı,
/// özlük dosyasına yüklenen bildirge belgesinden okunuyor — ikinci bir
/// "bildirildi" bayrağı tutmak, kimsenin güncellemediği bir alan
/// üretirdi.
/// </summary>
[ApiController]
[Authorize]
[Route("api/hr")]
public sealed class PayrollReadinessController(AppDbContext db, HrDbContext hrDb)
    : ControllerBase
{
    // ---------------- Bordro ön kontrolü ----------------

    [HttpGet("bordro-on-kontrol")]
    [RequirePermission(PermissionCatalog.Keys.AttendancePayrollView)]
    public async Task<IActionResult> PayrollReadiness(
        [FromQuery] Guid companyId,
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken cancellationToken)
    {
        if (month is < 1 or > 12)
            return BadRequest(new { message = "Geçersiz ay." });

        var personnel = await db.Personnel
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.IsActive &&
                        x.Status == PersonnelStatus.Active)
            .Select(x => new
            {
                x.Id,
                x.EmployeeNumber,
                FullName = x.FirstName + " " + x.LastName,
                x.IdentityNumber,
                x.BirthDate,
                x.Phone,
                x.SgkRegistrationNumber,
                x.EmploymentStartDate,
                x.JobTitle,
                x.BranchId,
                WorkLocationType = (int)x.WorkLocationType,
                HasActiveSiteAssignment = x.SiteAssignments.Any(a => a.EndDate == null)
            })
            .ToListAsync(cancellationToken);

        var ids = personnel.Select(x => x.Id).ToList();

        // Dönem sonunda yürürlükte olan kart: süresi geçmiş kart, kart
        // yokmuş gibi bordroyu engeller.
        var periodEnd = new DateTime(
            year, month, DateTime.DaysInMonth(year, month), 0, 0, 0, DateTimeKind.Utc);

        var withSalaryCard = (await hrDb.SalaryDefinitions
            .AsNoTracking()
            .Where(x => ids.Contains(x.PersonnelId) &&
                        x.EffectiveStartDate <= periodEnd &&
                        (x.EffectiveEndDate == null || x.EffectiveEndDate >= periodEnd))
            .Select(x => x.PersonnelId)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var completeness = PersonnelDataCompletenessCalculator.Summarize(
            personnel
                .Select(x => new PersonnelDataInput(
                    x.Id, x.EmployeeNumber, x.FullName, x.IdentityNumber,
                    x.BirthDate, x.Phone, x.SgkRegistrationNumber,
                    x.EmploymentStartDate, x.JobTitle, x.BranchId,
                    x.WorkLocationType, x.HasActiveSiteAssignment,
                    withSalaryCard.Contains(x.Id)))
                .ToList());

        // --- Puantaj durumu ---
        var periodStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);

        var attendance = await db.AttendanceRecords
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.WorkDate >= periodStart && x.WorkDate <= periodEnd)
            .Select(x => new { x.IsApproved })
            .ToListAsync(cancellationToken);

        // --- Parametre ve takvim ---
        var settings = await db.CompanyPayrollSettings
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Year == year)
            .Select(x => new
            {
                x.VerifiedAtUtc,
                x.AnnualOvertimeHourLimit,
                x.MealSgkExemptionDailyCap,
                x.MealIncomeTaxExemptionDailyCap,
                x.TravelSgkExemptionDailyCap,
                x.TravelIncomeTaxExemptionDailyCap
            })
            .SingleOrDefaultAsync(cancellationToken);

        var calendarVerified = await db.CompanyHolidayCalendars
            .AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId && x.Year == year &&
                           x.VerifiedAtUtc != null, cancellationToken);

        var blockers = new List<string>();
        var warnings = new List<string>();

        if (settings is null)
        {
            blockers.Add(
                $"{year} yılı bordro parametreleri tanımlı değil; bordro " +
                "hesaplanamaz.");
        }
        else if (settings.VerifiedAtUtc is null)
        {
            blockers.Add(
                $"{year} bordro parametreleri doğrulanmamış. Asgari ücret, SGK " +
                "taban/tavan ve vergi dilimleri onaylanmadan bordro üretilmez.");
        }

        var cannotEnter = completeness.Total - completeness.PayrollReadyCount;

        if (cannotEnter > 0)
        {
            blockers.Add(
                $"{cannotEnter} personelin dönemde yürürlükte ücret kartı yok; " +
                "bordroya hiç giremezler.");
        }

        var notOfficialReady = completeness.PayrollReadyCount -
                               completeness.OfficialReadyCount;

        if (notOfficialReady > 0)
        {
            warnings.Add(
                $"{notOfficialReady} personel eksik veriyle bordroya girecek " +
                "(kimlik, SGK sicil, doğum ya da işe giriş tarihi). Bordro " +
                "üretilir ama resmî bildirim yapılamaz.");
        }

        if (attendance.Count == 0)
        {
            warnings.Add(
                "Bu dönemde hiç puantaj kaydı yok. Bordro üretilir ama fazla " +
                "mesai ve tatil çalışması ücrete dönüşmez.");
        }
        else if (attendance.Count(x => !x.IsApproved) > 0)
        {
            warnings.Add(
                $"{attendance.Count(x => !x.IsApproved)} puantaj günü onaylanmamış; " +
                "yalnızca onaylı günler bordroya girer.");
        }

        // Nakdî yemek/yol yardımı olup istisna tavanı tanımlanmamışsa
        // kalemin tamamı matraha girer: bordro çıkar ama fazla vergi ve
        // fazla prim hesaplanmış olur. Bunu bordro üretildikten sonra
        // fark etmek, bordronun iptal edilip yeniden üretilmesi demek.
        var cappedComponents = await db.HrCompensationComponents
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.IsActive &&
                        x.IncludeInPayroll &&
                        !x.IsInKindBenefit &&
                        x.PaymentMethod != 1 &&
                        (x.ComponentType == 2 || x.ComponentType == 3) &&
                        x.EffectiveStartDate <= periodEnd &&
                        (x.EffectiveEndDate == null || x.EffectiveEndDate >= periodStart))
            .Select(x => new
            {
                x.ComponentType,
                x.IncludeInSgkBase,
                x.IncludeInIncomeTaxBase
            })
            .ToListAsync(cancellationToken);

        foreach (var (componentType, label) in
                 new[] { (3, "yemek"), (2, "yol") })
        {
            var rows = cappedComponents
                .Where(x => x.ComponentType == componentType)
                .ToList();

            if (rows.Count == 0) continue;

            var sgkCap = componentType == 3
                ? settings?.MealSgkExemptionDailyCap
                : settings?.TravelSgkExemptionDailyCap;

            var incomeTaxCap = componentType == 3
                ? settings?.MealIncomeTaxExemptionDailyCap
                : settings?.TravelIncomeTaxExemptionDailyCap;

            if (rows.Any(x => !x.IncludeInSgkBase) && sgkCap is null)
            {
                warnings.Add(
                    $"{year} yılı için {label} yardımının günlük SGK istisna " +
                    "tavanı tanımlanmadı; istisna uygulanmayacak ve kalemin " +
                    "tamamı prime esas kazanca girecek. Şirket Ayarları → " +
                    "Bordro Parametreleri ekranından girin.");
            }

            if (rows.Any(x => !x.IncludeInIncomeTaxBase) && incomeTaxCap is null)
            {
                warnings.Add(
                    $"{year} yılı için {label} yardımının günlük gelir vergisi " +
                    "istisna tavanı tanımlanmadı; istisna uygulanmayacak ve " +
                    "kalemin tamamı vergi matrahına girecek. Şirket Ayarları → " +
                    "Bordro Parametreleri ekranından girin.");
            }
        }

        // --- Fazla mesai: yıllık sınır ve muvafakat ---
        //
        // İkisi de ENGEL DEĞİL uyarı: bordro üretilir, ama onaylayan
        // yasal riski bordro çıkmadan görür.
        var requestOvertime = await hrDb.OvertimeRequests
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.Status == HrApprovalStatus.Approved &&
                        !x.IsSundayWork && !x.IsPublicHolidayWork &&
                        x.WorkDate.Year == year &&
                        x.ApprovedHours > 0m)
            .Select(x => new { x.PersonnelId, x.WorkDate, Hours = x.ApprovedHours })
            .ToListAsync(cancellationToken);

        // Mesai iki yoldan giriliyor: talep ve puantaj cetveli. Yalnız
        // talepler sayılsaydı cetvelden girilen saat yasal sınıra ve
        // muvafakat uyarısına hiç görünmezdi — bordro o saati ödediği
        // halde.
        //
        // ÇİFT SAYIM YOK: talebin sahiplendiği gün cetvelde kilitli;
        // burada da o günler cetvel tarafından eleniyor.
        var requestDays = requestOvertime
            .Select(x => (x.PersonnelId, x.WorkDate.Date))
            .ToHashSet();

        var sheetOvertime = (await db.AttendanceRecords
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.WorkDate.Year == year &&
                        x.OvertimeHours > 0m)
            .Select(x => new { x.PersonnelId, x.WorkDate, Hours = x.OvertimeHours })
            .ToListAsync(cancellationToken))
            .Where(x => !requestDays.Contains((x.PersonnelId, x.WorkDate.Date)))
            .ToList();

        var overtimeByPersonnel = requestOvertime
            .Concat(sheetOvertime)
            .GroupBy(x => x.PersonnelId)
            .Select(g => new { PersonnelId = g.Key, Hours = g.Sum(x => x.Hours) })
            .ToList();

        if (overtimeByPersonnel.Count > 0)
        {
            if (settings?.AnnualOvertimeHourLimit is decimal overtimeLimit &&
                overtimeLimit > 0m)
            {
                var exceeding = overtimeByPersonnel
                    .Count(x => x.Hours > overtimeLimit);

                if (exceeding > 0)
                {
                    warnings.Add(
                        $"{exceeding} personelin {year} yılı onaylı fazla mesaisi " +
                        $"yıllık sınırı ({TurkishFormat.Amount(overtimeLimit)} saat) " +
                        "aşıyor.");
                }
            }
            else
            {
                warnings.Add(
                    $"{year} yılı için yıllık fazla mesai sınırı tanımlanmadı; " +
                    "aşım kontrolü yapılamadı. Şirket Ayarları → Bordro " +
                    "Parametreleri ekranından girin.");
            }

            // Muvafakati olmayan personele mesai ödemesi çıkıyor mu.
            var overtimeIds = overtimeByPersonnel
                .Select(x => x.PersonnelId)
                .ToList();

            var withoutConsent = await db.Personnel
                .AsNoTracking()
                .Where(x => overtimeIds.Contains(x.Id) &&
                            (x.OvertimeConsentYear == null ||
                             x.OvertimeConsentYear != year))
                .CountAsync(cancellationToken);

            if (withoutConsent > 0)
            {
                warnings.Add(
                    $"{withoutConsent} personelin {year} yılı fazla mesai " +
                    "muvafakati yok ama onaylı mesaisi bordroya girecek. " +
                    "Yıllık yazılı onay personel kartından işaretlenir.");
            }
        }

        if (!calendarVerified)
        {
            warnings.Add(
                $"{year} resmî tatil takvimi doğrulanmamış; puantaj cetveli " +
                "takvimden doldurulamaz.");
        }

        return Ok(new
        {
            year,
            month,
            personnelCount = completeness.Total,
            payrollReadyCount = completeness.PayrollReadyCount,
            officialReadyCount = completeness.OfficialReadyCount,
            attendanceRecordCount = attendance.Count,
            approvedAttendanceCount = attendance.Count(x => x.IsApproved),
            holidayCalendarVerified = calendarVerified,
            settingsVerified = settings?.VerifiedAtUtc is not null,
            mealTravelExemptionCapsDefined =
                settings?.MealSgkExemptionDailyCap is not null &&
                settings?.MealIncomeTaxExemptionDailyCap is not null &&
                settings?.TravelSgkExemptionDailyCap is not null &&
                settings?.TravelIncomeTaxExemptionDailyCap is not null,
            canCalculate = blockers.Count == 0,
            blockers,
            warnings,
            // Adı geçen kişiler: kullanıcı sayıyı görüp kimin eksik
            // olduğunu aramak zorunda kalmasın.
            blocked = completeness.Items
                .Where(x => !x.PayrollReady)
                .Select(x => new { x.PersonnelId, x.EmployeeNumber, x.FullName }),
            incomplete = completeness.Items
                .Where(x => x.PayrollReady && !x.OfficialReady)
                .Select(x => new
                {
                    x.PersonnelId,
                    x.EmployeeNumber,
                    x.FullName,
                    MissingFields = x.Issues
                        .Where(i => i.Severity == PersonnelDataSeverity.OfficialBlocking)
                        .Select(i => i.Label)
                })
        });
    }

    // ---------------- SGK bildirim dökümü ----------------

    /// <summary>
    /// SGK'ya elle girilecek işe giriş / çıkış bildirimlerinin dökümü.
    ///
    /// Dosya biçimi ÜRETİLMİYOR: bildirim SGK'nın kendi ekranına elle
    /// giriliyor. Buradaki liste, o ekranda gereken alanları eksiksiz
    /// veriyor ve eksik alanı olanı ayrıca işaretliyor.
    /// </summary>
    [HttpGet("sgk-bildirim")]
    [RequirePermission(PermissionCatalog.Keys.AttendancePayrollView)]
    public async Task<IActionResult> SgkNotifications(
        [FromQuery] Guid companyId,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        CancellationToken cancellationToken)
    {
        if (to < from)
            return BadRequest(new { message = "Bitiş tarihi başlangıçtan önce olamaz." });

        var start = DateTime.SpecifyKind(from.Date, DateTimeKind.Utc);
        var end = DateTime.SpecifyKind(to.Date, DateTimeKind.Utc);

        var entries = await db.Personnel
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.EmploymentStartDate >= start &&
                        x.EmploymentStartDate <= end)
            .Select(x => new
            {
                x.Id,
                x.EmployeeNumber,
                FullName = x.FirstName + " " + x.LastName,
                x.IdentityNumber,
                x.BirthDate,
                x.SgkRegistrationNumber,
                Date = x.EmploymentStartDate,
                x.JobTitle
            })
            .ToListAsync(cancellationToken);

        var exits = await db.Set<PersonnelTermination>()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.TerminationDate >= start &&
                        x.TerminationDate <= end)
            .Select(x => new
            {
                x.PersonnelId,
                x.Personnel.EmployeeNumber,
                FullName = x.Personnel.FirstName + " " + x.Personnel.LastName,
                x.Personnel.IdentityNumber,
                x.Personnel.BirthDate,
                x.Personnel.SgkRegistrationNumber,
                Date = x.TerminationDate,
                x.Reason,
                x.Status
            })
            .ToListAsync(cancellationToken);

        var personnelIds = entries.Select(x => x.Id)
            .Concat(exits.Select(x => x.PersonnelId))
            .Distinct()
            .ToList();

        // Bildirimin yapılıp yapılmadığı özlük dosyasındaki bildirge
        // belgesinden okunuyor; ayrı bir "bildirildi" bayrağı tutmak
        // kimsenin güncellemediği bir alan üretirdi.
        var notices = await db.PersonnelDocuments
            .AsNoTracking()
            .Where(x => personnelIds.Contains(x.PersonnelId) &&
                        (x.DocumentType == PersonnelDocumentType.SgkEntryNotice ||
                         x.DocumentType == PersonnelDocumentType.SgkExitNotice))
            .Select(x => new { x.PersonnelId, x.DocumentType })
            .ToListAsync(cancellationToken);

        var entryNotices = notices
            .Where(x => x.DocumentType == PersonnelDocumentType.SgkEntryNotice)
            .Select(x => x.PersonnelId)
            .ToHashSet();

        var exitNotices = notices
            .Where(x => x.DocumentType == PersonnelDocumentType.SgkExitNotice)
            .Select(x => x.PersonnelId)
            .ToHashSet();

        return Ok(new
        {
            from = start,
            to = end,
            entries = entries.Select(x => new
            {
                x.Id,
                x.EmployeeNumber,
                x.FullName,
                x.IdentityNumber,
                x.BirthDate,
                x.SgkRegistrationNumber,
                x.Date,
                x.JobTitle,
                MissingFields = MissingFor(
                    x.IdentityNumber, x.BirthDate, x.SgkRegistrationNumber),
                NoticeUploaded = entryNotices.Contains(x.Id)
            }),
            exits = exits.Select(x => new
            {
                PersonnelId = x.PersonnelId,
                x.EmployeeNumber,
                x.FullName,
                x.IdentityNumber,
                x.BirthDate,
                x.SgkRegistrationNumber,
                x.Date,
                Reason = (int)x.Reason,
                ReasonName = Services.HumanResources.PersonnelTerminationService
                    .ReasonName(x.Reason),
                IsFinalized = x.Status == TerminationStatus.Finalized,
                MissingFields = MissingFor(
                    x.IdentityNumber, x.BirthDate, x.SgkRegistrationNumber),
                NoticeUploaded = exitNotices.Contains(x.PersonnelId)
            }),
            entryCount = entries.Count,
            exitCount = exits.Count,
            notNotifiableCount =
                entries.Count(x => MissingFor(
                    x.IdentityNumber, x.BirthDate, x.SgkRegistrationNumber).Count > 0) +
                exits.Count(x => MissingFor(
                    x.IdentityNumber, x.BirthDate, x.SgkRegistrationNumber).Count > 0),
            note = "Bildirim SGK ekranına elle giriliyor; bu liste giriş için " +
                   "gereken alanları verir. Bildirge özlük dosyasına " +
                   "yüklendiğinde 'bildirildi' olarak işaretlenir."
        });
    }

    /// <summary>SGK girişinde zorunlu olup eksik kalan alanlar.</summary>
    private static List<string> MissingFor(
        string? identityNumber, DateTime? birthDate, string? sgkNumber)
    {
        var missing = new List<string>();

        if (!TurkishIdentityNumber.IsValid(identityNumber))
            missing.Add("T.C. kimlik no");

        if (birthDate is null)
            missing.Add("Doğum tarihi");

        if (string.IsNullOrWhiteSpace(sgkNumber))
            missing.Add("SGK sicil no");

        return missing;
    }
}
