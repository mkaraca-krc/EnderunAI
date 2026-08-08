using System.Net;
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
/// Personel kartı veri bütünlüğü — uçtan uca (H1).
///
/// İki güvence:
/// - Kimlik numarası BOŞ bırakılabilir ama YANLIŞ girilemez. Eksik alan
///   uyarıyla yönetilir; yanlış alan sessiz bir hatadır.
/// - Veri eksikleri ucu TUTAR DÖNDÜRMEZ. Ücret kartının var olup
///   olmadığına bakar, rakamına bakmaz: eksik veri görmek maaş görme
///   yetkisi gerektirmemeli.
/// </summary>
[Collection("Integration")]
public sealed class PersonnelDataIntegrationTests(DatabaseFixture fixture)
{
    /// <summary>
    /// Sağlaması tutan, gerçek kişiye ait olmayan numara üretir.
    ///
    /// Kimlik numarası tekilliği ŞİRKETTEN BAĞIMSIZ olduğu için
    /// testler sabit bir numarayı paylaşamaz; ikinci kayıt çakışırdı.
    /// </summary>
    private static string NewValidIdentity()
    {
        var prefix = Random.Shared.Next(100_000_000, 1_000_000_000).ToString();
        var digits = prefix.Select(x => x - '0').ToArray();

        var odd = digits[0] + digits[2] + digits[4] + digits[6] + digits[8];
        var even = digits[1] + digits[3] + digits[5] + digits[7];

        var tenth = ((odd * 7 - even) % 10 + 10) % 10;
        var eleventh = (digits.Sum() + tenth) % 10;

        return prefix + tenth + eleventh;
    }

    /// <summary>Son hanesi bozulmuş numara — sağlama tutmaz.</summary>
    private const string InvalidIdentity = "12345678951";

