using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Faz E5: ücret gizliliği sızma testleri. Saha ve teknik roller
/// personel listesini görebilir ama hiçbir ücret rakamına
/// erişememelidir — ne uçtan, ne de projeksiyonda.
///
/// Gizlemenin yalnızca arayüzde yapılması yeterli değildi: bu testler
/// API'nin kendisini doğruluyor.
/// </summary>
[Collection("Integration")]
public sealed class SalaryPrivacyTests(DatabaseFixture fixture)
{
    /// <summary>
    /// Ücret rakamı görmemesi gereken roller. Şantiye Şefi, Formen ve
    /// Teknik Koordinatör personel listesini görebiliyor (personnel.view)
    /// — bu yüzden ücret gizliliği asıl burada sınanıyor.
    /// </summary>
    public static TheoryData<string> RestrictedRoles =>
        new()
        {
            "Şantiye Şefi",
            "Formen",
            "Sekreterya",
            "Teknik Ofis",
            "Teknik Koordinatör"
        };

    private async Task<HttpClient> CreateClientForRoleAsync(string roleName)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();

        const string password = "SalaryPrivacy!2026";
        var username = $"test-salary-{Guid.NewGuid():N}"[..40];
        var hash = passwordService.Hash(password);

        var user = new AppUser
        {
            Username = username,
            FullName = $"Test {roleName}",
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            IsActive = true,
            WorkHoursExempt = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var role = await db.Roles.SingleAsync(x => x.Name == roleName);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        db.UserDataScopes.Add(new UserDataScope
        {
            UserId = user.Id,
            ScopeType = DataScopeType.All
        });
        await db.SaveChangesAsync();

        var client = fixture.Factory.CreateClient();
        var token = await AuthHelper.LoginAsync(client, username, password);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private async Task<Guid> CreateCompanyWithSalariedPersonnelAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        db.Personnel.Add(new Personnel
        {
            CompanyId = company.Id,
            EmployeeNumber = $"GIZLI-{suffix}",
            FirstName = "Gizli",
            LastName = "Ucret",
            MonthlySalary = 123_456m,
            Status = PersonnelStatus.Active
        });
        await db.SaveChangesAsync();

        return company.Id;
    }

    [Theory]
    [MemberData(nameof(RestrictedRoles))]
    public async Task RestrictedRoles_CannotReadPayrollRecords(string roleName)
    {
        var client = await CreateClientForRoleAsync(roleName);

        var response = await client.GetAsync("/api/hr/payroll/records");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(RestrictedRoles))]
    public async Task RestrictedRoles_CannotReadSalaryDefinitions(string roleName)
    {
        var client = await CreateClientForRoleAsync(roleName);

        var response = await client.GetAsync("/api/hr/payroll/salary-definitions");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(RestrictedRoles))]
    public async Task RestrictedRoles_CannotReadCostReport(string roleName)
    {
        var client = await CreateClientForRoleAsync(roleName);

        var response = await client.GetAsync(
            $"/api/hr/payroll/cost-report?companyId={Guid.NewGuid()}&year=2026&month=1");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(RestrictedRoles))]
    public async Task RestrictedRoles_CannotReadPayrollSettings(string roleName)
    {
        var client = await CreateClientForRoleAsync(roleName);

        var response = await client.GetAsync(
            $"/api/payroll-settings?companyId={Guid.NewGuid()}&year=2026");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Personel listesini görebilen ama ücret izni olmayan rol, maaş
    /// alanını null görmeli. Bu, gizlemenin arayüzde değil projeksiyonda
    /// yapıldığının kanıtı.
    /// </summary>
    [Fact]
    public async Task PersonnelList_MasksSalaryForRoleWithoutSalaryPermission()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyWithSalariedPersonnelAsync(suffix);

        // Şantiye Şefi personnel.view iznine sahip ama salary.view'a değil.
        var client = await CreateClientForRoleAsync("Şantiye Şefi");

        var response = await client.GetAsync($"/api/personnel?companyId={companyId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var person = payload.EnumerateArray().Single();

        // Personel görünür...
        Assert.Equal("Gizli", person.GetProperty("firstName").GetString());

        // ...ama maaşı görünmez.
        Assert.Equal(JsonValueKind.Null, person.GetProperty("monthlySalary").ValueKind);
    }

    [Fact]
    public async Task PersonnelList_ShowsSalaryForAuthorizedRole()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyWithSalariedPersonnelAsync(suffix);

        var client = await CreateClientForRoleAsync("İK Sorumlusu");

        var response = await client.GetAsync($"/api/personnel?companyId={companyId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var person = payload.EnumerateArray().Single();

        Assert.Equal(123_456m, person.GetProperty("monthlySalary").GetDecimal());
    }

    [Fact]
    public async Task PersonnelDetail_MasksSalaryForRoleWithoutSalaryPermission()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyWithSalariedPersonnelAsync(suffix);

        Guid personnelId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            personnelId = await db.Personnel
                .Where(x => x.CompanyId == companyId)
                .Select(x => x.Id)
                .SingleAsync();
        }

        var client = await CreateClientForRoleAsync("Formen");

        var response = await client.GetAsync($"/api/personnel/{personnelId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("monthlySalary").ValueKind);
    }

    [Fact]
    public async Task AuthorizedRole_CanReadCostReport()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyWithSalariedPersonnelAsync(suffix);

        var client = await CreateClientForRoleAsync("Finans Sorumlusu");

        var response = await client.GetAsync(
            $"/api/hr/payroll/cost-report?companyId={companyId}&year=2026&month=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, payload.GetProperty("personnelCount").GetInt32());
    }
}
