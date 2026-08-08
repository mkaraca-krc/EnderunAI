using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.HumanResources;
using EnderunAI.Api.Services.Schedule;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Aylık puantaj cetveli — uçtan uca (H4).
///
/// Mevcut günlük uçlar tek kayıt açıyordu; ekran "toplu" görünse de
/// arka planda personel başına bir istek atıyor ve yarısı geçip yarısı
/// düşebiliyordu. Buradaki uçlar TEK İSTEK, TEK İŞLEM.
///
/// İkinci güvence: doğrulanmamış tatil takvimiyle cetvel
/// DOLDURULMAZ — eksik bir tatil, o gün çalışılmış gibi puantaj ve
/// yanlış bordro üretir.
/// </summary>
[Collection("Integration")]
public sealed class AttendanceSheetTests(DatabaseFixture fixture)
{
    private const int Year = 2026;
    private const int Month = 3;

    private sealed record Context(Guid CompanyId, Guid PersonnelId);

    private async Task<HttpClient> ClientAsync() =>
        await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    private async Task<Context> CreateContextAsync(
        int workLocationType = 2, int? personnelWorkWeek = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        db.CompanyPayrollSettings.Add(new CompanyPayrollSettings
        {
            CompanyId = company.Id,
            Year = Year,
            MinimumWageGross = 33_030m,
            MinimumWageNet = 28_075m,
            SgkBaseFloor = 33_030m,
            SgkBaseCeiling = 247_725m,
            DailyWorkHours = 7.5m,
            WorkWeek = (int)WorkWeekDays.MondayToSaturday,
            HeadOfficeWorkWeek = (int)WorkWeekDays.MondayToFriday
        });

        var personnel = await TestDataFactory.CreatePersonnelAsync(
            db, company.Id, suffix);

        personnel.WorkLocationType = (WorkLocationType)workLocationType;
        personnel.WorkWeek = personnelWorkWeek;

        await db.SaveChangesAsync();

        return new Context(company.Id, personnel.Id);
    }

    /// <summary>Tatil takvimini kurar ve istenirse doğrular.</summary>
    private async Task SetUpCalendarAsync(
        HttpClient client, Guid companyId, bool verify)
    {
        await client.PostAsJsonAsync(
            $"/api/hr/tatil-takvimi/{Year}/sabit-tatiller?companyId={companyId}",
            new { });

        if (verify)
        {
            await client.PostAsJsonAsync(
                $"/api/hr/tatil-takvimi/{Year}/dogrula?companyId={companyId}",
                new { note = "Test" });
        }
    }

    private async Task<JsonElement> SheetAsync(HttpClient client, Guid companyId)
    {
        var response = await client.GetAsync(
            $"/api/hr/attendance/cetvel?companyId={companyId}&year={Year}&month={Month}");

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Task<HttpResponseMessage> GenerateAsync(
        HttpClient client, Guid companyId, bool overwrite = false) =>
        client.PostAsJsonAsync("/api/hr/attendance/cetvel/olustur", new
        {
            companyId,
            year = Year,
            month = Month,
            personnelIds = (Guid[]?)null,
            overwrite
        });

    private static JsonElement Cell(JsonElement sheet, int dayOfMonth) =>
        sheet.GetProperty("rows").EnumerateArray().First()
            .GetProperty("cells").EnumerateArray()
            .Single(x => x.GetProperty("date").GetString()!.EndsWith(
                $"-{dayOfMonth:D2}"));

    // ---------- Doğrulanmamış takvim ----------

    /// <summary>
    /// Doğrulanmamış takvimle cetvel doldurulmaz; sebebi de yazılır.
    /// </summary>
    [Fact]
    public async Task UnverifiedCalendar_BlocksGeneration()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await SetUpCalendarAsync(client, context.CompanyId, verify: false);

        var response = await GenerateAsync(client, context.CompanyId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Contains("doğrulanmadan", payload.GetProperty("message").GetString()!);
    }

    [Fact]
    public async Task UnverifiedCalendar_IsReportedOnTheSheet()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await SetUpCalendarAsync(client, context.CompanyId, verify: false);

        var sheet = await SheetAsync(client, context.CompanyId);

        Assert.False(sheet.GetProperty("holidayCalendarVerified").GetBoolean());
        Assert.Contains("doğrulanmadı", sheet.GetProperty("message").GetString()!);
    }

    // ---------- Doldurma ----------

    /// <summary>
    /// Bir personel için Mart 2026'nın 31 günü TEK istekte açılıyor.
    /// </summary>
    [Fact]
    public async Task Generate_CreatesEveryDayOfTheMonthInOneCall()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await SetUpCalendarAsync(client, context.CompanyId, verify: true);

