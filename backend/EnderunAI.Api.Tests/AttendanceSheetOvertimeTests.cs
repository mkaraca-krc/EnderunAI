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
