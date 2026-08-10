using EnderunAI.Api.Data.HumanResources;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.HumanResources;
using EnderunAI.Api.Services.Schedule;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

/// <param name="PersonnelIds">Boşsa şirketin bütün aktif personeli.</param>
/// <param name="Overwrite">Var olan kayıtların üzerine yazılsın mı.
/// ONAYLI kayıtlar hiçbir durumda değiştirilmez.</param>
public sealed record GenerateAttendanceSheetRequest(
    Guid CompanyId,
    int Year,
    int Month,
    IReadOnlyCollection<Guid>? PersonnelIds,
    bool Overwrite = false);

public sealed record AttendanceSheetEntry(
    Guid PersonnelId,
    DateOnly WorkDate,
    int Status,
    decimal NormalHours,
    decimal OvertimeHours,
    decimal SundayHours,
    decimal PublicHolidayHours,
    string? Description);

public sealed record SaveAttendanceSheetRequest(
    Guid CompanyId,
    IReadOnlyCollection<AttendanceSheetEntry> Entries);

public sealed record ApproveAttendanceSheetRequest(
    Guid CompanyId,
    int Year,
    int Month,
    IReadOnlyCollection<Guid>? PersonnelIds);

/// <summary>
/// Aylık puantaj cetveli.
///
/// Mevcut günlük uçlar tek kayıt açıyor; ekran "toplu" görünse de arka
/// planda personel başına bir istek atıyordu. 79 kişilik bir ay ~2.000
/// istek demek ve yarısı geçip yarısı düşebiliyordu — kullanıcı hangi
/// 36 kaydın oluşmadığını elle arıyordu. Buradaki uçlar TEK İSTEK ve
/// TEK İŞLEM: ya hepsi yazılır ya hiçbiri.
///
/// Cetvel resmî tatil takviminden dolar; takvim DOĞRULANMADAN
/// doldurma yapılmaz, çünkü eksik bir tatil o gün çalışılmış gibi
/// puantaj ve yanlış bordro üretir.
/// </summary>
[ApiController]
[Authorize]
[Route("api/hr/attendance/cetvel")]
public sealed class AttendanceSheetController(
    AppDbContext db, HrDbContext hrDb) : ControllerBase
{
    /// <summary>Cetvel görünümü: günler, personel satırları ve mevcut kayıtlar.</summary>
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.AttendancePayrollView)]
    public async Task<IActionResult> Get(
        [FromQuery] Guid companyId,
        [FromQuery] int year,
        [FromQuery] int month,
        [FromQuery] int? workLocation,
        [FromQuery] Guid? projectId,
        [FromQuery] Guid? projectSiteId,
        CancellationToken cancellationToken)
    {
        if (month is < 1 or > 12)
            return BadRequest(new { message = "Geçersiz ay." });

        var context = await LoadContextAsync(companyId, year, cancellationToken);

        var (periodStart, periodEnd) = Period(year, month);

        var personnel = await LoadPersonnelAsync(
            companyId, null,
            new SheetScope(workLocation, projectId, projectSiteId,
                periodStart, periodEnd),
            cancellationToken);

        var records = await db.AttendanceRecords
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.WorkDate >= periodStart && x.WorkDate <= periodEnd)
            .Select(x => new
            {
                x.Id,
                x.PersonnelId,
                x.WorkDate,
                x.Status,
                x.NormalHours,
                x.OvertimeHours,
                x.SundayHours,
                x.PublicHolidayHours,
                x.IsApproved,
                x.Description
            })
            .ToListAsync(cancellationToken);

        var byPersonnel = records
            .GroupBy(x => x.PersonnelId)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(x => DateOnly.FromDateTime(x.WorkDate)));

        var lockedDays = await LoadRequestOwnedDaysAsync(
            personnel.Select(x => x.Id).ToList(), periodStart, periodEnd,
            cancellationToken);

        var rows = personnel.Select(person =>
        {
            var week = WorkWeekResolver.Resolve(
                person.WorkWeek, person.WorkLocationType,
                context.HeadOfficeWorkWeek, context.CompanyWorkWeek);

            var days = AttendanceSheetGenerator.Build(
                year, month, week.Days, context.Holidays, context.DailyWorkHours);

            byPersonnel.TryGetValue(person.Id, out var existing);

            return new
            {
                personnelId = person.Id,
                person.EmployeeNumber,
                person.FullName,
                workWeek = (int)week.Days,
                workWeekName = WorkWeekResolver.Describe(week.Days),
                workWeekSource = week.Source,
                cells = days.Select(day =>
                {
                    var saved = existing is not null &&
                                existing.TryGetValue(day.Date, out var found)
                        ? found
                        : null;

                    return new
                    {
                        date = day.Date,
                        day.IsWorkDay,
                        day.IsHoliday,
                        day.IsHalfDayHoliday,
                        day.HolidayName,
                        day.SuggestedStatus,
                        day.SuggestedStatusName,
                        day.SuggestedNormalHours,
                        recordId = saved?.Id,
                        status = saved?.Status,
                        normalHours = saved?.NormalHours,
                        overtimeHours = saved?.OvertimeHours,
                        // Tatil çalışması saatleri de hücrede taşınır:
                        // cetvel bunları geri göndermezse kaydetme
                        // sırasında sıfırlanıp siliniyorlardı.
                        sundayHours = saved?.SundayHours,
                        publicHolidayHours = saved?.PublicHolidayHours,
                        isApproved = saved?.IsApproved ?? false,

                        // O günün mesai saatini fazla mesai TALEBİ
                        // yazdıysa cetvel o hücreye dokunamaz: tek
                        // alana iki yazıcı olsaydı son kaydeden
                        // diğerinin saatini sessizce silerdi.
                        overtimeLocked = lockedDays.Contains(
                            (person.Id, day.Date))
                    };
                })
            };
        }).ToList();

        return Ok(new
        {
            year,
            month,
            context.HolidayCalendarVerified,
            context.DailyWorkHours,
            companyWorkWeek = context.CompanyWorkWeek,
            companyWorkWeekName = WorkWeekResolver.Describe(
                (WorkWeekDays)(context.CompanyWorkWeek ?? 0)),
            holidayCount = context.Holidays.Count,
            recordCount = records.Count,
            approvedCount = records.Count(x => x.IsApproved),
            personnelCount = rows.Count,
            message = context.HolidayCalendarVerified
                ? null
                : $"{year} resmî tatil takvimi doğrulanmadı; cetvel otomatik " +
                  "doldurulamaz. Tatil takvimini tamamlayıp doğrulayın.",
            rows
        });
    }

    /// <summary>
    /// Ayı takvimden doldurur — TEK İŞLEMDE.
    ///
    /// Doğrulanmamış tatil takvimiyle çalışmaz: eksik bir tatil, o gün
    /// çalışılmış gibi puantaj ve yanlış bordro demek.
    /// </summary>
    [HttpPost("olustur")]
    [RequirePermission(PermissionCatalog.Keys.AttendancePayrollCreate)]
    public async Task<IActionResult> Generate(
        GenerateAttendanceSheetRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Month is < 1 or > 12)
            return BadRequest(new { message = "Geçersiz ay." });

        var context = await LoadContextAsync(
            request.CompanyId, request.Year, cancellationToken);

        if (!context.HolidayCalendarVerified)
        {
            return BadRequest(new
            {
                message = $"{request.Year} resmî tatil takvimi doğrulanmadan cetvel " +
                          "doldurulamaz. Eksik bir tatil, o gün çalışılmış gibi " +
                          "puantaj ve yanlış bordro üretir."
            });
        }

        var personnel = await LoadPersonnelAsync(
            request.CompanyId, request.PersonnelIds, null, cancellationToken);

        if (personnel.Count == 0)
            return BadRequest(new { message = "Cetvele girecek aktif personel yok." });

        var (periodStart, periodEnd) = Period(request.Year, request.Month);

        var existing = await db.AttendanceRecords
            .Where(x => x.CompanyId == request.CompanyId &&
                        x.WorkDate >= periodStart && x.WorkDate <= periodEnd)
            .ToListAsync(cancellationToken);

        var index = existing.ToDictionary(
            x => (x.PersonnelId, DateOnly.FromDateTime(x.WorkDate)));

        var created = 0;
        var updated = 0;
        var skippedApproved = 0;

        foreach (var person in personnel)
        {
            var week = WorkWeekResolver.Resolve(
                person.WorkWeek, person.WorkLocationType,
                context.HeadOfficeWorkWeek, context.CompanyWorkWeek);

            var days = AttendanceSheetGenerator.Build(
                request.Year, request.Month, week.Days,
                context.Holidays, context.DailyWorkHours);

            foreach (var day in days)
            {
                if (index.TryGetValue((person.Id, day.Date), out var record))
                {
                    // Onaylı kayıt hiçbir durumda ezilmez: onay, o günün
                    // birileri tarafından doğrulandığı anlamına geliyor.
                    if (record.IsApproved)
                    {
                        skippedApproved++;
                        continue;
                    }

                    if (!request.Overwrite)
                        continue;

                    ApplySuggestion(record, day);
                    updated++;
                    continue;
                }

                var created_ = new AttendanceRecord
                {
                    CompanyId = request.CompanyId,
                    PersonnelId = person.Id,
                    WorkDate = DateTime.SpecifyKind(
                        day.Date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc)
                };

                ApplySuggestion(created_, day);
                db.AttendanceRecords.Add(created_);
                created++;
            }
        }

        // Tek SaveChanges = tek işlem: ya hepsi ya hiçbiri.
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            createdCount = created,
            updatedCount = updated,
            skippedApprovedCount = skippedApproved,
            personnelCount = personnel.Count,
            message =
                $"{personnel.Count} personel için {created} gün oluşturuldu" +
                (updated > 0 ? $", {updated} gün güncellendi" : "") +
                (skippedApproved > 0
                    ? $", {skippedApproved} onaylı gün korundu"
                    : "") + "."
        });
    }

    /// <summary>Cetveldeki düzeltmeleri tek istekte kaydeder.</summary>
    [HttpPost("kaydet")]
    [RequirePermission(PermissionCatalog.Keys.AttendancePayrollEdit)]
    public async Task<IActionResult> Save(
        SaveAttendanceSheetRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Entries.Count == 0)
            return Ok(new { savedCount = 0, message = "Değişiklik yok." });

        foreach (var entry in request.Entries)
        {
            if (!Enum.IsDefined(typeof(AttendanceStatus), entry.Status))
            {
                return BadRequest(new
                {
                    message = $"Geçersiz puantaj durumu: {entry.Status}."
                });
            }

            if (entry.NormalHours < 0m || entry.OvertimeHours < 0m ||
                entry.SundayHours < 0m || entry.PublicHolidayHours < 0m)
            {
                return BadRequest(new { message = "Saat değerleri negatif olamaz." });
            }
        }

        var dates = request.Entries.Select(x => x.WorkDate).ToList();
        var minDate = ToUtc(dates.Min());
        var maxDate = ToUtc(dates.Max());

        var personnelIds = request.Entries.Select(x => x.PersonnelId).Distinct().ToList();

        var existing = await db.AttendanceRecords
            .Where(x => x.CompanyId == request.CompanyId &&
                        personnelIds.Contains(x.PersonnelId) &&
                        x.WorkDate >= minDate && x.WorkDate <= maxDate)
            .ToListAsync(cancellationToken);

        var index = existing.ToDictionary(
            x => (x.PersonnelId, DateOnly.FromDateTime(x.WorkDate)));

        // Mesai saatini talebin yazdığı günler: cetvel bu hücrelere
        // dokunmaz. Ekran zaten kilitli gösteriyor ama kapı BURADA —
        // eski bir sekme ya da doğrudan istek talebin saatini
        // ezmemeli.
        var lockedDays = await LoadRequestOwnedDaysAsync(
            personnelIds, minDate, maxDate, cancellationToken);

        var saved = 0;
        var skippedApproved = 0;
        var keptRequestHours = 0;

        foreach (var entry in request.Entries)
        {
            if (index.TryGetValue((entry.PersonnelId, entry.WorkDate), out var record))
            {
                if (record.IsApproved)
                {
                    skippedApproved++;
                    continue;
                }
            }
            else
            {
                record = new AttendanceRecord
                {
                    CompanyId = request.CompanyId,
                    PersonnelId = entry.PersonnelId,
                    WorkDate = ToUtc(entry.WorkDate)
                };

                db.AttendanceRecords.Add(record);
            }

            record.Status = entry.Status;
            record.NormalHours = entry.NormalHours;

            // Talebin sahiplendiği günde mesai saatleri OLDUĞU GİBİ
            // kalır; normal çalışma saati ve durum yine cetvelden
            // güncellenir.
            if (lockedDays.Contains((entry.PersonnelId, entry.WorkDate)))
            {
                if (entry.OvertimeHours != record.OvertimeHours ||
                    entry.SundayHours != record.SundayHours ||
                    entry.PublicHolidayHours != record.PublicHolidayHours)
                {
                    keptRequestHours++;
                }
            }
            else
            {
                record.OvertimeHours = entry.OvertimeHours;
                record.SundayHours = entry.SundayHours;
                record.PublicHolidayHours = entry.PublicHolidayHours;
            }

            record.TotalHours = record.NormalHours + record.OvertimeHours +
                                record.SundayHours + record.PublicHolidayHours;
            record.Description = entry.Description?.Trim();
            record.UpdatedAtUtc = DateTime.UtcNow;

            saved++;
        }

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            savedCount = saved,
            skippedApprovedCount = skippedApproved,
            keptRequestHoursCount = keptRequestHours,
            message = $"{saved} gün kaydedildi" +
                      (skippedApproved > 0
                          ? $", {skippedApproved} onaylı gün korundu"
                          : "") +
                      (keptRequestHours > 0
                          ? $", {keptRequestHours} günde mesai saati onaylı " +
                            "fazla mesai talebinden geldiği için korundu"
                          : "") + "."
        });
    }

    /// <summary>Ayın tamamını tek işlemde onaylar.</summary>
    [HttpPost("onayla")]
    [RequirePermission(PermissionCatalog.Keys.AttendancePayrollApprove)]
    public async Task<IActionResult> Approve(
        ApproveAttendanceSheetRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Month is < 1 or > 12)
            return BadRequest(new { message = "Geçersiz ay." });

        var (periodStart, periodEnd) = Period(request.Year, request.Month);

        var query = db.AttendanceRecords
            .Where(x => x.CompanyId == request.CompanyId &&
                        !x.IsApproved &&
                        x.WorkDate >= periodStart && x.WorkDate <= periodEnd);

        if (request.PersonnelIds is { Count: > 0 })
            query = query.Where(x => request.PersonnelIds.Contains(x.PersonnelId));

        var records = await query.ToListAsync(cancellationToken);

        if (records.Count == 0)
            return Ok(new { approvedCount = 0, message = "Onaylanacak kayıt yok." });

        var raw = User.FindFirst("sub")?.Value
            ?? User.FindFirst(
                System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        var actorId = Guid.TryParse(raw, out var parsed) ? parsed : (Guid?)null;
        var now = DateTime.UtcNow;

        foreach (var record in records)
        {
            record.IsApproved = true;
            record.ApprovedByUserId = actorId;
            record.ApprovedAtUtc = now;
            record.UpdatedAtUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            approvedCount = records.Count,
            message = $"{records.Count} puantaj günü onaylandı; bu günler artık " +
                      "bordroya ve hakedişe girer."
        });
    }

    // ---------------- Yardımcılar ----------------

    private sealed record SheetContext(
        int? CompanyWorkWeek,
        int? HeadOfficeWorkWeek,
        decimal DailyWorkHours,
        bool HolidayCalendarVerified,
        IReadOnlyDictionary<DateOnly, (string Name, bool IsHalfDay)> Holidays);

    private sealed record SheetPersonnel(
        Guid Id,
        string EmployeeNumber,
        string FullName,
        int? WorkWeek,
        int WorkLocationType);

    private async Task<SheetContext> LoadContextAsync(
        Guid companyId, int year, CancellationToken cancellationToken)
    {
        var settings = await db.CompanyPayrollSettings
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Year == year)
            .Select(x => new { x.WorkWeek, x.HeadOfficeWorkWeek, x.DailyWorkHours })
            .SingleOrDefaultAsync(cancellationToken);

        var calendar = await db.CompanyHolidayCalendars
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Year == year)
            .Select(x => new
            {
                x.VerifiedAtUtc,
                Days = x.Days.Select(d => new { d.Date, d.Name, d.IsHalfDay })
            })
            .SingleOrDefaultAsync(cancellationToken);

        var holidays = calendar?.Days
            .ToDictionary(x => x.Date, x => (x.Name, x.IsHalfDay))
            ?? [];

        return new SheetContext(
            CompanyWorkWeek: settings is { WorkWeek: > 0 } ? settings.WorkWeek : null,
            HeadOfficeWorkWeek: settings?.HeadOfficeWorkWeek,
            DailyWorkHours: settings?.DailyWorkHours ?? 7.5m,
            HolidayCalendarVerified: calendar?.VerifiedAtUtc is not null,
            Holidays: holidays);
    }

    /// <summary>
    /// Cetvelin kapsamı: görev yeri ekseni ve/veya belirli bir
    /// proje/şantiye. Dönem, görevlendirmeyle GEÇİCİ gelen personeli
    /// bulmak için gerekiyor.
    /// </summary>
    private sealed record SheetScope(
        int? WorkLocation,
        Guid? ProjectId,
        Guid? ProjectSiteId,
        DateTime PeriodStart,
        DateTime PeriodEnd);

    /// <summary>
    /// Bir proje ya da şantiyede o dönem çalışan personel.
    ///
    /// İKİ KAYNAĞIN BİRLEŞİMİ: kadrolu atama (ProjectSiteAssignment)
    /// ve o döneme denk gelen onaylı ÇALIŞMA görevlendirmesi. Yalnız
    /// atamaya bakılsaydı, görevlendirmeyle gelen personel gittiği
    /// şantiyenin cetvelinde hiç görünmez ve puantajı girilemezdi —
    /// oysa gün maliyeti o projeye yazılıyor.
    /// </summary>
    private async Task<HashSet<Guid>> LoadScopePersonnelAsync(
        SheetScope scope, CancellationToken cancellationToken)
    {
        var assignments = db.ProjectSiteAssignments
            .AsNoTracking()
            // Dönem içinde açık olan atama: dönem bitmeden başlamış ve
            // dönem başlamadan kapanmamış.
            .Where(x => x.StartDate <= scope.PeriodEnd &&
                        (x.EndDate == null || x.EndDate >= scope.PeriodStart));

        assignments = scope.ProjectSiteId is Guid site
            ? assignments.Where(x => x.ProjectSiteId == site)
            : assignments.Where(x => x.ProjectSite.ProjectId == scope.ProjectId);

        var result = (await assignments
            .Select(x => x.PersonnelId)
            .Distinct()
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var duties = db.PersonnelDuties
            .AsNoTracking()
            .Where(x => x.DutyType == PersonnelDutyType.Work &&
                        x.Status == PersonnelDutyStatus.Approved &&
                        x.StartDate <= scope.PeriodEnd &&
                        x.EndDate >= scope.PeriodStart);

        duties = scope.ProjectSiteId is Guid dutySite
            ? duties.Where(x => x.TargetProjectSiteId == dutySite)
            : duties.Where(x => x.TargetProjectId == scope.ProjectId);

        foreach (var id in await duties
                     .Select(x => x.PersonnelId)
                     .Distinct()
                     .ToListAsync(cancellationToken))
        {
            result.Add(id);
        }

        return result;
    }

    /// <summary>
    /// Mesai saatini fazla mesai TALEBİNİN yazdığı günler.
    ///
    /// Bu günlerde cetvel mesai hücresine dokunmaz. Onaylı talep
    /// puantaja kendi saatini yazıyor; cetvel de yazsaydı iki yazıcı
    /// aynı alanda çarpışır, hangi rakamın bordroya gittiği son
    /// kaydedene bağlı kalırdı.
    /// </summary>
    private async Task<HashSet<(Guid, DateOnly)>> LoadRequestOwnedDaysAsync(
        List<Guid> personnelIds,
        DateTime periodStart,
        DateTime periodEnd,
        CancellationToken cancellationToken)
    {
        if (personnelIds.Count == 0)
            return [];

        var rows = await hrDb.OvertimeRequests
            .AsNoTracking()
            .Where(x => personnelIds.Contains(x.PersonnelId) &&
                        x.Status == Models.HumanResources.HrApprovalStatus.Approved &&
                        x.ApprovedHours > 0m &&
                        x.WorkDate >= periodStart && x.WorkDate <= periodEnd)
            .Select(x => new { x.PersonnelId, x.WorkDate })
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => (x.PersonnelId, DateOnly.FromDateTime(x.WorkDate)))
            .ToHashSet();
    }

    private async Task<List<SheetPersonnel>> LoadPersonnelAsync(
        Guid companyId,
        IReadOnlyCollection<Guid>? personnelIds,
        SheetScope? scope,
        CancellationToken cancellationToken)
    {
        var query = db.Personnel
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.IsActive &&
                        x.Status == PersonnelStatus.Active);

        if (personnelIds is { Count: > 0 })
            query = query.Where(x => personnelIds.Contains(x.Id));

        if (scope?.WorkLocation is int location &&
            Enum.IsDefined(typeof(WorkLocationType), location))
        {
            query = query.Where(x => (int)x.WorkLocationType == location);
        }

        if (scope is { } filter &&
            (filter.ProjectId is not null || filter.ProjectSiteId is not null))
        {
            var scoped = await LoadScopePersonnelAsync(filter, cancellationToken);

            query = query.Where(x => scoped.Contains(x.Id));
        }

        return await query
            .OrderBy(x => x.FirstName).ThenBy(x => x.LastName)
            .Select(x => new SheetPersonnel(
                x.Id,
                x.EmployeeNumber,
                x.FirstName + " " + x.LastName,
                x.WorkWeek,
                (int)x.WorkLocationType))
            .ToListAsync(cancellationToken);
    }

    private static void ApplySuggestion(AttendanceRecord record, AttendanceSheetDay day)
    {
        record.Status = day.SuggestedStatus;
        record.NormalHours = day.SuggestedNormalHours;
        record.OvertimeHours = 0m;
        record.SundayHours = 0m;
        record.PublicHolidayHours = 0m;
        record.TotalHours = day.SuggestedNormalHours;
        record.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static (DateTime Start, DateTime End) Period(int year, int month) =>
        (new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc),
         new DateTime(year, month, DateTime.DaysInMonth(year, month),
             0, 0, 0, DateTimeKind.Utc));

    private static DateTime ToUtc(DateOnly date) =>
        DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
}