        var response = await GenerateAsync(client, context.CompanyId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(31, payload.GetProperty("createdCount").GetInt32());
        Assert.Equal(1, payload.GetProperty("personnelCount").GetInt32());
    }

    /// <summary>Şantiye personelinde cumartesi çalışma günü.</summary>
    [Fact]
    public async Task SiteStaff_WorksOnSaturday()
    {
        var context = await CreateContextAsync(workLocationType: 2);
        var client = await ClientAsync();

        await SetUpCalendarAsync(client, context.CompanyId, verify: true);
        await GenerateAsync(client, context.CompanyId);

        var sheet = await SheetAsync(client, context.CompanyId);

        // 7 Mart 2026 cumartesi
        Assert.Equal((int)AttendanceStatus.Worked,
            Cell(sheet, 7).GetProperty("status").GetInt32());
    }

    /// <summary>
    /// Merkez kadrosunda cumartesi hafta tatili — ofise cumartesi
    /// yazmak gün ve mesai sayısını şişirirdi.
    /// </summary>
    [Fact]
    public async Task HeadOfficeStaff_RestsOnSaturday()
    {
        var context = await CreateContextAsync(workLocationType: 1);
        var client = await ClientAsync();

        await SetUpCalendarAsync(client, context.CompanyId, verify: true);
        await GenerateAsync(client, context.CompanyId);

        var sheet = await SheetAsync(client, context.CompanyId);

        Assert.Equal((int)AttendanceStatus.WeeklyHoliday,
            Cell(sheet, 7).GetProperty("status").GetInt32());
    }

    /// <summary>Personele özel hafta her ikisini de ezer.</summary>
    [Fact]
    public async Task PersonnelWorkWeek_OverridesEverything()
    {
        var context = await CreateContextAsync(
            workLocationType: 2, personnelWorkWeek: (int)WorkWeekDays.MondayToFriday);

        var client = await ClientAsync();

        await SetUpCalendarAsync(client, context.CompanyId, verify: true);
        await GenerateAsync(client, context.CompanyId);

        var sheet = await SheetAsync(client, context.CompanyId);
        var row = sheet.GetProperty("rows").EnumerateArray().First();

        Assert.Equal("Personel", row.GetProperty("workWeekSource").GetString());
        Assert.Equal((int)AttendanceStatus.WeeklyHoliday,
            Cell(sheet, 7).GetProperty("status").GetInt32());
    }

    /// <summary>İkinci çağrı var olan günleri tekrar açmaz.</summary>
    [Fact]
    public async Task GeneratingTwice_CreatesNothingNew()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await SetUpCalendarAsync(client, context.CompanyId, verify: true);
        await GenerateAsync(client, context.CompanyId);