    private async Task<HttpClient> ClientAsync() =>
        await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    private async Task<Guid> CreateCompanyAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        return company.Id;
    }

    private static object NewPersonnel(
        Guid companyId, string suffix, string? identity) =>
        new
        {
            companyId,
            employeeNumber = $"PRS-{suffix}",
            firstName = "Ali",
            lastName = "Veli",
            identityNumber = identity,
            phone = "5321234567",
            sgkRegistrationNumber = "1234567890123",
            birthDate = new DateTime(1990, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            employmentStartDate = new DateTime(2020, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            jobTitle = "Elektrik Teknisyeni"
        };

    // ---------- Kimlik numarası ----------

    [Fact]
    public async Task InvalidIdentityNumber_IsRejectedOnCreate()
    {
        var companyId = await CreateCompanyAsync(Guid.NewGuid().ToString("N")[..8]);
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/personnel",
            NewPersonnel(companyId, Guid.NewGuid().ToString("N")[..8], InvalidIdentity));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Contains("algoritma", payload.GetProperty("message").GetString()!);
    }

    [Fact]
    public async Task ShortIdentityNumber_IsRejectedWithItsOwnReason()
    {
        var companyId = await CreateCompanyAsync(Guid.NewGuid().ToString("N")[..8]);
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/personnel",
            NewPersonnel(companyId, Guid.NewGuid().ToString("N")[..8], "123"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Contains("11 haneli", payload.GetProperty("message").GetString()!);
    }

    /// <summary>
    /// Kimlik numarası olmadan kayıt açılabilir; canlıda numarası
    /// olmayan personel var ve onları kilitlemek işe yaramazdı.
    /// </summary>
    [Fact]
    public async Task BlankIdentityNumber_IsAccepted()
    {
        var companyId = await CreateCompanyAsync(Guid.NewGuid().ToString("N")[..8]);
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/personnel",
            NewPersonnel(companyId, Guid.NewGuid().ToString("N")[..8], null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ValidIdentityNumber_IsAccepted()
    {
        var companyId = await CreateCompanyAsync(Guid.NewGuid().ToString("N")[..8]);
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/personnel",
            NewPersonnel(
                companyId, Guid.NewGuid().ToString("N")[..8], NewValidIdentity()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task InvalidIdentityNumber_IsRejectedOnUpdate()
    {
        var companyId = await CreateCompanyAsync(Guid.NewGuid().ToString("N")[..8]);
        var client = await ClientAsync();

        var created = await client.PostAsJsonAsync(
            "/api/personnel",
            NewPersonnel(companyId, Guid.NewGuid().ToString("N")[..8], null));

        var id = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/personnel/{id}",
            new
            {
                firstName = "Ali",
                lastName = "Veli",
                identityNumber = InvalidIdentity,
                status = (int)PersonnelStatus.Active,
                isActive = true
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Kimlik numarasına dokunulmayan bir güncelleme, o alanın
    /// geçmişteki durumu ne olursa olsun engellenmemeli — aksi halde
    /// tek bir bozuk alan yüzünden telefon bile güncellenemezdi.
    /// </summary>
    [Fact]
    public async Task UpdateWithUnchangedIdentity_IsAllowed()
    {
        var companyId = await CreateCompanyAsync(Guid.NewGuid().ToString("N")[..8]);
        var client = await ClientAsync();

        var identity = NewValidIdentity();

        var created = await client.PostAsJsonAsync(
            "/api/personnel",
            NewPersonnel(companyId, Guid.NewGuid().ToString("N")[..8], identity));

        var id = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/personnel/{id}",
            new
            {
                firstName = "Ali",
                lastName = "Veli",
                identityNumber = identity,
                phone = "5559998877",
                status = (int)PersonnelStatus.Active,
                isActive = true
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------- Veri eksikleri ucu ----------

    private async Task<JsonElement> CompletenessAsync(
        HttpClient client, Guid companyId)
    {
        var response = await client.GetAsync(
            $"/api/hr/personnel/veri-eksikleri?companyId={companyId}");

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>
    /// Personel numarası kayıtta BÜYÜK harfe çevriliyor; arama da
    /// harf duyarsız olmalı.
    /// </summary>
    private static JsonElement Person(JsonElement payload, string employeeNumber) =>
        payload.GetProperty("items").EnumerateArray()
            .Single(x => string.Equals(
                x.GetProperty("employeeNumber").GetString(),
                employeeNumber,
                StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Ücret kartı olmayan personel bordroya giremez; bu en sert
    /// kademe ve ayrıca işaretleniyor.
    /// </summary>
    [Fact]
    public async Task PersonnelWithoutSalaryCard_IsNotPayrollReady()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyAsync(suffix);
        var client = await ClientAsync();

        await client.PostAsJsonAsync(
            "/api/personnel", NewPersonnel(companyId, suffix, NewValidIdentity()));

        var payload = await CompletenessAsync(client, companyId);
        var person = Person(payload, $"PRS-{suffix}");

        Assert.False(person.GetProperty("payrollReady").GetBoolean());
        Assert.Contains(
            person.GetProperty("issues").EnumerateArray(),
            x => x.GetProperty("field").GetString() == "salaryCard");
    }

    /// <summary>
    /// Yürürlükteki ücret kartı olan personelde bordro engeli kalkar.
    /// </summary>
    [Fact]
    public async Task PersonnelWithSalaryCard_IsPayrollReady()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyAsync(suffix);
        var client = await ClientAsync();

        var created = await client.PostAsJsonAsync(
            "/api/personnel", NewPersonnel(companyId, suffix, NewValidIdentity()));

        var personnelId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

            hrDb.SalaryDefinitions.Add(new HrSalaryDefinition
            {
                CompanyId = companyId,
                PersonnelId = personnelId,
                GrossSalary = 60_000m,
                NetSalary = 45_000m,
                CurrencyCode = "TRY",
                EffectiveStartDate = new DateTime(
                    2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });

            await hrDb.SaveChangesAsync();
        }

        var payload = await CompletenessAsync(client, companyId);
        var person = Person(payload, $"PRS-{suffix}");

        Assert.True(person.GetProperty("payrollReady").GetBoolean());
    }

    /// <summary>
    /// SGK sicil eksikliği bordroyu engellemez, resmî bildirimi
    /// engeller. İkisi ayrı kademe.
    /// </summary>
    [Fact]
    public async Task MissingSgkNumber_BlocksOfficialReadinessOnly()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyAsync(suffix);
        var client = await ClientAsync();

        await client.PostAsJsonAsync("/api/personnel", new
        {
            companyId,
            employeeNumber = $"PRS-{suffix}",
            firstName = "Veli",
            lastName = "Ali",
            identityNumber = NewValidIdentity(),
            phone = "5321234567",
            birthDate = new DateTime(1990, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            employmentStartDate = new DateTime(2020, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            jobTitle = "Teknisyen"
        });

        var payload = await CompletenessAsync(client, companyId);
        var person = Person(payload, $"PRS-{suffix}");

        Assert.False(person.GetProperty("officialReady").GetBoolean());
        Assert.Contains(
            person.GetProperty("issues").EnumerateArray(),
            x => x.GetProperty("field").GetString() == "sgkRegistrationNumber" &&
                 x.GetProperty("severityName").GetString() == "Resmî bildirim engeli");
    }

    /// <summary>
    /// Özet, hangi alanın toplu tamamlamaya değdiğini gösteriyor.
    /// </summary>
    [Fact]
    public async Task Summary_ReportsMissingFieldCounts()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyAsync(suffix);
        var client = await ClientAsync();

        await client.PostAsJsonAsync("/api/personnel", new
        {
            companyId,
            employeeNumber = $"PRS-{suffix}",
            firstName = "Ayşe",
            lastName = "Yılmaz",
            employmentStartDate = new DateTime(2021, 3, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        var payload = await CompletenessAsync(client, companyId);

        Assert.Equal(1, payload.GetProperty("total").GetInt32());
        Assert.Equal(0, payload.GetProperty("completeCount").GetInt32());

        var byField = payload.GetProperty("byField");

        Assert.Equal(1, byField.GetProperty("phone").GetInt32());
        Assert.Equal(1, byField.GetProperty("identityNumber").GetInt32());
        Assert.Equal(1, byField.GetProperty("salaryCard").GetInt32());
    }

    /// <summary>
    /// Uç ücret TUTARI döndürmüyor: eksik veri görmek maaş görme
    /// yetkisi gerektirmemeli.
    /// </summary>
    [Fact]
    public async Task Completeness_DoesNotLeakSalaryAmounts()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyAsync(suffix);
        var client = await ClientAsync();

        await client.PostAsJsonAsync(
            "/api/personnel", NewPersonnel(companyId, suffix, NewValidIdentity()));

        var response = await client.GetAsync(
            $"/api/hr/personnel/veri-eksikleri?companyId={companyId}");

        var raw = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("grossSalary", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("netSalary", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("monthlySalary", raw, StringComparison.OrdinalIgnoreCase);
    }
}
