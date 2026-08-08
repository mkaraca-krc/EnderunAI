using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Data.HumanResources;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.HumanResources;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Bordro öncesi ön kontrol ve SGK bildirim dökümü (H9).
///
/// Bordro bugün eksik verili personeli sessizce içine alıp üretiyor;
/// sorun ancak resmî bildirim reddedilince, yani bordro çıktıktan
/// sonra görülüyor. Bu testler, üretmeden ÖNCE neyin eksik olduğunun
/// söylendiğini doğruluyor.
///
/// SGK tarafında dosya biçimi üretilmiyor: bildirim SGK ekranına elle
/// giriliyor, sistem yalnızca gereken alanları eksiksiz veriyor ve
/// eksik olanı işaretliyor.
/// </summary>
[Collection("Integration")]
public sealed class PayrollReadinessTests(DatabaseFixture fixture)
{
    private const int Year = 2026;
    private const int Month = 7;

    /// <summary>Sağlaması tutan, gerçek kişiye ait olmayan numara.</summary>
    private static string NewValidIdentity()
    {
        var prefix = Random.Shared.Next(100_000_000, 1_000_000_000).ToString();
        var digits = prefix.Select(x => x - '0').ToArray();

        var odd = digits[0] + digits[2] + digits[4] + digits[6] + digits[8];
        var even = digits[1] + digits[3] + digits[5] + digits[7];

        var tenth = ((odd * 7 - even) % 10 + 10) % 10;

        return prefix + tenth + (digits.Sum() + tenth) % 10;
    }

    private async Task<HttpClient> ClientAsync() =>
        await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    /// <param name="verified">Bordro parametreleri doğrulanmış mı.</param>
    /// <param name="complete">Personelin resmî alanları tam mı.</param>
    /// <param name="withSalaryCard">Dönemde yürürlükte ücret kartı var mı.</param>
    private async Task<(Guid CompanyId, Guid PersonnelId)> CreateAsync(
        bool verified = true, bool complete = true, bool withSalaryCard = true)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        var (company, branch, _) = await TestDataFactory.CreateCompanyStackAsync(
            db, suffix);

        db.CompanyPayrollSettings.Add(new CompanyPayrollSettings
        {
            CompanyId = company.Id,
            Year = Year,
            MinimumWageGross = 33_030m,
            MinimumWageNet = 28_075m,
            SgkBaseFloor = 33_030m,
            SgkBaseCeiling = 297_270m,
            VerifiedAtUtc = verified ? DateTime.UtcNow : null
        });

        var personnel = await TestDataFactory.CreatePersonnelAsync(
            db, company.Id, suffix);

        personnel.EmploymentStartDate =
            new DateTime(Year, Month, 3, 0, 0, 0, DateTimeKind.Utc);

        if (complete)
        {
            personnel.IdentityNumber = NewValidIdentity();
            personnel.BirthDate = new DateTime(1990, 5, 1, 0, 0, 0, DateTimeKind.Utc);
            personnel.SgkRegistrationNumber = "1234567890123";
            personnel.Phone = "5321234567";
            personnel.JobTitle = "Teknisyen";
            personnel.BranchId = branch.Id;
            personnel.WorkLocationType = WorkLocationType.HeadOffice;
        }

        await db.SaveChangesAsync();

        if (withSalaryCard)
        {
            hrDb.SalaryDefinitions.Add(new HrSalaryDefinition
            {
                CompanyId = company.Id,
                PersonnelId = personnel.Id,
                GrossSalary = 60_000m,
                NetSalary = 45_000m,
                CurrencyCode = "TRY",
                EffectiveStartDate = new DateTime(Year, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });

            await hrDb.SaveChangesAsync();
        }

        return (company.Id, personnel.Id);
    }