        var second = await GenerateAsync(client, context.CompanyId);
        var payload = await second.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(0, payload.GetProperty("createdCount").GetInt32());
        Assert.Equal(0, payload.GetProperty("updatedCount").GetInt32());
    }

    // ---------- Kaydetme ----------

    [Fact]
    public async Task Save_UpdatesExistingDaysInOneCall()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await SetUpCalendarAsync(client, context.CompanyId, verify: true);
        await GenerateAsync(client, context.CompanyId);

        var response = await client.PostAsJsonAsync(
            "/api/hr/attendance/cetvel/kaydet",
            new
            {
                companyId = context.CompanyId,
                entries = new[]
                {
                    new
                    {
                        personnelId = context.PersonnelId,
                        workDate = new DateOnly(Year, Month, 2),
                        status = (int)AttendanceStatus.PaidLeave,
                        normalHours = 0m,
                        overtimeHours = 0m,
                        sundayHours = 0m,
                        publicHolidayHours = 0m,
                        description = "Yıllık izin"
                    }
                }
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("savedCount").GetInt32());

        var sheet = await SheetAsync(client, context.CompanyId);

        Assert.Equal((int)AttendanceStatus.PaidLeave,
            Cell(sheet, 2).GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Save_RejectsUnknownStatus()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/hr/attendance/cetvel/kaydet",
            new
            {
                companyId = context.CompanyId,
                entries = new[]
                {
                    new
                    {
                        personnelId = context.PersonnelId,
                        workDate = new DateOnly(Year, Month, 2),
                        status = 42,
                        normalHours = 0m,
                        overtimeHours = 0m,
                        sundayHours = 0m,
                        publicHolidayHours = 0m,
                        description = (string?)null
                    }
                }
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Save_RejectsNegativeHours()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/hr/attendance/cetvel/kaydet",
            new
            {
                companyId = context.CompanyId,
                entries = new[]
                {
                    new
                    {
                        personnelId = context.PersonnelId,
                        workDate = new DateOnly(Year, Month, 2),
                        status = (int)AttendanceStatus.Worked,
                        normalHours = -3m,
                        overtimeHours = 0m,
                        sundayHours = 0m,
                        publicHolidayHours = 0m,
                        description = (string?)null
                    }
                }
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Onay ----------

    [Fact]
    public async Task Approve_ApprovesTheWholeMonthInOneCall()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await SetUpCalendarAsync(client, context.CompanyId, verify: true);
        await GenerateAsync(client, context.CompanyId);

        var response = await client.PostAsJsonAsync(
            "/api/hr/attendance/cetvel/onayla",
            new
            {
                companyId = context.CompanyId,
                year = Year,
                month = Month,
                personnelIds = (Guid[]?)null
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(31, (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("approvedCount").GetInt32());

        var sheet = await SheetAsync(client, context.CompanyId);

        Assert.Equal(31, sheet.GetProperty("approvedCount").GetInt32());
    }

    /// <summary>
    /// ONAYLI gün ezilmez: onay, o günün birileri tarafından
    /// doğrulandığı anlamına geliyor.
    /// </summary>
    [Fact]
    public async Task ApprovedDays_AreNotOverwrittenByGeneration()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await SetUpCalendarAsync(client, context.CompanyId, verify: true);
        await GenerateAsync(client, context.CompanyId);

        await client.PostAsJsonAsync(
            "/api/hr/attendance/cetvel/onayla",
            new
            {
                companyId = context.CompanyId,
                year = Year,
                month = Month,
                personnelIds = (Guid[]?)null
            });

        var response = await GenerateAsync(client, context.CompanyId, overwrite: true);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(31, payload.GetProperty("skippedApprovedCount").GetInt32());
        Assert.Equal(0, payload.GetProperty("updatedCount").GetInt32());
    }

    [Fact]
    public async Task ApprovedDays_AreNotOverwrittenBySave()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await SetUpCalendarAsync(client, context.CompanyId, verify: true);
        await GenerateAsync(client, context.CompanyId);

        await client.PostAsJsonAsync(
            "/api/hr/attendance/cetvel/onayla",
            new
            {
                companyId = context.CompanyId,
                year = Year,
                month = Month,
                personnelIds = (Guid[]?)null
            });

        var response = await client.PostAsJsonAsync(
            "/api/hr/attendance/cetvel/kaydet",
            new
            {
                companyId = context.CompanyId,
                entries = new[]
                {
                    new
                    {
                        personnelId = context.PersonnelId,
                        workDate = new DateOnly(Year, Month, 2),
                        status = (int)AttendanceStatus.Absent,
                        normalHours = 0m,
                        overtimeHours = 0m,
                        sundayHours = 0m,
                        publicHolidayHours = 0m,
                        description = (string?)null
                    }
                }
            });

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(0, payload.GetProperty("savedCount").GetInt32());
        Assert.Equal(1, payload.GetProperty("skippedApprovedCount").GetInt32());

        var sheet = await SheetAsync(client, context.CompanyId);

        Assert.Equal((int)AttendanceStatus.Worked,
            Cell(sheet, 2).GetProperty("status").GetInt32());
    }

    /// <summary>
    /// Bütün ay tek işlemde yazılıyor: kısmi yazma olmamalı. Geçersiz
    /// tek satır bütün isteği reddediyor.
    /// </summary>
    [Fact]
    public async Task OneBadEntry_RejectsTheWholeBatch()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await client.PostAsJsonAsync(
            "/api/hr/attendance/cetvel/kaydet",
            new
            {
                companyId = context.CompanyId,
                entries = new object[]
                {
                    new
                    {
                        personnelId = context.PersonnelId,
                        workDate = new DateOnly(Year, Month, 2),
                        status = (int)AttendanceStatus.Worked,
                        normalHours = 7.5m,
                        overtimeHours = 0m,
                        sundayHours = 0m,
                        publicHolidayHours = 0m,
                        description = (string?)null
                    },
                    new
                    {
                        personnelId = context.PersonnelId,
                        workDate = new DateOnly(Year, Month, 3),
                        status = 99,
                        normalHours = 7.5m,
                        overtimeHours = 0m,
                        sundayHours = 0m,
                        publicHolidayHours = 0m,
                        description = (string?)null
                    }
                }
            });

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Geçerli olan satır da yazılmadı: ya hepsi ya hiçbiri.
        Assert.Equal(0, await db.AttendanceRecords
            .CountAsync(x => x.CompanyId == context.CompanyId));
    }
}
