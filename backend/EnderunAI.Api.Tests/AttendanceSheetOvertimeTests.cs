using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Data.HumanResources;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.HumanResources;
using EnderunAI.Api.Services.Schedule;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Puantaj cetveli: şantiye/merkez filtresi ve satır-içi mesai girişi.
///
/// MESAİNİN İKİ GİRİŞ YOLU VAR: fazla mesai talebi ve cetvel hücresi.
/// İkisi de aynı alana (AttendanceRecord saatleri) yazıyor, o yüzden
/// GÜN BAŞINA TEK SAHİP kuralı geçerli: onaylı talebi olan günde
/// cetvel mesai hücresine dokunmaz. Bu kural olmasaydı son kaydeden
/// diğerinin saatini sessizce silerdi.
///
/// Sınır (270) ve muvafakat uyarıları da iki kaynağı birleştirerek
/// sayıyor; aynı gün iki kez sayılmıyor.
/// </summary>
[Collection("Integration")]
public sealed class AttendanceSheetOvertimeTests(DatabaseFixture fixture)
{
    private const int Year = 2026;
    private const int Month = 4;

    // 2026-04-15 Çarşamba: normal iş günü.
    private static readonly DateTime WorkDate =
        new(Year, Month, 15, 0, 0, 0, DateTimeKind.Utc);

    private sealed record Context(
        Guid CompanyId,
        Guid PersonnelId,
        Guid ProjectId,
        Guid ProjectSiteId);

    private Task<HttpClient> ClientAsync() =>
        AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    private async Task<Context> CreateContextAsync(
        string suffix,
        WorkLocationType location = WorkLocationType.ProjectSite,
        decimal? annualLimit = null,
        int? consentYear = null)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var site = new ProjectSite
        {
            ProjectId = project.Id,
            Code = $"SNT-{suffix}",
            Name = $"Şantiye {suffix}"
        };

        db.ProjectSites.Add(site);

        var personnel = await TestDataFactory.CreatePersonnelAsync(
            db, project.CompanyId, suffix);

        personnel.WorkLocationType = location;
        personnel.OvertimeConsentYear = consentYear;

        db.CompanyPayrollSettings.Add(new CompanyPayrollSettings
        {
            CompanyId = project.CompanyId,
            Year = Year,
            MinimumWageGross = 33_030m,
            MinimumWageNet = 28_075m,
            SgkBaseFloor = 33_030m,
            SgkBaseCeiling = 247_725m,
            DailyWorkHours = 7.5m,
            WorkWeek = (int)WorkWeekDays.MondayToSaturday,
            HeadOfficeWorkWeek = (int)WorkWeekDays.MondayToFriday,
            AnnualOvertimeHourLimit = annualLimit
        });

        await db.SaveChangesAsync();

        if (location == WorkLocationType.ProjectSite)
        {
            db.ProjectSiteAssignments.Add(new ProjectSiteAssignment
            {
                PersonnelId = personnel.Id,
                ProjectSiteId = site.Id,
                StartDate = new DateTime(Year, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });

            await db.SaveChangesAsync();
        }

        return new Context(
            project.CompanyId, personnel.Id, project.Id, site.Id);
    }

    private async Task<JsonElement> SheetAsync(
        HttpClient client, Guid companyId, string query = "")
    {
        var response = await client.GetAsync(
            $"/api/hr/attendance/cetvel?companyId={companyId}" +
            $"&year={Year}&month={Month}{query}");

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Task<HttpResponseMessage> SaveAsync(
        HttpClient client,
        Guid companyId,
        Guid personnelId,
        decimal overtime = 0m,
        decimal sunday = 0m,
        decimal publicHoliday = 0m,
        DateTime? date = null) =>
        client.PostAsJsonAsync("/api/hr/attendance/cetvel/kaydet", new
        {
            companyId,
            entries = new[]
            {
                new
                {
                    personnelId,
                    workDate = (date ?? WorkDate).ToString("yyyy-MM-dd"),
                    status = (int)AttendanceStatus.Worked,
                    normalHours = 7.5m,
                    overtimeHours = overtime,
                    sundayHours = sunday,
                    publicHolidayHours = publicHoliday,
                    description = (string?)null
                }
            }
        });

    private async Task<AttendanceRecord?> LoadRecordAsync(Guid personnelId) =>
        await LoadRecordAsync(personnelId, WorkDate);

    private async Task<AttendanceRecord?> LoadRecordAsync(
        Guid personnelId, DateTime date)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.AttendanceRecords.AsNoTracking()
            .SingleOrDefaultAsync(x => x.PersonnelId == personnelId &&
                                       x.WorkDate == date);
    }