    private async Task<JsonElement> ReadinessAsync(HttpClient client, Guid companyId)
    {
        var response = await client.GetAsync(
            $"/api/hr/bordro-on-kontrol?companyId={companyId}&year={Year}&month={Month}");

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static IEnumerable<string> Texts(JsonElement payload, string property) =>
        payload.GetProperty(property).EnumerateArray().Select(x => x.GetString()!);

    // ---------------- Ön kontrol ----------------

    [Fact]
    public async Task CompleteSetup_CanCalculate()
    {
        var (companyId, _) = await CreateAsync();
        var client = await ClientAsync();

        var payload = await ReadinessAsync(client, companyId);

        Assert.True(payload.GetProperty("canCalculate").GetBoolean());
        Assert.Empty(payload.GetProperty("blockers").EnumerateArray());
    }

    /// <summary>
    /// Ücret kartı olmayan personel bordroya HİÇ giremez; bu engeldir,
    /// uyarı değil.
    /// </summary>
    [Fact]
    public async Task PersonnelWithoutSalaryCard_BlocksCalculation()
    {
        var (companyId, personnelId) = await CreateAsync(withSalaryCard: false);
        var client = await ClientAsync();

        var payload = await ReadinessAsync(client, companyId);

        Assert.False(payload.GetProperty("canCalculate").GetBoolean());
        Assert.Contains(Texts(payload, "blockers"), x => x.Contains("ücret kartı"));

        Assert.Equal(personnelId, payload.GetProperty("blocked")
            .EnumerateArray().Single().GetProperty("personnelId").GetGuid());
    }

    /// <summary>
    /// Doğrulanmamış bordro parametresiyle hesap yapılmaz: asgari
    /// ücret ve vergi dilimleri onaylanmadan üretilen bordro yanlıştır.
    /// </summary>
    [Fact]
    public async Task UnverifiedPayrollSettings_BlockCalculation()
    {
        var (companyId, _) = await CreateAsync(verified: false);
        var client = await ClientAsync();

        var payload = await ReadinessAsync(client, companyId);

        Assert.False(payload.GetProperty("canCalculate").GetBoolean());
        Assert.Contains(Texts(payload, "blockers"), x => x.Contains("doğrulanmamış"));
    }

    /// <summary>
    /// Eksik resmî alan bordroyu ENGELLEMEZ ama uyarır ve kimin eksik
    /// olduğunu adıyla söyler.
    /// </summary>
    [Fact]
    public async Task MissingOfficialFields_WarnWithoutBlocking()
    {
        var (companyId, personnelId) = await CreateAsync(complete: false);
        var client = await ClientAsync();

        var payload = await ReadinessAsync(client, companyId);

        Assert.True(payload.GetProperty("canCalculate").GetBoolean());
        Assert.Contains(Texts(payload, "warnings"), x => x.Contains("eksik veriyle"));

        var incomplete = payload.GetProperty("incomplete").EnumerateArray().Single();

        Assert.Equal(personnelId, incomplete.GetProperty("personnelId").GetGuid());
        Assert.Contains(
            incomplete.GetProperty("missingFields").EnumerateArray()
                .Select(x => x.GetString()!),
            x => x.Contains("SGK"));
    }

    [Fact]
    public async Task NoAttendance_IsWarned()
    {
        var (companyId, _) = await CreateAsync();
        var client = await ClientAsync();

        var payload = await ReadinessAsync(client, companyId);

        Assert.Contains(Texts(payload, "warnings"), x => x.Contains("puantaj kaydı yok"));
    }

    [Fact]
    public async Task UnverifiedHolidayCalendar_IsWarned()
    {
        var (companyId, _) = await CreateAsync();
        var client = await ClientAsync();

        var payload = await ReadinessAsync(client, companyId);

        Assert.False(payload.GetProperty("holidayCalendarVerified").GetBoolean());
        Assert.Contains(Texts(payload, "warnings"), x => x.Contains("tatil takvimi"));
    }

    [Fact]
    public async Task InvalidMonth_IsRejected()
    {
        var (companyId, _) = await CreateAsync();
        var client = await ClientAsync();

        var response = await client.GetAsync(
            $"/api/hr/bordro-on-kontrol?companyId={companyId}&year={Year}&month=13");

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------------- SGK dökümü ----------------

    private async Task<JsonElement> SgkAsync(HttpClient client, Guid companyId)
    {
        var response = await client.GetAsync(
            $"/api/hr/sgk-bildirim?companyId={companyId}" +
            $"&from={Year}-{Month:D2}-01&to={Year}-{Month:D2}-28");

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>
    /// Dönemde işe giren personel dökümde ve SGK ekranına girilecek
    /// alanlarla birlikte.
    /// </summary>
    [Fact]
    public async Task NewHire_AppearsInTheEntryList()
    {
        var (companyId, personnelId) = await CreateAsync();
        var client = await ClientAsync();

        var payload = await SgkAsync(client, companyId);
        var entry = payload.GetProperty("entries").EnumerateArray().Single();

        Assert.Equal(personnelId, entry.GetProperty("id").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(
            entry.GetProperty("identityNumber").GetString()));
        Assert.Empty(entry.GetProperty("missingFields").EnumerateArray());
        Assert.False(entry.GetProperty("noticeUploaded").GetBoolean());
    }

    /// <summary>
    /// Eksik alanı olan personel bildirilemez; hangi alanların eksik
    /// olduğu satırda yazıyor.
    /// </summary>
    [Fact]
    public async Task IncompleteHire_IsMarkedNotNotifiable()
    {
        var (companyId, _) = await CreateAsync(complete: false);
        var client = await ClientAsync();

        var payload = await SgkAsync(client, companyId);
        var entry = payload.GetProperty("entries").EnumerateArray().Single();

        Assert.Equal(1, payload.GetProperty("notNotifiableCount").GetInt32());

        var missing = entry.GetProperty("missingFields").EnumerateArray()
            .Select(x => x.GetString()!)
            .ToList();

        Assert.Contains(missing, x => x.Contains("kimlik"));
        Assert.Contains(missing, x => x.Contains("SGK"));
    }

    /// <summary>
    /// Bildirimin yapıldığı, özlük dosyasına yüklenen bildirgeden
    /// okunuyor — ayrı bir "bildirildi" bayrağı tutulmuyor.
    /// </summary>
    [Fact]
    public async Task UploadedNotice_MarksTheEntryAsNotified()
    {
        var (companyId, personnelId) = await CreateAsync();
        var client = await ClientAsync();

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.PersonnelDocuments.Add(new PersonnelDocument
            {
                CompanyId = companyId,
                PersonnelId = personnelId,
                DocumentType = PersonnelDocumentType.SgkEntryNotice,
                DocumentName = "SGK işe giriş bildirgesi"
            });

            await db.SaveChangesAsync();
        }

        var payload = await SgkAsync(client, companyId);

        Assert.True(payload.GetProperty("entries").EnumerateArray().Single()
            .GetProperty("noticeUploaded").GetBoolean());
    }

    [Fact]
    public async Task ReversedDateRange_IsRejected()
    {
        var (companyId, _) = await CreateAsync();
        var client = await ClientAsync();

        var response = await client.GetAsync(
            $"/api/hr/sgk-bildirim?companyId={companyId}" +
            $"&from={Year}-07-28&to={Year}-07-01");

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Dönem dışında işe girenler dökümde yok: bildirim aylık yapılır
    /// ve listenin karışmaması gerekir.
    /// </summary>
    [Fact]
    public async Task HireOutsideTheRange_IsExcluded()
    {
        var (companyId, _) = await CreateAsync();
        var client = await ClientAsync();

        var response = await client.GetAsync(
            $"/api/hr/sgk-bildirim?companyId={companyId}&from=2026-01-01&to=2026-01-31");

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(0, payload.GetProperty("entryCount").GetInt32());
    }
}
