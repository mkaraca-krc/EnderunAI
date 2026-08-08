using EnderunAI.Api.Data;
using EnderunAI.Api.Models.HumanResources;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.HumanResources;
using EnderunAI.Api.Services.Schedule;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

/// <param name="FirstDay">Bayramın BİRİNCİ günü — resmî ilandan
/// alınır. Arife ve kalan günler bundan türetilir.</param>
public sealed record AddReligiousHolidayRequest(int Kind, DateOnly FirstDay);

public sealed record HolidayRequest(DateOnly Date, string Name, bool IsHalfDay);

public sealed record VerifyHolidayCalendarRequest(string? Note);

public sealed record UpdateWorkWeekRequest(int? WorkWeek, int? HeadOfficeWorkWeek);

/// <summary>
/// Resmî tatil takvimi ve çalışma haftası.
///
/// Puantaj cetveli buradan doluyor: eksik bir tatil, o gün için
/// çalışılmış gibi puantaj ve dolayısıyla yanlış bordro demek. Bu
/// yüzden takvim DOĞRULANMADAN otomatik doldurmada kullanılmıyor —
/// bordro parametrelerindeki fail-closed desenin aynısı.
/// </summary>
[ApiController]
[Authorize]
[Route("api/hr/tatil-takvimi")]
public sealed class HolidayCalendarController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.AttendanceView)]
    public async Task<IActionResult> Get(
        [FromQuery] Guid companyId,
        [FromQuery] int? year,
        CancellationToken cancellationToken)
    {
        var targetYear = year ?? DateTime.UtcNow.Year;

        var calendar = await db.CompanyHolidayCalendars
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Year == targetYear)
            .Select(x => new
            {
                x.Id,
                x.Year,
                x.VerifiedAtUtc,
                x.VerificationNote,
                Days = x.Days
                    .OrderBy(d => d.Date)
                    .Select(d => new { d.Id, d.Date, d.Name, d.IsHalfDay })
            })
            .SingleOrDefaultAsync(cancellationToken);

        var settings = await db.CompanyPayrollSettings
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Year == targetYear)
            .Select(x => new { x.WorkWeek, x.HeadOfficeWorkWeek })
            .SingleOrDefaultAsync(cancellationToken);

        // Sıfır "tanımsız" sayılıyor: eski kayıtlarda kolon 0 ile açıldı
        // ve boş bir hafta gösterilirse süre hesabı yapılamaz.
        var companyWorkWeek = settings is { WorkWeek: > 0 }
            ? settings.WorkWeek
            : (int)WorkWeekResolver.SiteDefault;

        return Ok(new
        {
            year = targetYear,
            exists = calendar is not null,
            calendar,
            isVerified = calendar?.VerifiedAtUtc is not null,
            workWeek = companyWorkWeek,
            workWeekName = WorkWeekResolver.Describe((WorkWeekDays)companyWorkWeek),
            headOfficeWorkWeek = settings?.HeadOfficeWorkWeek,
            headOfficeWorkWeekName = settings?.HeadOfficeWorkWeek is int head
                ? WorkWeekResolver.Describe((WorkWeekDays)head)
                : null,
            message = calendar is null
                ? "Bu yıl için tatil takvimi açılmamış."
                : calendar.VerifiedAtUtc is null
                    ? "Takvim doğrulanmadı; puantaj cetvelini doldurmakta " +
                      "kullanılmaz. Dini bayram tarihlerini girip doğrulayın."
                    : null
        });
    }

    /// <summary>
    /// Sabit resmî tatilleri ekler. Zaten var olan gün TEKRAR
    /// eklenmez — kullanıcının düzelttiği bir kayıt geri alınmamalı.
    ///
    /// Dini bayramlar buradan GELMEZ: tarihleri resmî ilana bağlı ve
    /// sistemin tahmin etmesi yanlış bordro üretir.
    /// </summary>
    [HttpPost("{year:int}/sabit-tatiller")]
    [RequirePermission(PermissionCatalog.Keys.AttendanceManage)]
    public async Task<IActionResult> SeedFixed(
        int year,
        [FromQuery] Guid companyId,
        CancellationToken cancellationToken)
    {
        var calendar = await ResolveOrCreateAsync(companyId, year, cancellationToken);

        if (calendar is null)
            return BadRequest(new { message = "Şirket bulunamadı." });

        var existing = calendar.Days.Select(x => x.Date).ToHashSet();
        var added = 0;

        foreach (var day in TurkishPublicHolidays.Fixed(year))
        {
            if (existing.Contains(day.Date))
                continue;

            db.CompanyHolidays.Add(new CompanyHoliday
            {
                CompanyHolidayCalendarId = calendar.Id,
                Date = day.Date,
                Name = day.Name,
                IsHalfDay = day.IsHalfDay
            });

            added++;
        }

        // Takvim değişti: doğrulama düşer, yeniden onaylanmalı.
        Invalidate(calendar);

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            addedCount = added,
            message = added == 0
                ? "Eklenecek yeni sabit tatil yok."
                : $"{added} sabit resmî tatil eklendi. Dini bayram tarihlerini " +
                  "girdikten sonra takvimi doğrulayın."
        });
    }

    /// <summary>
    /// Dini bayramı ilk gününden türetir: arife yarım gün + Ramazan'da
    /// 3, Kurban'da 4 tam gün.
    /// </summary>
    [HttpPost("{year:int}/dini-bayram")]
    [RequirePermission(PermissionCatalog.Keys.AttendanceManage)]
    public async Task<IActionResult> AddReligious(
        int year,
        [FromQuery] Guid companyId,
        AddReligiousHolidayRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(typeof(ReligiousHolidayKind), request.Kind))
            return BadRequest(new { message = "Geçersiz bayram türü." });

        var kind = (ReligiousHolidayKind)request.Kind;

        if (request.FirstDay.Year != year)
        {
            return BadRequest(new
            {
                message = $"Bayramın ilk günü {year} yılında olmalıdır."
            });
        }

        var calendar = await ResolveOrCreateAsync(companyId, year, cancellationToken);

        if (calendar is null)
            return BadRequest(new { message = "Şirket bulunamadı." });

        var existing = calendar.Days.Select(x => x.Date).ToHashSet();
        var days = TurkishPublicHolidays.Religious(kind, request.FirstDay);
        var added = 0;

        foreach (var day in days.Where(x => !existing.Contains(x.Date)))
        {
            db.CompanyHolidays.Add(new CompanyHoliday
            {
                CompanyHolidayCalendarId = calendar.Id,
                Date = day.Date,
                Name = day.Name,
                IsHalfDay = day.IsHalfDay
            });

            added++;
        }

        Invalidate(calendar);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            addedCount = added,
            message = $"{TurkishPublicHolidays.KindName(kind)} için {added} gün " +
                      "eklendi."
        });
    }

    [HttpPost("{year:int}/gun")]
    [RequirePermission(PermissionCatalog.Keys.AttendanceManage)]
    public async Task<IActionResult> AddDay(
        int year,
        [FromQuery] Guid companyId,
        HolidayRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "Tatil adı zorunludur." });

        if (request.Date.Year != year)
            return BadRequest(new { message = $"Tarih {year} yılında olmalıdır." });

        var calendar = await ResolveOrCreateAsync(companyId, year, cancellationToken);

        if (calendar is null)
            return BadRequest(new { message = "Şirket bulunamadı." });

        if (calendar.Days.Any(x => x.Date == request.Date))
            return BadRequest(new { message = "Bu tarih zaten takvimde var." });

        db.CompanyHolidays.Add(new CompanyHoliday
        {
            CompanyHolidayCalendarId = calendar.Id,
            Date = request.Date,
            Name = request.Name.Trim(),
            IsHalfDay = request.IsHalfDay
        });

        Invalidate(calendar);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Tatil günü eklendi." });
    }

    [HttpDelete("gun/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.AttendanceManage)]
    public async Task<IActionResult> RemoveDay(
        Guid id, CancellationToken cancellationToken)
    {
        var day = await db.CompanyHolidays
            .Include(x => x.CompanyHolidayCalendar)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (day is null)
            return NotFound(new { message = "Tatil günü bulunamadı." });

        db.CompanyHolidays.Remove(day);
        Invalidate(day.CompanyHolidayCalendar);

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Tatil günü kaldırıldı." });
    }

    /// <summary>
    /// Takvimi doğrular. Doğrulanana kadar puantaj cetveli bu takvimden
    /// doldurulmaz.
    /// </summary>
    [HttpPost("{year:int}/dogrula")]
    [RequirePermission(PermissionCatalog.Keys.AttendanceManage)]
    public async Task<IActionResult> Verify(
        int year,
        [FromQuery] Guid companyId,
        VerifyHolidayCalendarRequest request,
        CancellationToken cancellationToken)
    {
        var calendar = await db.CompanyHolidayCalendars
            .Include(x => x.Days)
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId && x.Year == year, cancellationToken);

        if (calendar is null)
            return NotFound(new { message = "Bu yıl için takvim bulunamadı." });

        if (calendar.Days.Count == 0)
        {
            return BadRequest(new
            {
                message = "Boş takvim doğrulanamaz; en az sabit resmî tatilleri " +
                          "ekleyin."
            });
        }

        var raw = User.FindFirst("sub")?.Value
            ?? User.FindFirst(
                System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        calendar.VerifiedAtUtc = DateTime.UtcNow;
        calendar.VerifiedByUserId = Guid.TryParse(raw, out var parsed)
            ? parsed
            : null;
        calendar.VerificationNote = request.Note?.Trim();
        calendar.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            dayCount = calendar.Days.Count,
            message = $"{year} tatil takvimi doğrulandı ({calendar.Days.Count} gün); " +
                      "puantaj cetveli artık bu takvimden dolabilir."
        });
    }

    /// <summary>Şirket ve merkez kadrosu çalışma haftası.</summary>
    [HttpPut("{year:int}/calisma-haftasi")]
    [RequirePermission(PermissionCatalog.Keys.AttendanceManage)]
    public async Task<IActionResult> UpdateWorkWeek(
        int year,
        [FromQuery] Guid companyId,
        UpdateWorkWeekRequest request,
        CancellationToken cancellationToken)
    {
        if (request.WorkWeek is int week && week is <= 0 or > 127)
        {
            return BadRequest(new
            {
                message = "Çalışma haftasında en az bir gün seçilmelidir."
            });
        }

        if (request.HeadOfficeWorkWeek is int head && head is <= 0 or > 127)
        {
            return BadRequest(new
            {
                message = "Merkez çalışma haftasında en az bir gün seçilmelidir."
            });
        }

        var settings = await db.CompanyPayrollSettings
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId && x.Year == year, cancellationToken);

        if (settings is null)
        {
            return BadRequest(new
            {
                message = $"{year} yılı için bordro parametreleri tanımlı değil; " +
                          "çalışma haftası oraya bağlı."
            });
        }

        if (request.WorkWeek is int value)
            settings.WorkWeek = value;

        settings.HeadOfficeWorkWeek = request.HeadOfficeWorkWeek;
        settings.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Çalışma haftası kaydedildi." });
    }

    private async Task<CompanyHolidayCalendar?> ResolveOrCreateAsync(
        Guid companyId, int year, CancellationToken cancellationToken)
    {
        var calendar = await db.CompanyHolidayCalendars
            .Include(x => x.Days)
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId && x.Year == year, cancellationToken);

        if (calendar is not null)
            return calendar;

        var companyExists = await db.Companies.AnyAsync(
            x => x.Id == companyId, cancellationToken);

        if (!companyExists)
            return null;

        calendar = new CompanyHolidayCalendar { CompanyId = companyId, Year = year };

        db.CompanyHolidayCalendars.Add(calendar);
        await db.SaveChangesAsync(cancellationToken);

        return calendar;
    }

    /// <summary>
    /// Takvim her değiştiğinde doğrulama DÜŞER. Doğrulanmış bir takvime
    /// sessizce gün eklenebilseydi, damga "bu takvim kontrol edildi"
    /// anlamını kaybederdi.
    /// </summary>
    private static void Invalidate(CompanyHolidayCalendar calendar)
    {
        calendar.VerifiedAtUtc = null;
        calendar.VerifiedByUserId = null;
        calendar.UpdatedAtUtc = DateTime.UtcNow;
    }
}
