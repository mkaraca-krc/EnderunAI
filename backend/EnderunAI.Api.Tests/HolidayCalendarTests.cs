using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.HumanResources;
using EnderunAI.Api.Services.Schedule;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Resmî tatil takvimi ve çalışma haftası — uçtan uca (H3).
///
/// Asıl güvence: takvim DOĞRULANMADAN puantaj cetvelini doldurmakta
/// kullanılmaz ve her değişiklik doğrulamayı düşürür. Doğrulanmış bir
/// takvime sessizce gün eklenebilseydi damga anlamını kaybederdi —
/// eksik bir tatil, o gün çalışılmış gibi puantaj ve yanlış bordro
/// demek.
/// </summary>
[Collection("Integration")]
public sealed class HolidayCalendarTests(DatabaseFixture fixture)
{
    private const int Year = 2026;

    private async Task<HttpClient> ClientAsync() =>
        await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    /// <summary>Şirket ve o yıla ait bordro parametreleri.</summary>
    private async Task<Guid> CreateCompanyAsync()
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
            SgkBaseCeiling = 247_725m
        });

        await db.SaveChangesAsync();

        return company.Id;
    }

    private async Task<JsonElement> GetAsync(HttpClient client, Guid companyId)
    {
        var response = await client.GetAsync(
            $"/api/hr/tatil-takvimi?companyId={companyId}&year={Year}");

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Task<HttpResponseMessage> SeedFixedAsync(
        HttpClient client, Guid companyId) =>
        client.PostAsJsonAsync(
            $"/api/hr/tatil-takvimi/{Year}/sabit-tatiller?companyId={companyId}",
            new { });

    private static Task<HttpResponseMessage> VerifyAsync(
        HttpClient client, Guid companyId) =>
        client.PostAsJsonAsync(
            $"/api/hr/tatil-takvimi/{Year}/dogrula?companyId={companyId}",
            new { note = "Resmî ilanla karşılaştırıldı." });

    // ---------- Takvim kurulumu ----------

    [Fact]
    public async Task MissingCalendar_IsReportedNotInvented()
    {
        var companyId = await CreateCompanyAsync();
        var client = await ClientAsync();

        var payload = await GetAsync(client, companyId);

        Assert.False(payload.GetProperty("exists").GetBoolean());
        Assert.False(payload.GetProperty("isVerified").GetBoolean());
        Assert.Contains("açılmamış", payload.GetProperty("message").GetString()!);
    }

    [Fact]
    public async Task FixedHolidays_AreSeeded()
    {
        var companyId = await CreateCompanyAsync();
        var client = await ClientAsync();

        var response = await SeedFixedAsync(client, companyId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(8, (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("addedCount").GetInt32());

        var payload = await GetAsync(client, companyId);

        Assert.Equal(8, payload.GetProperty("calendar")
            .GetProperty("days").GetArrayLength());
    }

    /// <summary>Aynı gün ikinci kez eklenmez.</summary>
    [Fact]
    public async Task SeedingTwice_AddsNothing()
    {
        var companyId = await CreateCompanyAsync();
        var client = await ClientAsync();

        await SeedFixedAsync(client, companyId);
        var second = await SeedFixedAsync(client, companyId);

        Assert.Equal(0, (await second.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("addedCount").GetInt32());
    }

    /// <summary>
    /// Dini bayram ilk gününden türetilir; sistem tarihi tahmin etmez.
    /// </summary>
    [Fact]
    public async Task ReligiousHoliday_IsDerivedFromItsFirstDay()
    {
        var companyId = await CreateCompanyAsync();
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/hr/tatil-takvimi/{Year}/dini-bayram?companyId={companyId}",
            new
            {
                kind = (int)ReligiousHolidayKind.Kurban,
                firstDay = new DateOnly(2026, 5, 27)
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Arife + 4 tam gün
        Assert.Equal(5, (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("addedCount").GetInt32());

        var days = (await GetAsync(client, companyId))
            .GetProperty("calendar").GetProperty("days").EnumerateArray()
            .ToList();

        Assert.Contains(days, x => x.GetProperty("date").GetString() == "2026-05-26" &&
                                   x.GetProperty("isHalfDay").GetBoolean());
        Assert.Contains(days, x => x.GetProperty("date").GetString() == "2026-05-30");
    }

    [Fact]
    public async Task ReligiousHolidayOutsideTheYear_IsRejected()
    {
        var companyId = await CreateCompanyAsync();
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/hr/tatil-takvimi/{Year}/dini-bayram?companyId={companyId}",
            new
            {
                kind = (int)ReligiousHolidayKind.Ramazan,
                firstDay = new DateOnly(2027, 3, 10)
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Doğrulama ----------

    [Fact]
    public async Task NewCalendar_IsNotVerified()
    {
        var companyId = await CreateCompanyAsync();
        var client = await ClientAsync();

        await SeedFixedAsync(client, companyId);

        var payload = await GetAsync(client, companyId);

        Assert.False(payload.GetProperty("isVerified").GetBoolean());
        Assert.Contains("doğrulanmadı", payload.GetProperty("message").GetString()!);
    }

    [Fact]
    public async Task VerifiedCalendar_ReportsItsStamp()
    {
        var companyId = await CreateCompanyAsync();
        var client = await ClientAsync();

        await SeedFixedAsync(client, companyId);

        var response = await VerifyAsync(client, companyId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await GetAsync(client, companyId);

        Assert.True(payload.GetProperty("isVerified").GetBoolean());
    }

    /// <summary>
    /// Doğrulanmış takvime gün eklenince damga DÜŞER; aksi halde
    /// "kontrol edildi" iddiası yalan olurdu.
    /// </summary>
    [Fact]
    public async Task AddingADay_InvalidatesVerification()
    {
        var companyId = await CreateCompanyAsync();
        var client = await ClientAsync();

        await SeedFixedAsync(client, companyId);
        await VerifyAsync(client, companyId);

        Assert.True((await GetAsync(client, companyId))
            .GetProperty("isVerified").GetBoolean());

        await client.PostAsJsonAsync(
            $"/api/hr/tatil-takvimi/{Year}/gun?companyId={companyId}",
            new
            {
                date = new DateOnly(2026, 6, 15),
                name = "Şantiye kapalı",
                isHalfDay = false
            });

        Assert.False((await GetAsync(client, companyId))
            .GetProperty("isVerified").GetBoolean());
    }

    [Fact]
    public async Task RemovingADay_AlsoInvalidatesVerification()
    {
        var companyId = await CreateCompanyAsync();
        var client = await ClientAsync();

        await SeedFixedAsync(client, companyId);
        await VerifyAsync(client, companyId);

        var dayId = (await GetAsync(client, companyId))
            .GetProperty("calendar").GetProperty("days").EnumerateArray()
            .First().GetProperty("id").GetGuid();

        await client.DeleteAsync($"/api/hr/tatil-takvimi/gun/{dayId}");

        Assert.False((await GetAsync(client, companyId))
            .GetProperty("isVerified").GetBoolean());
    }

    [Fact]
    public async Task EmptyCalendar_CannotBeVerified()
    {
        var companyId = await CreateCompanyAsync();
        var client = await ClientAsync();

        // Takvimi boş olarak açtırmak için tek gün ekleyip siliyoruz.
        await client.PostAsJsonAsync(
            $"/api/hr/tatil-takvimi/{Year}/gun?companyId={companyId}",
            new { date = new DateOnly(2026, 6, 15), name = "Geçici", isHalfDay = false });

        var dayId = (await GetAsync(client, companyId))
            .GetProperty("calendar").GetProperty("days").EnumerateArray()
            .Single().GetProperty("id").GetGuid();

        await client.DeleteAsync($"/api/hr/tatil-takvimi/gun/{dayId}");

        var response = await VerifyAsync(client, companyId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DuplicateDay_IsRejected()
    {
        var companyId = await CreateCompanyAsync();
        var client = await ClientAsync();

        await SeedFixedAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/hr/tatil-takvimi/{Year}/gun?companyId={companyId}",
            new
            {
                date = new DateOnly(2026, 1, 1),
                name = "Yılbaşı (tekrar)",
                isHalfDay = false
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Çalışma haftası ----------

    [Fact]
    public async Task CompanyWorkWeek_DefaultsToMondayThroughSaturday()
    {
        var companyId = await CreateCompanyAsync();
        var client = await ClientAsync();

        var payload = await GetAsync(client, companyId);

        Assert.Equal((int)WorkWeekDays.MondayToSaturday,
            payload.GetProperty("workWeek").GetInt32());
        Assert.Equal("Pazartesi–Cumartesi",
            payload.GetProperty("workWeekName").GetString());
    }

    /// <summary>
    /// Merkez kadrosu ayrı ayarlanabiliyor: ofise cumartesi yazmak gün
    /// ve mesai sayısını şişirirdi.
    /// </summary>
    [Fact]
    public async Task HeadOfficeWorkWeek_CanBeSetSeparately()
    {
        var companyId = await CreateCompanyAsync();
        var client = await ClientAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/hr/tatil-takvimi/{Year}/calisma-haftasi?companyId={companyId}",
            new
            {
                workWeek = (int)WorkWeekDays.MondayToSaturday,
                headOfficeWorkWeek = (int)WorkWeekDays.MondayToFriday
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await GetAsync(client, companyId);

        Assert.Equal((int)WorkWeekDays.MondayToFriday,
            payload.GetProperty("headOfficeWorkWeek").GetInt32());
        Assert.Equal("Pazartesi–Cuma",
            payload.GetProperty("headOfficeWorkWeekName").GetString());
    }

    [Fact]
    public async Task EmptyWorkWeek_IsRejected()
    {
        var companyId = await CreateCompanyAsync();
        var client = await ClientAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/hr/tatil-takvimi/{Year}/calisma-haftasi?companyId={companyId}",
            new { workWeek = 0, headOfficeWorkWeek = (int?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Çalışma haftası bordro parametrelerine bağlı; o yıl için
    /// parametre yoksa ayar kaydedilemez.
    /// </summary>
    [Fact]
    public async Task WorkWeekWithoutPayrollSettings_IsRejected()
    {
        var companyId = await CreateCompanyAsync();
        var client = await ClientAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/hr/tatil-takvimi/2030/calisma-haftasi?companyId={companyId}",
            new
            {
                workWeek = (int)WorkWeekDays.MondayToFriday,
                headOfficeWorkWeek = (int?)null
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
