using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Data.HumanResources;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.HumanResources;
using EnderunAI.Api.Security;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Ek ödeme (elden) izolasyonu sızma testleri. Elden ödenen tutarlar ve
/// elden tazminat farkı, extra_payment.view izni olmayan hiç kimseye
/// görünmemeli — uçtan da, projeksiyondan da.
///
/// Bu paketin en hassas kısmı: elden ödeme bilgisinin sızması hem
/// personel gizliliği hem şirket riski.
/// </summary>
[Collection("Integration")]
public sealed class ExtraPaymentPrivacyTests(DatabaseFixture fixture)
{
    /// <summary>
    /// Elden ödemeyi ASLA görmemesi gereken roller. İK Sorumlusu
    /// bilerek listede: bordroyu yönetir ama elden tutarları görmez.
    /// </summary>
    public static TheoryData<string> RestrictedRoles =>
        new()
        {
            "Şantiye Şefi",
            "Formen",
            "Sekreterya",
            "Teknik Ofis",
            "Teknik Koordinatör",
            "İK Sorumlusu"
        };

    /// <summary>Elden ödemeyi görmesi gereken roller.</summary>
    public static TheoryData<string> AuthorizedRoles =>
        new() { "Genel Müdür", "Finans Sorumlusu", "Ön Muhasebe" };

    private async Task<HttpClient> CreateClientForRoleAsync(string roleName)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();

        const string password = "ExtraPayment!2026";
        var username = $"test-extra-{Guid.NewGuid():N}"[..40];
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

    /// <summary>
    /// Resmi ücreti, elden ödemesi ve 3 yıllık kıdemi olan bir personel
    /// kurar — tazminat simülasyonunun gerçek sayı üretmesi için.
    /// </summary>
    private async Task<Guid> CreatePersonnelWithExtraPaymentAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        var start = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var personnel = new Personnel
        {
            CompanyId = company.Id,
            EmployeeNumber = $"TAZ-{suffix}",
            FirstName = "Tazminat",
            LastName = "Testi",
            EmploymentStartDate = start,
            Status = PersonnelStatus.Active
        };
        db.Personnel.Add(personnel);

        db.PersonnelExtraPayments.Add(new PersonnelExtraPayment
        {
            CompanyId = company.Id,
            PersonnelId = personnel.Id,
            MonthlyAmount = 15_000m,
            EffectiveStartDate = start,
            Note = "Sızma testi"
        });

        // Bordro parametreleri uygulama açılışında seed edilir; testte
        // sonradan oluşan şirketin kendi kaydı olmaz.
        var settings = new CompanyPayrollSettings
        {
            CompanyId = company.Id,
            Year = 2026,
            MinimumWageGross = 33_030m,
            MinimumWageNet = 28_075.50m,
            SgkBaseFloor = 33_030m,
            SgkBaseCeiling = 297_270m,
            SeveranceCeiling = 53_919.68m,
            SeveranceCeilingPeriodNote = "01.01.2026-30.06.2026",
            VerifiedAtUtc = DateTime.UtcNow,
            TaxBrackets =
            [
                new() { Order = 1, LowerBound = 0m, UpperBound = 190_000m, Rate = 15m },
                new() { Order = 2, LowerBound = 190_000m, UpperBound = 400_000m, Rate = 20m },
                new() { Order = 3, LowerBound = 400_000m, UpperBound = 1_500_000m, Rate = 27m },
                new() { Order = 4, LowerBound = 1_500_000m, UpperBound = 5_300_000m, Rate = 35m },
                new() { Order = 5, LowerBound = 5_300_000m, UpperBound = null, Rate = 40m }
            ]
        };
        db.CompanyPayrollSettings.Add(settings);

        await db.SaveChangesAsync();

        hrDb.SalaryDefinitions.Add(new HrSalaryDefinition
        {
            CompanyId = company.Id,
            PersonnelId = personnel.Id,
            EffectiveStartDate = start,
            GrossSalary = 40_000m,
            NetSalary = 33_058.43m,
            DailyRate = 1_333.33m,
            HourlyRate = 166.67m
        });
        await hrDb.SaveChangesAsync();

