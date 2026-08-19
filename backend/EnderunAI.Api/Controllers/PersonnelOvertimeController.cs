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
/// ÜCRET TABANI: mesai saat ücreti RESMÎ NET + MANUEL ELDEN üzerinden
/// yürür — yalnız resmî tutar değil. Sıra sabittir ve döngü yoktur:
///   1) taban ele geçen = resmî net + manuel elden (MESAİ HARİÇ)
///   2) saatlik = taban / (30 × günlük çalışma saati)
///   3) mesai tutarı = Σ(saat × saatlik × katsayı), tamamı ELDEN
///   4) toplam elden = manuel elden + mesai; ele geçen = resmî net +
///      toplam elden — mesai eldene BİR KEZ girer
///   5) mesai 1. adımdaki tabana GERİ BESLENMEZ (yüzdesel kalem ve
///      brütleştirme kararlarındaki disiplinin aynısı)
///
/// GİZLİLİK: saat, döküm ve muvafakat personnel.view ile açıktır —
/// şantiye şefi ve formen kendi ekibinin mesaisini görmeden
/// çalışamaz. TUTAR ise ELDEN ödemedir ve elden izolasyonuna tabidir:
/// yalnızca extra_payment.view olan kullanıcıya döner. Tutar
/// gizlenmez, sorgudan hiç çıkmaz.
/// </summary>
[ApiController]
[Authorize]
[Route("api/hr/personel/{personnelId:guid}/fazla-mesai")]
public sealed class PersonnelOvertimeController(
    AppDbContext db,
    HrDbContext hrDb,
    SalaryTakeHomeService takeHome,
    IExtraPaymentVisibilityService extraPaymentVisibility,
    IScopedData scoped) : ControllerBase
{
    /// <summary>Sınıra yaklaşma eşiği — köprüdeki uyarıyla aynı.</summary>
    private const decimal NearLimitRatio = 0.9m;

    /// <summary>Aylık tutarın güne bölünmesi — bordroyla aynı bölen.</summary>
    private const decimal MonthlyToDailyDivisor = 30m;

    /// <summary>
    /// Ayar yoksa şirketin uyguladığı günlük çalışma süresi: 8 saat.
    /// Yevmiye ve saatlik ücret bu bölenden çıkıyor; merkez ve şantiye
    /// için AYNI — ayrım çalışma HAFTASINDA (cumartesi), günlük sürede
    /// değil.
    /// </summary>
    private const decimal DefaultDailyWorkHours = 8m;

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

        /*
         * KAPSAM DİKİŞİNDEN OKUNUYOR.
         *
         * Bu uç `personnel.view` ile korunuyor ve o izin ŞANTİYE ŞEFİ
         * ile FORMEN'de de var (ikisi de SiteOnly kapsamlı). Ham
         * `db.Personnel` okumak, kendi şantiyesinde olmayan bir
         * personelin kimliğini ve fazla mesai onay durumunu
         * gösterirdi.
         *
         * Kapsam dışı kayıt 404 döner (403 değil): kaydın VARLIĞINI
         * sızdırmamak için — PersonnelController'daki desenin aynısı.
         */
        var scopedPersonnel = await scoped.PersonnelAsync(cancellationToken);

        var personnel = await scopedPersonnel
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

        // --- Cetvelden girilen mesai ---
        //
        // Mesai iki yoldan girilebiliyor: fazla mesai TALEBİ ve
        // puantaj CETVELİ. Sınır ve muvafakat sayımı yalnız talepleri
        // saysaydı, cetvelden girilen saat yasal sınıra hiç
        // görünmezdi — bordro o saati ödediği halde.
        //
        // ÇİFT SAYIM YOK: talebin sahiplendiği gün cetvelde kilitli
        // olduğu için bir gün ya talepten ya cetvelden gelir, ikisi
        // birden olmaz. Yine de burada açıkça eleniyor: onaylı talebi
        // olan gün cetvel tarafından atlanıyor.
        var requestDays = approved.Select(x => x.WorkDate.Date).ToHashSet();

        var sheetDays = (await db.AttendanceRecords
            .AsNoTracking()
            .Where(x => x.PersonnelId == personnelId &&
                        x.WorkDate >= yearStart && x.WorkDate <= yearEnd &&
                        (x.OvertimeHours > 0m || x.SundayHours > 0m ||
                         x.PublicHolidayHours > 0m))
            .Select(x => new
            {
                x.Id,
                x.WorkDate,
                x.OvertimeHours,
                x.SundayHours,
                x.PublicHolidayHours
            })
            .ToListAsync(cancellationToken))
            .Where(x => !requestDays.Contains(x.WorkDate.Date))
            .ToList();

        // Cetvel satırları talep satırlarıyla aynı biçime çevriliyor
        // ki sınır, tutar ve liste hesabı tek koddan geçsin.
        var sheetLines = sheetDays
            .SelectMany(x => new[]
            {
                (Record: x.Id, x.WorkDate, Hours: x.OvertimeHours, Kind: 0),
                (Record: x.Id, x.WorkDate, Hours: x.SundayHours, Kind: 1),
                (Record: x.Id, x.WorkDate, Hours: x.PublicHolidayHours, Kind: 2)
            })
            .Where(x => x.Hours > 0m)
            .ToList();

        var limit = await db.CompanyPayrollSettings
            .AsNoTracking()
            .Where(x => x.CompanyId == personnel.CompanyId && x.Year == targetYear)
            .Select(x => x.AnnualOvertimeHourLimit)
            .SingleOrDefaultAsync(cancellationToken);

        decimal HoursOfKind(int kind) =>
            approved
                .Where(x => KindOf(x.IsSundayWork, x.IsPublicHolidayWork) == kind)
                .Sum(x => x.ApprovedHours) +
            sheetLines.Where(x => x.Kind == kind).Sum(x => x.Hours);

        var overtimeHours = HoursOfKind(0);
        var sundayHours = HoursOfKind(1);
        var publicHolidayHours = HoursOfKind(2);

        var (limitStatus, limitStatusName) = ResolveLimitStatus(limit, overtimeHours);

        // --- Tutar: ELDEN, elden izolasyonuna tabi ---
        var canViewAmounts = await extraPaymentVisibility
            .CanViewExtraPaymentAsync(cancellationToken);

        decimal? hourlyRate = null;
        decimal? officialNet = null;
        decimal? manualExtra = null;
        decimal? dailyWorkHours = null;
        var multipliers = (Overtime: 1.5m, Sunday: 2m, PublicHoliday: 2m);

        if (canViewAmounts)
        {
            // Kartın esas alındığı an: yılın en son onaylı mesai günü.
            // Elden ödeme tarih aralıklı olduğu için "bugün"e bakmak
            // geçmiş yılın mesaisine bugünkü zammı yansıtırdı.
            var asOf = approved.Count > 0
                ? approved[0].WorkDate
                : yearEnd;

            var card = await hrDb.SalaryDefinitions
                .AsNoTracking()
                .Where(x => x.PersonnelId == personnelId &&
                            x.EffectiveStartDate <= asOf &&
                            (x.EffectiveEndDate == null || x.EffectiveEndDate >= asOf))
                .OrderByDescending(x => x.EffectiveStartDate)
                .FirstOrDefaultAsync(cancellationToken);

            if (card is not null)
            {
                multipliers = (card.OvertimeMultiplier, card.SundayMultiplier,
                    card.PublicHolidayMultiplier);

                officialNet = SalaryTakeHomeService.ResolveOfficialNet(
                    card,
                    await takeHome.TryLoadPayrollParametersAsync(
                        personnel.CompanyId, asOf.Year, cancellationToken));

                var extras = await takeHome.LoadEffectiveExtraPaymentsAsync(
                    [personnelId], asOf, cancellationToken);

                manualExtra = extras.GetValueOrDefault(personnelId);

                dailyWorkHours = await db.CompanyPayrollSettings
                    .AsNoTracking()
                    .Where(x => x.CompanyId == personnel.CompanyId &&
                                x.Year == asOf.Year)
                    .Select(x => (decimal?)x.DailyWorkHours)
                    .SingleOrDefaultAsync(cancellationToken) ?? DefaultDailyWorkHours;

                // ADIM 1-2: taban ele geçen = resmî net + manuel elden
                // (mesai HARİÇ), saatlik = taban / (30 × günlük saat).
                // Bölen bordrodaki konvansiyonun aynısı.
                var baseTakeHome = (officialNet ?? 0m) + manualExtra.Value;

                // Formül ORTAK YARDIMCIDAN: nakit akış projeksiyonu da
                // aynı hesabı yapıyor. Satır içinde kalsaydı ikisi
                // zamanla ayrışır ve aynı personel için iki ekran iki
                // farklı rakam gösterirdi.
                hourlyRate = SalaryTakeHomeService.ResolveOvertimeHourlyRate(
                    officialNet, manualExtra, dailyWorkHours);
            }
        }

        decimal MultiplierOf(int kind) => kind switch
        {
            2 => multipliers.PublicHoliday,
            1 => multipliers.Sunday,
            _ => multipliers.Overtime
        };

        // ADIM 3: mesai tutarı = saat × saatlik × katsayı. Tamamı ELDEN.
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
                amount = AmountOf(kind, x.ApprovedHours),
                source = "request"
            };
        })
        .Concat(sheetLines.Select(x => new
        {
            x.Record,
            x.WorkDate,
            hours = x.Hours,
            kind = x.Kind,
            kindName = KindName(x.Kind),
            multiplier = MultiplierOf(x.Kind),
            // Cetvelden girilen saat zaten puantajın kendisi.
            landedOnAttendance = true,
            attendanceMonth = (string?)x.WorkDate.ToString("yyyy-MM"),
            Reason = (string?)null,
            ApprovedAtUtc = (DateTime?)null,
            amount = AmountOf(x.Kind, x.Hours),
            source = "sheet"
        }).Select(x => new
        {
            Id = x.Record,
            x.WorkDate,
            x.hours,
            x.kind,
            x.kindName,
            x.multiplier,
            x.landedOnAttendance,
            x.attendanceMonth,
            x.Reason,
            x.ApprovedAtUtc,
            x.amount,
            x.source
        }))
        .OrderByDescending(x => x.WorkDate)
        .ToList();

        // "Bu ay": mesai tutarı aylık ele geçene girdiği için ay
        // kırılımı gerekiyor.
        var currentPeriod = DateTime.UtcNow;

        var currentMonthLines = lines
            .Where(x => x.WorkDate.Year == currentPeriod.Year &&
                        x.WorkDate.Month == currentPeriod.Month)
            .ToList();

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

            // Bu ayın mesaisi ayrı satır olarak: kartta "şu an ne
            // birikti" sorusunun cevabı yıllık toplamda kayboluyordu.
            currentMonth = new
            {
                year = currentPeriod.Year,
                month = currentPeriod.Month,
                hours = currentMonthLines.Sum(x => x.hours),
                overtimeHours = currentMonthLines
                    .Where(x => x.kind == 0).Sum(x => x.hours),
                sundayHours = currentMonthLines
                    .Where(x => x.kind == 1).Sum(x => x.hours),
                publicHolidayHours = currentMonthLines
                    .Where(x => x.kind == 2).Sum(x => x.hours),
                amount = canViewAmounts
                    ? currentMonthLines.Sum(x => x.amount ?? 0m)
                    : (decimal?)null
            },

            // ADIM 4: mesai eldene BİR KEZ girer. Manuel elden ile
            // mesai ayrı ayrı da dönüyor ki ekran ikisini toplayıp
            // çift saymasın.
            takeHome = new
            {
                officialNet,
                manualExtraMonthly = manualExtra,
                overtimeExtra = canViewAmounts
                    ? currentMonthLines.Sum(x => x.amount ?? 0m)
                    : (decimal?)null,
                totalExtra = canViewAmounts
                    ? (manualExtra ?? 0m) + currentMonthLines.Sum(x => x.amount ?? 0m)
                    : (decimal?)null,
                totalTakeHome = canViewAmounts
                    ? (officialNet ?? 0m) + (manualExtra ?? 0m) +
                      currentMonthLines.Sum(x => x.amount ?? 0m)
                    : (decimal?)null,
                hourlyRate,
                dailyWorkHours,
                // Mesai tabana geri beslenmez: saatlik ücret yalnızca
                // resmî net + manuel eldenden türer.
                baseExcludesOvertime = true
            },

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

}
