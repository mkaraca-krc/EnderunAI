using System.Net;
using System.Net.Http.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Data.HumanResources;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.HumanResources;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Fazla mesai köprüsü: onaylanan saatin puantaja düşmesi.
///
/// Köprü YOKTU: ApprovedHours yalnızca Personel 360 ekranında
/// toplanıp gösteriliyor, AttendanceRecord.OvertimeHours'a hiçbir
/// yerde yazılmıyordu. Mesai onaylanıyor, sonra saha aynı saati
/// puantaja elle ikinci kez giriyordu; girmezse onaylı mesai bordroya
/// hiç yansımıyordu.
/// </summary>
[Collection("Integration")]
public sealed class OvertimeAttendanceBridgeTests(DatabaseFixture fixture)
{
    private static readonly DateTime WorkDate =
        new(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc);

    private sealed record Context(Guid CompanyId, Guid PersonnelId, Guid ProjectId);

    private async Task<Context> CreateContextAsync(
        string suffix, decimal? annualOvertimeLimit = null, int? consentYear = null)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);
        var personnel = await TestDataFactory.CreatePersonnelAsync(
            db, project.CompanyId, suffix);

        personnel.OvertimeConsentYear = consentYear;
        personnel.OvertimeConsentDate = consentYear is int year
            ? new DateTime(year, 1, 5, 0, 0, 0, DateTimeKind.Utc)
            : null;

        db.CompanyPayrollSettings.Add(new CompanyPayrollSettings
        {
            CompanyId = project.CompanyId,
            Year = WorkDate.Year,
            MinimumWageGross = 33_030m,
            SgkBaseFloor = 33_030m,
            SgkBaseCeiling = 247_725m,
            DailyWorkHours = 7.5m,
            AnnualOvertimeHourLimit = annualOvertimeLimit
        });

        await db.SaveChangesAsync();

        return new Context(project.CompanyId, personnel.Id, project.Id);
    }

    private Task<HttpClient> ClientAsync() =>
        AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    private async Task<Guid> CreateOvertimeAsync(
        HttpClient client,
        Context context,
        decimal hours,
        bool sunday = false,
        bool publicHoliday = false,
        DateTime? workDate = null)
    {
        var response = await client.PostAsJsonAsync("/api/hr/workforce/overtimes", new
        {
            companyId = context.CompanyId,
            personnelId = context.PersonnelId,
            projectId = context.ProjectId,
            workDate = workDate ?? WorkDate,
            requestedHours = hours,
            isSundayWork = sunday,
            isPublicHolidayWork = publicHoliday,
            reason = "Termin baskısı"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonDoc>();

        return payload!.Id;
    }

    private sealed record JsonDoc(Guid Id);

    private async Task<HttpResponseMessage> ApproveAsync(
        HttpClient client, Guid overtimeId) =>
        await client.PostAsync($"/api/hr/workforce/overtimes/{overtimeId}/approve", null);

    private async Task<AttendanceRecord?> LoadRecordAsync(
        Context context, DateTime? workDate = null)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.AttendanceRecords.AsNoTracking()
            .SingleOrDefaultAsync(x => x.PersonnelId == context.PersonnelId &&
                                       x.WorkDate == (workDate ?? WorkDate));
    }

    // ---------------- Köprünün kendisi ----------------

    /// <summary>
    /// Onaylanan fazla mesai o günün puantajına düşüyor ve talep
    /// hangi kayda yazdığını tutuyor.
    /// </summary>
    [Fact]
    public async Task ApprovedOvertime_LandsOnAttendance()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientAsync();

        var id = await CreateOvertimeAsync(client, context, 3m);

        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(client, id)).StatusCode);

        var record = await LoadRecordAsync(context);

        Assert.NotNull(record);
        Assert.Equal(3m, record!.OvertimeHours);
        Assert.Equal(0m, record.SundayHours);
        Assert.Equal(0m, record.PublicHolidayHours);

        using var scope = fixture.Factory.Services.CreateScope();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        var request = await hrDb.OvertimeRequests.AsNoTracking()
            .SingleAsync(x => x.Id == id);

        Assert.Equal(record.Id, request.AttendanceRecordId);
    }

    /// <summary>
    /// Hafta tatili ve genel tatil çalışması kendi alanlarına gider:
    /// ikisi 2× ile ücretlendiği için fazla mesaiyle (1,5×) aynı
    /// kovaya konamaz.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task HolidayWork_LandsOnItsOwnBucket(bool sunday, bool holiday)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientAsync();

        var id = await CreateOvertimeAsync(
            client, context, 8m, sunday: sunday, publicHoliday: holiday);

        await ApproveAsync(client, id);

        var record = await LoadRecordAsync(context);

        Assert.Equal(0m, record!.OvertimeHours);
        Assert.Equal(sunday ? 8m : 0m, record.SundayHours);
        Assert.Equal(holiday ? 8m : 0m, record.PublicHolidayHours);
    }

    /// <summary>
    /// MÜKERRER SAYIM OLMAZ: köprü eklemez, eşitler. Aynı talep
    /// yeniden onaylandığında saat iki katına çıkmıyor.
    /// </summary>
    [Fact]
    public async Task ReApproving_DoesNotDoubleTheHours()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientAsync();

        var id = await CreateOvertimeAsync(client, context, 4m);

        await ApproveAsync(client, id);
        await ApproveAsync(client, id);
        await ApproveAsync(client, id);

        var record = await LoadRecordAsync(context);

        Assert.Equal(4m, record!.OvertimeHours);
    }

    /// <summary>
    /// Aynı güne birden çok onaylı talep varsa hepsi tek puantaj
    /// satırında toplanıyor.
    /// </summary>
    [Fact]
    public async Task MultipleRequestsSameDay_AreSummed()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientAsync();

        var first = await CreateOvertimeAsync(client, context, 2m);
        var second = await CreateOvertimeAsync(client, context, 3m);

        await ApproveAsync(client, first);
        await ApproveAsync(client, second);

        var record = await LoadRecordAsync(context);

        Assert.Equal(5m, record!.OvertimeHours);
    }

    /// <summary>
    /// Puantaj kaydı yoksa açılıyor ve kullanıcı normal çalışma
    /// saatini girmesi için uyarılıyor.
    /// </summary>
    [Fact]
    public async Task WithoutAttendanceRecord_OneIsCreatedWithWarning()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientAsync();

        Assert.Null(await LoadRecordAsync(context));

        var id = await CreateOvertimeAsync(client, context, 2m);
        var response = await ApproveAsync(client, id);

        var raw = await response.Content.ReadAsStringAsync();

        Assert.Contains("puantaj kaydı yoktu", raw);

        var record = await LoadRecordAsync(context);

        Assert.NotNull(record);
        Assert.Equal(0m, record!.NormalHours);
        Assert.Equal(2m, record.OvertimeHours);
    }

    /// <summary>
    /// Onaylı puantaja dokunulmuyor: saati sessizce değiştirmek
    /// kesinleşmiş bordroyu kaydırırdı.
    /// </summary>
    [Fact]
    public async Task ApprovedAttendance_IsNotTouched()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.AttendanceRecords.Add(new AttendanceRecord
            {
                CompanyId = context.CompanyId,
                PersonnelId = context.PersonnelId,
                WorkDate = WorkDate,
                Status = (int)AttendanceStatus.Worked,
                NormalHours = 7.5m,
                IsApproved = true
            });

            await db.SaveChangesAsync();
        }

        var client = await ClientAsync();
        var id = await CreateOvertimeAsync(client, context, 3m);

        var response = await ApproveAsync(client, id);
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Contains("onaylanmış", raw);

        var record = await LoadRecordAsync(context);

        Assert.Equal(0m, record!.OvertimeHours);
    }

    // ---------------- Yıllık sınır uyarısı ----------------

    /// <summary>
    /// Sınır aşımı UYARIR ama engellemez: onay yine geçer.
    /// </summary>
    [Fact]
    public async Task ExceedingAnnualLimit_WarnsButDoesNotBlock()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, annualOvertimeLimit: 10m);
        var client = await ClientAsync();

        var first = await CreateOvertimeAsync(client, context, 8m);
        await ApproveAsync(client, first);

        var second = await CreateOvertimeAsync(
            client, context, 5m,
            workDate: WorkDate.AddDays(1));

        var response = await ApproveAsync(client, second);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();

        Assert.Contains("AŞILDI", raw);

        // Onay gerçekten geçmiş olmalı.
        var record = await LoadRecordAsync(context, WorkDate.AddDays(1));
        Assert.Equal(5m, record!.OvertimeHours);
    }

    /// <summary>Sınıra yaklaşınca da uyarılıyor (%90 eşiği).</summary>
    [Fact]
    public async Task ApproachingAnnualLimit_Warns()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, annualOvertimeLimit: 10m);
        var client = await ClientAsync();

        var id = await CreateOvertimeAsync(client, context, 9m);
        var raw = await (await ApproveAsync(client, id)).Content.ReadAsStringAsync();

        Assert.Contains("yaklaşıldı", raw);
    }

    /// <summary>
    /// Sınır tanımlı değilse uyarı üretilmiyor: koda gömülü bir 270
    /// varsayılmıyor.
    /// </summary>
    [Fact]
    public async Task WithoutLimit_NoWarningIsProduced()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientAsync();

        var id = await CreateOvertimeAsync(client, context, 12m);
        var raw = await (await ApproveAsync(client, id)).Content.ReadAsStringAsync();

        Assert.DoesNotContain("AŞILDI", raw);
        Assert.DoesNotContain("yaklaşıldı", raw);
    }

    /// <summary>
    /// Tatil çalışması yıllık sınır sayımına girmiyor: yasal sınırın
    /// konusu fazla çalışmadır.
    /// </summary>
    [Fact]
    public async Task HolidayWork_DoesNotCountTowardsAnnualLimit()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, annualOvertimeLimit: 10m);
        var client = await ClientAsync();

        var holiday = await CreateOvertimeAsync(
            client, context, 24m, publicHoliday: true);

        var raw = await (await ApproveAsync(client, holiday)).Content
            .ReadAsStringAsync();

        Assert.DoesNotContain("AŞILDI", raw);
    }

    // ---------------- Bordro ön kontrolü ----------------

    /// <summary>
    /// Muvafakati olmayan personele mesai ödemesi çıkıyorsa bordro
    /// ÜRETİLMEDEN önce uyarılıyor.
    /// </summary>
    [Fact]
    public async Task Readiness_WarnsAboutMissingConsent()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, annualOvertimeLimit: 270m);
        var client = await ClientAsync();

        var id = await CreateOvertimeAsync(client, context, 4m);
        await ApproveAsync(client, id);

        var raw = await (await client.GetAsync(
            $"/api/hr/bordro-on-kontrol?companyId={context.CompanyId}" +
            $"&year={WorkDate.Year}&month={WorkDate.Month}")).Content
            .ReadAsStringAsync();

        Assert.Contains("muvafakati yok", raw);
    }

    /// <summary>Muvafakati olan personel için uyarı çıkmıyor.</summary>
    [Fact]
    public async Task Readiness_IsQuietWhenConsentExists()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(
            suffix, annualOvertimeLimit: 270m, consentYear: WorkDate.Year);

        var client = await ClientAsync();

        var id = await CreateOvertimeAsync(client, context, 4m);
        await ApproveAsync(client, id);

        var raw = await (await client.GetAsync(
            $"/api/hr/bordro-on-kontrol?companyId={context.CompanyId}" +
            $"&year={WorkDate.Year}&month={WorkDate.Month}")).Content
            .ReadAsStringAsync();

        Assert.DoesNotContain("muvafakati yok", raw);
    }

    /// <summary>
    /// Yıllık sınır tanımsızsa ön kontrol bunu söylüyor — sessizce
    /// kontrolsüz geçmiyor.
    /// </summary>
    [Fact]
    public async Task Readiness_SaysWhenLimitIsUndefined()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientAsync();

        var id = await CreateOvertimeAsync(client, context, 4m);
        await ApproveAsync(client, id);

        var raw = await (await client.GetAsync(
            $"/api/hr/bordro-on-kontrol?companyId={context.CompanyId}" +
            $"&year={WorkDate.Year}&month={WorkDate.Month}")).Content
            .ReadAsStringAsync();

        Assert.Contains("fazla mesai sınırı tanımlanmadı", raw);
    }
}