    private static JsonElement Cell(JsonElement sheet, Guid personnelId, int day) =>
        sheet.GetProperty("rows").EnumerateArray()
            .Single(x => x.GetProperty("personnelId").GetGuid() == personnelId)
            .GetProperty("cells").EnumerateArray()
            .Single(x => x.GetProperty("date").GetString()!.EndsWith($"-{day:D2}"));

    private static IEnumerable<Guid> RowIds(JsonElement sheet) =>
        sheet.GetProperty("rows").EnumerateArray()
            .Select(x => x.GetProperty("personnelId").GetGuid());

    // ---------------- Satır-içi mesai girişi ----------------

    /// <summary>
    /// ANA TEST: güne girilen mesai puantaja BİR KEZ düşüyor ve
    /// ele geçen hesabına bir kez giriyor. İki yol da aynı alana
    /// yazdığı için mükerrer sayım en büyük risk.
    /// </summary>
    [Fact]
    public async Task SheetOvertime_CountsExactlyOnce()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, consentYear: Year);
        var client = await ClientAsync();

        Assert.Equal(HttpStatusCode.OK,
            (await SaveAsync(client, context.CompanyId, context.PersonnelId,
                overtime: 3m)).StatusCode);

        // Aynı gün ikinci kez kaydedilince satır güncellenir, ikincisi
        // açılmaz — puantaj tek satır kalır.
        await SaveAsync(client, context.CompanyId, context.PersonnelId,
            overtime: 3m);

        var record = await LoadRecordAsync(context.PersonnelId);

        Assert.NotNull(record);
        Assert.Equal(3m, record!.OvertimeHours);
        Assert.Equal(10.5m, record.TotalHours);

        // Personel kartı: saat bir kez sayılıyor.
        var panel = await (await client.GetAsync(
            $"/api/hr/personel/{context.PersonnelId}/fazla-mesai?year={Year}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(3m, panel.GetProperty("overtimeHours").GetDecimal());

        var lines = panel.GetProperty("lines").EnumerateArray().ToList();

        Assert.Single(lines);
        Assert.Equal("sheet", lines[0].GetProperty("source").GetString());
        Assert.Equal(1.5m, lines[0].GetProperty("multiplier").GetDecimal());
        Assert.True(lines[0].GetProperty("landedOnAttendance").GetBoolean());
    }

    /// <summary>
    /// Tatil çalışması AYRI kovada kalıyor: fazla çalışma sayımına
    /// girmiyor, kendi çarpanıyla duruyor. Aynı kovaya atılsaydı hem
    /// çarpan yanlış olurdu hem yıllık sınır şişerdi.
    /// </summary>
    [Fact]
    public async Task HolidayHours_StayInTheirOwnBucket()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, consentYear: Year);
        var client = await ClientAsync();

        await SaveAsync(client, context.CompanyId, context.PersonnelId,
            overtime: 2m, sunday: 4m, publicHoliday: 6m);

        var panel = await (await client.GetAsync(
            $"/api/hr/personel/{context.PersonnelId}/fazla-mesai?year={Year}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(2m, panel.GetProperty("overtimeHours").GetDecimal());
        Assert.Equal(4m, panel.GetProperty("sundayHours").GetDecimal());
        Assert.Equal(6m, panel.GetProperty("publicHolidayHours").GetDecimal());

        // Sınır sayımı yalnız fazla çalışmayı sayar.
        Assert.True(panel.GetProperty("limitCountsOvertimeOnly").GetBoolean());

        var multipliers = panel.GetProperty("lines").EnumerateArray()
            .ToDictionary(
                x => x.GetProperty("kind").GetInt32(),
                x => x.GetProperty("multiplier").GetDecimal());

        Assert.Equal(1.5m, multipliers[0]);
        Assert.Equal(2m, multipliers[1]);
        Assert.Equal(2m, multipliers[2]);
    }

    /// <summary>
    /// GÜN SAHİBİ: o gün için onaylı fazla mesai talebi varsa cetvel
    /// mesai hücresine dokunamaz. Kilit ekranda gösteriliyor ama kapı
    /// UÇTA — eski bir sekme talebin saatini ezmemeli.
    /// </summary>
    [Fact]
    public async Task RequestOwnedDay_IsNotOverwrittenBySheet()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, consentYear: Year);
        var client = await ClientAsync();

        var created = await client.PostAsJsonAsync(
            "/api/hr/workforce/overtimes", new
            {
                companyId = context.CompanyId,
                personnelId = context.PersonnelId,
                projectId = context.ProjectId,
                workDate = WorkDate,
                requestedHours = 4m,
                isSundayWork = false,
                isPublicHolidayWork = false,
                reason = "Termin baskısı"
            });

        var overtimeId = JsonDocument
            .Parse(await created.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(
            $"/api/hr/workforce/overtimes/{overtimeId}/approve", null)).StatusCode);

        // Cetvel aynı güne 9 saat yazmayı deniyor.
        var response = await SaveAsync(
            client, context.CompanyId, context.PersonnelId, overtime: 9m);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(1, payload.GetProperty("keptRequestHoursCount").GetInt32());

        // Talebin saati duruyor.
        var record = await LoadRecordAsync(context.PersonnelId);

        Assert.Equal(4m, record!.OvertimeHours);

        // Normal çalışma saati yine cetvelden güncelleniyor.
        Assert.Equal(7.5m, record.NormalHours);

        // Hücre kilitli işaretleniyor.
        var sheet = await SheetAsync(client, context.CompanyId);

        Assert.True(Cell(sheet, context.PersonnelId, 15)
            .GetProperty("overtimeLocked").GetBoolean());
    }

    /// <summary>
    /// Talebi olmayan gün cetvele açık: kilit yalnız çakışan günde.
    /// </summary>
    [Fact]
    public async Task DayWithoutRequest_IsWritableFromTheSheet()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, consentYear: Year);
        var client = await ClientAsync();

        await SaveAsync(client, context.CompanyId, context.PersonnelId,
            overtime: 2.5m);

        var sheet = await SheetAsync(client, context.CompanyId);
        var cell = Cell(sheet, context.PersonnelId, 15);

        Assert.False(cell.GetProperty("overtimeLocked").GetBoolean());
        Assert.Equal(2.5m, cell.GetProperty("overtimeHours").GetDecimal());
    }

    /// <summary>
    /// Aynı gün iki kaynaktan sayılmıyor: talebin sahiplendiği gün
    /// cetvel tarafında elenirken, personel kartında da tek satır
    /// olarak görünüyor.
    /// </summary>
    [Fact]
    public async Task RequestAndSheet_AreNeverCountedTwice()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, consentYear: Year);
        var client = await ClientAsync();

        var created = await client.PostAsJsonAsync(
            "/api/hr/workforce/overtimes", new
            {
                companyId = context.CompanyId,
                personnelId = context.PersonnelId,
                projectId = context.ProjectId,
                workDate = WorkDate,
                requestedHours = 4m,
                isSundayWork = false,
                isPublicHolidayWork = false,
                reason = "Termin baskısı"
            });

        var overtimeId = JsonDocument
            .Parse(await created.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetGuid();

        await client.PostAsync(
            $"/api/hr/workforce/overtimes/{overtimeId}/approve", null);

        await SaveAsync(client, context.CompanyId, context.PersonnelId,
            overtime: 9m);

        var panel = await (await client.GetAsync(
            $"/api/hr/personel/{context.PersonnelId}/fazla-mesai?year={Year}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        // 4 saat: talepten. Cetvelin 9'u hiç yazılmadı, 13 çıkmıyor.
        Assert.Equal(4m, panel.GetProperty("overtimeHours").GetDecimal());
        Assert.Single(panel.GetProperty("lines").EnumerateArray());
    }

    // ---------------- Çalışma günü olmayan günler ----------------

    /// <summary>
    /// HAFTA TATİLİ: mesai girilebiliyor ve ×2 kovaya gidiyor.
    ///
    /// Girişin günün TÜRÜNE bağlı kapatılması, en yüksek çarpanlı
    /// mesainin hiç girilememesi demekti — oysa hafta tatilinde
    /// çalışmanın kendisi zaten mesaidir.
    /// </summary>
    [Fact]
    public async Task WeeklyHoliday_AcceptsOvertimeIntoTheDoubleBucket()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, consentYear: Year);
        var client = await ClientAsync();

        var sheet = await SheetAsync(client, context.CompanyId);

        // Çalışma günü olmayan ilk gün (tatil takvimi kurulmadığı için
        // yalnız hafta tatili düşer).
        var restDay = sheet.GetProperty("rows").EnumerateArray().First()
            .GetProperty("cells").EnumerateArray()
            .First(x => !x.GetProperty("isWorkDay").GetBoolean() &&
                        !x.GetProperty("isHoliday").GetBoolean());

        var date = DateTime.Parse(restDay.GetProperty("date").GetString()!)
            .Date;

        // Kutu kilitli DEĞİL: ne onaylı gün ne onaylı talep var.
        Assert.False(restDay.GetProperty("overtimeLocked").GetBoolean());
        Assert.False(restDay.GetProperty("isApproved").GetBoolean());

        var response = await client.PostAsJsonAsync(
            "/api/hr/attendance/cetvel/kaydet", new
            {
                companyId = context.CompanyId,
                entries = new[]
                {
                    new
                    {
                        personnelId = context.PersonnelId,
                        workDate = date.ToString("yyyy-MM-dd"),
                        status = (int)AttendanceStatus.WeeklyHoliday,
                        normalHours = 0m,
                        overtimeHours = 0m,
                        sundayHours = 6m,
                        publicHolidayHours = 0m,
                        description = (string?)null
                    }
                }
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var record = await LoadRecordAsync(
            context.PersonnelId,
            DateTime.SpecifyKind(date, DateTimeKind.Utc));

        Assert.Equal(6m, record!.SundayHours);
        Assert.Equal(0m, record.OvertimeHours);

        var panel = await (await client.GetAsync(
            $"/api/hr/personel/{context.PersonnelId}/fazla-mesai?year={Year}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(6m, panel.GetProperty("sundayHours").GetDecimal());

        // Yıllık sınır sayımına GİRMEZ: tatil çalışması ayrı kova.
        Assert.Equal(0m, panel.GetProperty("overtimeHours").GetDecimal());

        var line = panel.GetProperty("lines").EnumerateArray().Single();

        Assert.Equal(2m, line.GetProperty("multiplier").GetDecimal());
        Assert.Equal("sheet", line.GetProperty("source").GetString());
    }

    /// <summary>
    /// GENEL TATİL: mesai girilebiliyor ve genel tatil kovasına
    /// gidiyor. Köprünün sırası: genel tatil hafta tatilinden önce.
    /// </summary>
    [Fact]
    public async Task PublicHoliday_AcceptsOvertimeIntoTheHolidayBucket()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, consentYear: Year);
        var client = await ClientAsync();

        await client.PostAsJsonAsync(
            $"/api/hr/tatil-takvimi/{Year}/sabit-tatiller?companyId={context.CompanyId}",
            new { });

        var sheet = await SheetAsync(client, context.CompanyId);

        // 23 Nisan: Nisan ayının sabit resmî tatili.
        var holiday = sheet.GetProperty("rows").EnumerateArray().First()
            .GetProperty("cells").EnumerateArray()
            .First(x => x.GetProperty("isHoliday").GetBoolean());

        var date = DateTime.Parse(holiday.GetProperty("date").GetString()!).Date;

        Assert.False(holiday.GetProperty("overtimeLocked").GetBoolean());

        await client.PostAsJsonAsync("/api/hr/attendance/cetvel/kaydet", new
        {
            companyId = context.CompanyId,
            entries = new[]
            {
                new
                {
                    personnelId = context.PersonnelId,
                    workDate = date.ToString("yyyy-MM-dd"),
                    status = (int)AttendanceStatus.PublicHoliday,
                    normalHours = 0m,
                    overtimeHours = 0m,
                    sundayHours = 0m,
                    publicHolidayHours = 8m,
                    description = (string?)null
                }
            }
        });

        var record = await LoadRecordAsync(
            context.PersonnelId,
            DateTime.SpecifyKind(date, DateTimeKind.Utc));

        Assert.Equal(8m, record!.PublicHolidayHours);
        Assert.Equal(0m, record.SundayHours);
        Assert.Equal(0m, record.OvertimeHours);

        var panel = await (await client.GetAsync(
            $"/api/hr/personel/{context.PersonnelId}/fazla-mesai?year={Year}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(8m, panel.GetProperty("publicHolidayHours").GetDecimal());
        Assert.Equal(2m, panel.GetProperty("lines").EnumerateArray()
            .Single().GetProperty("multiplier").GetDecimal());
    }

    // ---------------- 270 saat ve muvafakat ----------------

    /// <summary>
    /// Cetvelden girilen saat yıllık sınıra da görünüyor. Yalnız
    /// talepler sayılsaydı bütün mesai cetvelden girilerek sınır
    /// kontrolü boşa düşerdi.
    /// </summary>
    [Fact]
    public async Task SheetOvertime_CountsTowardTheAnnualLimit()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(
            suffix, annualLimit: 10m, consentYear: Year);

        var client = await ClientAsync();

        await SaveAsync(client, context.CompanyId, context.PersonnelId,
            overtime: 12m);

        var panel = await (await client.GetAsync(
            $"/api/hr/personel/{context.PersonnelId}/fazla-mesai?year={Year}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("exceeded", panel.GetProperty("limitStatus").GetString());

        // Bordro ön kontrolü de uyarıyor — ENGEL DEĞİL, uyarı.
        var readiness = await (await client.GetAsync(
            $"/api/hr/bordro-on-kontrol?companyId={context.CompanyId}" +
            $"&year={Year}&month={Month}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        var warnings = readiness.GetProperty("warnings").EnumerateArray()
            .Select(x => x.GetString() ?? "")
            .ToList();

        Assert.Contains(warnings, x => x.Contains("yıllık sınırı"));
    }

    /// <summary>
    /// Muvafakati olmayan personele cetvelden mesai girilirse bordro
    /// ön kontrolü uyarıyor.
    /// </summary>
    [Fact]
    public async Task SheetOvertime_TriggersTheConsentWarning()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, annualLimit: 270m);
        var client = await ClientAsync();

        await SaveAsync(client, context.CompanyId, context.PersonnelId,
            overtime: 5m);

        var readiness = await (await client.GetAsync(
            $"/api/hr/bordro-on-kontrol?companyId={context.CompanyId}" +
            $"&year={Year}&month={Month}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        var warnings = readiness.GetProperty("warnings").EnumerateArray()
            .Select(x => x.GetString() ?? "")
            .ToList();

        Assert.Contains(warnings, x => x.Contains("muvafakati yok"));
    }

    // ---------------- Maaş kartındaki mesai ----------------

    /// <summary>
    /// Personel kartındaki "Ek ödeme ve ele geçen" bloğu bu uçtan
    /// besleniyor: cetvelden girilen mesai saat ve TUTAR olarak bir
    /// kez görünüyor, doğru çarpanla.
    ///
    /// Taban ele geçen = resmî net 45.000 + elden 9.000 = 54.000
    /// Saatlik = 54.000 / (30 × 7,5) = 240
    /// 3 saat × 240 × 1,5 = 1.080
    /// </summary>
    [Fact]
    public async Task SalaryCard_ShowsSheetOvertimeOnceWithTheRightMultiplier()
    {
        var (context, day) = await CreateCurrentMonthContextAsync();
        var client = await ClientAsync();

        await SaveAsync(client, context.CompanyId, context.PersonnelId,
            overtime: 3m, date: day);

        var panel = await LoadPanelAsync(client, context.PersonnelId, day.Year);

        var month = panel.GetProperty("currentMonth");

        Assert.Equal(3m, month.GetProperty("hours").GetDecimal());
        Assert.Equal(1_080m, month.GetProperty("amount").GetDecimal());

        var takeHome = panel.GetProperty("takeHome");

        Assert.Equal(45_000m, takeHome.GetProperty("officialNet").GetDecimal());
        Assert.Equal(9_000m,
            takeHome.GetProperty("manualExtraMonthly").GetDecimal());
        Assert.Equal(1_080m, takeHome.GetProperty("overtimeExtra").GetDecimal());

        // Mesai toplam eldene BİR KEZ giriyor: 9.000 + 1.080
        Assert.Equal(10_080m, takeHome.GetProperty("totalExtra").GetDecimal());
        Assert.Equal(55_080m, takeHome.GetProperty("totalTakeHome").GetDecimal());
    }

    /// <summary>
    /// Aynı güne fazla mesai talebi onaylanınca kart çift saymıyor:
    /// gün talebe geçiyor, cetvelin saati zaten yazılamıyor.
    /// </summary>
    [Fact]
    public async Task SalaryCard_DoesNotDoubleCountWhenARequestOwnsTheDay()
    {
        var (context, day) = await CreateCurrentMonthContextAsync();
        var client = await ClientAsync();

        await SaveAsync(client, context.CompanyId, context.PersonnelId,
            overtime: 3m, date: day);

        var created = await client.PostAsJsonAsync(
            "/api/hr/workforce/overtimes", new
            {
                companyId = context.CompanyId,
                personnelId = context.PersonnelId,
                projectId = context.ProjectId,
                workDate = day,
                requestedHours = 2m,
                isSundayWork = false,
                isPublicHolidayWork = false,
                reason = "Termin baskısı"
            });

        var overtimeId = JsonDocument
            .Parse(await created.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetGuid();

        await client.PostAsync(
            $"/api/hr/workforce/overtimes/{overtimeId}/approve", null);

        var panel = await LoadPanelAsync(client, context.PersonnelId, day.Year);

        // Talep günü sahiplendi: 2 saat, 3+2=5 değil.
        Assert.Equal(2m,
            panel.GetProperty("currentMonth").GetProperty("hours").GetDecimal());

        Assert.Equal(720m,
            panel.GetProperty("takeHome").GetProperty("overtimeExtra")
                .GetDecimal());

        Assert.Single(panel.GetProperty("lines").EnumerateArray());
    }

    /// <summary>
    /// Saatlik ücretin böleni ŞİRKET PARAMETRESİNDEN geliyor, kodda
    /// sabit değil: günlük çalışma süresi 8 saat olan şirkette
    /// saatlik = taban / (30 × 8).
    ///
    /// Şirket yevmiyeyi 8 saat üzerinden buluyor; bölen 7,5 kalsaydı
    /// saatlik ücret ve mesai tutarı %6,7 yüksek çıkardı.
    ///
    /// Taban 54.000 → 54.000 / 240 = 225,00
    /// 3 saat × 225 × 1,5 = 1.012,50
    /// </summary>
    [Fact]
    public async Task HourlyRate_FollowsTheCompanyDailyWorkHours()
    {
        var (context, day) = await CreateCurrentMonthContextAsync(
            dailyWorkHours: 8m);

        var client = await ClientAsync();

        await SaveAsync(client, context.CompanyId, context.PersonnelId,
            overtime: 3m, date: day);

        var panel = await LoadPanelAsync(client, context.PersonnelId, day.Year);
        var takeHome = panel.GetProperty("takeHome");

        Assert.Equal(8m, takeHome.GetProperty("dailyWorkHours").GetDecimal());
        Assert.Equal(225m, takeHome.GetProperty("hourlyRate").GetDecimal());

        Assert.Equal(1_012.50m,
            panel.GetProperty("currentMonth").GetProperty("amount").GetDecimal());

        Assert.Equal(1_012.50m,
            takeHome.GetProperty("overtimeExtra").GetDecimal());
    }

    private async Task<JsonElement> LoadPanelAsync(
        HttpClient client, Guid personnelId, int year) =>
        await (await client.GetAsync(
            $"/api/hr/personel/{personnelId}/fazla-mesai?year={year}"))
            .Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>
    /// Ele geçen hesabı BU AYA bakıyor; tutar testleri o yüzden
    /// içinde bulunulan ayı kullanıyor.
    /// </summary>
    private async Task<(Context Context, DateTime Day)>
        CreateCurrentMonthContextAsync(decimal dailyWorkHours = 7.5m)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var now = DateTime.UtcNow;

        var day = new DateTime(now.Year, now.Month, 5, 0, 0, 0, DateTimeKind.Utc);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);
        var personnel = await TestDataFactory.CreatePersonnelAsync(
            db, project.CompanyId, suffix);

        personnel.WorkLocationType = WorkLocationType.ProjectSite;
        personnel.OvertimeConsentYear = now.Year;

        db.CompanyPayrollSettings.Add(new CompanyPayrollSettings
        {
            CompanyId = project.CompanyId,
            Year = now.Year,
            MinimumWageGross = 33_030m,
            MinimumWageNet = 28_075m,
            SgkBaseFloor = 33_030m,
            SgkBaseCeiling = 247_725m,
            DailyWorkHours = dailyWorkHours
        });

        db.PersonnelExtraPayments.Add(new PersonnelExtraPayment
        {
            CompanyId = project.CompanyId,
            PersonnelId = personnel.Id,
            MonthlyAmount = 9_000m,
            EffectiveStartDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        await db.SaveChangesAsync();

        hrDb.SalaryDefinitions.Add(new HrSalaryDefinition
        {
            CompanyId = project.CompanyId,
            PersonnelId = personnel.Id,
            EffectiveStartDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            SalaryBasis = SalaryBasis.Net,
            TargetNetSalary = 45_000m,
            GrossSalary = 60_000m,
            OvertimeMultiplier = 1.5m,
            SundayMultiplier = 2m,
            PublicHolidayMultiplier = 2m,
            CurrencyCode = "TRY"
        });

        await hrDb.SaveChangesAsync();

        return (new Context(
            project.CompanyId, personnel.Id, project.Id, Guid.Empty), day);
    }

    // ---------------- Şantiye / merkez filtresi ----------------

    /// <summary>Görev yeri ekseni: merkez seçilince şantiye personeli düşüyor.</summary>
    [Fact]
    public async Task WorkLocationFilter_SeparatesHeadOfficeFromSite()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var site = await CreateContextAsync(suffix);

        var officeSuffix = Guid.NewGuid().ToString("N")[..8];
        Guid officeId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var office = await TestDataFactory.CreatePersonnelAsync(
                db, site.CompanyId, officeSuffix);

            office.WorkLocationType = WorkLocationType.HeadOffice;
            await db.SaveChangesAsync();

            officeId = office.Id;
        }

        var client = await ClientAsync();

        var siteSheet = await SheetAsync(
            client, site.CompanyId, "&workLocation=2");

        Assert.Contains(site.PersonnelId, RowIds(siteSheet));
        Assert.DoesNotContain(officeId, RowIds(siteSheet));

        var officeSheet = await SheetAsync(
            client, site.CompanyId, "&workLocation=1");

        Assert.Contains(officeId, RowIds(officeSheet));
        Assert.DoesNotContain(site.PersonnelId, RowIds(officeSheet));
    }

    /// <summary>
    /// Proje filtresi kadrolu atamayı getiriyor.
    /// </summary>
    [Fact]
    public async Task ProjectFilter_ReturnsAssignedPersonnel()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientAsync();

        var sheet = await SheetAsync(
            client, context.CompanyId, $"&projectId={context.ProjectId}");

        Assert.Contains(context.PersonnelId, RowIds(sheet));

        var siteSheet = await SheetAsync(
            client, context.CompanyId, $"&projectSiteId={context.ProjectSiteId}");

        Assert.Contains(context.PersonnelId, RowIds(siteSheet));
    }

    /// <summary>
    /// GÖREVLENDİRMEYLE GELEN PERSONEL: kadrolu ataması başka yerde
    /// olsa da, o döneme denk gelen onaylı çalışma görevlendirmesi
    /// varsa gittiği projenin cetvelinde görünüyor. Yalnız atamaya
    /// bakılsaydı puantajı hiç girilemezdi — oysa gün maliyeti o
    /// projeye yazılıyor.
    /// </summary>
    [Fact]
    public async Task TemporarilyAssignedPersonnel_AppearsOnTheTargetProject()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var home = await CreateContextAsync(suffix);

        var awaySuffix = Guid.NewGuid().ToString("N")[..8];
        Guid awayProjectId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var away = new Project
            {
                CompanyId = home.CompanyId,
                BranchId = await db.Projects
                    .Where(x => x.Id == home.ProjectId)
                    .Select(x => x.BranchId)
                    .SingleAsync(),
                Code = $"PRJ-{awaySuffix}",
                Name = $"Takviye Projesi {awaySuffix}",
                CurrencyCode = "TRY",
                Status = ProjectStatus.Active
            };

            db.Projects.Add(away);
            await db.SaveChangesAsync();

            awayProjectId = away.Id;

            db.PersonnelDuties.Add(new PersonnelDuty
            {
                CompanyId = home.CompanyId,
                PersonnelId = home.PersonnelId,
                DutyType = PersonnelDutyType.Work,
                TargetProjectId = away.Id,
                StartDate = new DateTime(Year, Month, 10, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(Year, Month, 20, 0, 0, 0, DateTimeKind.Utc),
                DailyAllowance = 0m,
                Purpose = "Ekip takviyesi",
                Status = PersonnelDutyStatus.Approved,
                ApprovedAtUtc = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        var client = await ClientAsync();

        var awaySheet = await SheetAsync(
            client, home.CompanyId, $"&projectId={awayProjectId}");

        Assert.Contains(home.PersonnelId, RowIds(awaySheet));

        // Kadrolu olduğu projede de görünmeye devam ediyor: ayın bir
        // kısmını orada geçirdi.
        var homeSheet = await SheetAsync(
            client, home.CompanyId, $"&projectId={home.ProjectId}");

        Assert.Contains(home.PersonnelId, RowIds(homeSheet));
    }

    /// <summary>
    /// Onaylı gün korunuyor: ay onaylandıktan sonra cetvelden mesai
    /// değiştirilemiyor. "Ayı Onayla" kesinleştirme adımı.
    /// </summary>
    [Fact]
    public async Task ApprovedMonth_FreezesTheSheetOvertime()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, consentYear: Year);
        var client = await ClientAsync();

        await SaveAsync(client, context.CompanyId, context.PersonnelId,
            overtime: 3m);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            "/api/hr/attendance/cetvel/onayla", new
            {
                companyId = context.CompanyId,
                year = Year,
                month = Month,
                personnelIds = (Guid[]?)null
            })).StatusCode);

        var response = await SaveAsync(
            client, context.CompanyId, context.PersonnelId, overtime: 8m);

        var payload = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(1, payload.GetProperty("skippedApprovedCount").GetInt32());
        Assert.Equal(3m, (await LoadRecordAsync(context.PersonnelId))!.OvertimeHours);
    }
}