        return personnel.Id;
    }

    // ---------- Uç seviyesinde ----------

    [Theory]
    [MemberData(nameof(RestrictedRoles))]
    public async Task RestrictedRoles_CannotListExtraPayments(string roleName)
    {
        var client = await CreateClientForRoleAsync(roleName);

        var response = await client.GetAsync("/api/personnel-extra-payments");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(RestrictedRoles))]
    public async Task RestrictedRoles_CannotCreateExtraPayment(string roleName)
    {
        var client = await CreateClientForRoleAsync(roleName);

        var response = await client.PostAsJsonAsync(
            "/api/personnel-extra-payments",
            new
            {
                personnelId = Guid.NewGuid(),
                monthlyAmount = 5_000m,
                effectiveStartDate = "2026-01-01"
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// İzin anahtarı bu rollerin izin listesinde HİÇ bulunmamalı —
    /// katalog seviyesinde de doğrulanır.
    /// </summary>
    [Theory]
    [MemberData(nameof(RestrictedRoles))]
    public void RestrictedRoles_DoNotHaveExtraPaymentPermissionInCatalog(string roleName)
    {
        var role = RoleCatalog.Roles.Single(x => x.Name == roleName);

        Assert.DoesNotContain(role.PermissionKeys, key =>
            key.StartsWith("extra_payment.", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(AuthorizedRoles))]
    public async Task AuthorizedRoles_CanListExtraPayments(string roleName)
    {
        var client = await CreateClientForRoleAsync(roleName);

        var response = await client.GetAsync("/api/personnel-extra-payments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------- Projeksiyon seviyesinde ----------

    /// <summary>
    /// Asıl sızma yüzeyi: tazminat simülasyonu. Ücret iznini haiz ama
    /// ek ödeme izni OLMAYAN rol (İK Sorumlusu) resmi tutarları görür,
    /// gerçek ve fark alanlarını null görür.
    /// </summary>
    [Fact]
    public async Task PayrollRoleWithoutExtraPaymentPermission_SeesOnlyOfficialAmounts()
    {
        var personnelId = await CreatePersonnelWithExtraPaymentAsync();
        var client = await CreateClientForRoleAsync("İK Sorumlusu");

        var response = await client.GetAsync(
            $"/api/personnel-terminations/simulation?personnelId={personnelId}" +
            "&reason=0&terminationDate=2026-06-30");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Resmi tutarlar görünür.
        Assert.True(payload.GetProperty("officialNetTotal").GetDecimal() > 0m);

        // Elden bilgisi HİÇ dönmemeli.
        Assert.Equal(JsonValueKind.Null,
            payload.GetProperty("extraMonthlyAmount").ValueKind);
        Assert.Equal(JsonValueKind.Null,
            payload.GetProperty("actualNetTotal").ValueKind);
        Assert.Equal(JsonValueKind.Null,
            payload.GetProperty("extraPaymentDifference").ValueKind);
    }

    /// <summary>
    /// Yetkili rol gerçek tutarları ve farkı görür; gerçek tutar
    /// resmiden büyük olmalı (elden kısım eklendiği için).
    /// </summary>
    [Fact]
    public async Task AuthorizedRole_SeesActualAmountAndDifference()
    {
        var personnelId = await CreatePersonnelWithExtraPaymentAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        var response = await client.GetAsync(
            $"/api/personnel-terminations/simulation?personnelId={personnelId}" +
            "&reason=0&terminationDate=2026-06-30");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(15_000m, payload.GetProperty("extraMonthlyAmount").GetDecimal());

        var official = payload.GetProperty("officialNetTotal").GetDecimal();
        var actual = payload.GetProperty("actualNetTotal").GetDecimal();
        var difference = payload.GetProperty("extraPaymentDifference").GetDecimal();

        Assert.True(actual > official);
        Assert.Equal(actual - official, difference);
    }

    /// <summary>
    /// Ücret izni olmayan rol simülasyon ucuna hiç giremez — tazminat
    /// tutarı da ücret bilgisidir.
    /// </summary>
    [Theory]
    [InlineData("Şantiye Şefi")]
    [InlineData("Formen")]
    [InlineData("Teknik Ofis")]
    [InlineData("Sekreterya")]
    public async Task RolesWithoutSalaryPermission_CannotRunSimulation(string roleName)
    {
        var client = await CreateClientForRoleAsync(roleName);

        var response = await client.GetAsync(
            $"/api/personnel-terminations/simulation?personnelId={Guid.NewGuid()}" +
            "&reason=0");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
