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
/// Maaş kartında resmi net + elden ödeme + toplam ele geçen birlikte.
///
/// Görünürlük genişledi ama muhasebe izolasyonu duruyor: elden ödeme
/// resmi bordroyu değiştirmemeli.
/// </summary>
[Collection("Integration")]
public sealed class SalaryCardExtraPaymentTests(DatabaseFixture fixture)
{
    private const int Year = 2026;

    private sealed record Context(Guid CompanyId, Guid PersonnelId);

    private async Task<Context> CreateContextAsync(
        string suffix, decimal extraMonthly = 15_000m)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        db.CompanyPayrollSettings.Add(new CompanyPayrollSettings
        {
            CompanyId = company.Id,
            Year = Year,
            MinimumWageGross = 33_030m,
            MinimumWageNet = 28_075.50m,
            SgkBaseFloor = 33_030m,
            SgkBaseCeiling = 297_270m,
            StampTaxPerMille = 7.59m,
            DailyWorkHours = 7.5m,
            TaxBrackets = new List<PayrollTaxBracket>
            {
                new() { Order = 1, LowerBound = 0m, UpperBound = 190_000m, Rate = 15m },
                new() { Order = 2, LowerBound = 190_000m, UpperBound = null, Rate = 20m }
            }
        });

        var personnel = await TestDataFactory.CreatePersonnelAsync(db, company.Id, suffix);

        db.PersonnelExtraPayments.Add(new PersonnelExtraPayment
        {
            CompanyId = company.Id,
            PersonnelId = personnel.Id,
            MonthlyAmount = extraMonthly,
            EffectiveStartDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();

        hrDb.SalaryDefinitions.Add(new HrSalaryDefinition
        {
            CompanyId = company.Id,
            PersonnelId = personnel.Id,
            EffectiveStartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            SalaryBasis = SalaryBasis.Net,
            TargetNetSalary = 45_000m,
            GrossSalary = 60_000m,
            CurrencyCode = "TRY"
        });
        await hrDb.SaveChangesAsync();

        return new Context(company.Id, personnel.Id);
    }

    private async Task<HttpClient> CreateClientForRoleAsync(string roleName)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();

        const string password = "SalaryCard!2026";
        var username = $"test-kart-{Guid.NewGuid():N}"[..40];
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

    private static async Task<JsonElement> GetCardAsync(
        HttpClient client, Guid companyId, Guid personnelId)
    {
        var list = await client.GetFromJsonAsync<JsonElement>(
            $"/api/hr/payroll/salary-definitions?companyId={companyId}" +
            $"&personnelId={personnelId}");

        return list.EnumerateArray().Single();
    }

    [Fact]
    public async Task HrManager_NowSeesExtraPaymentOnSalaryCard()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        // İK Sorumlusu önceden elden ödemeyi göremiyordu; artık görüyor.
        var client = await CreateClientForRoleAsync("İK Sorumlusu");

        var card = await GetCardAsync(client, context.CompanyId, context.PersonnelId);

        Assert.Equal(45_000m, card.GetProperty("officialNetSalary").GetDecimal());
        Assert.Equal(15_000m, card.GetProperty("extraPaymentMonthlyAmount").GetDecimal());
        Assert.Equal(60_000m, card.GetProperty("totalTakeHome").GetDecimal());
        Assert.False(card.GetProperty("extraPaymentHidden").GetBoolean());
    }

    [Fact]
    public async Task GeneralManager_SeesTheSameCombinedView()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await CreateClientForRoleAsync("Genel Müdür");

        var card = await GetCardAsync(client, context.CompanyId, context.PersonnelId);

        Assert.Equal(60_000m, card.GetProperty("totalTakeHome").GetDecimal());
    }

    [Fact]
    public async Task FieldRole_CannotSeeSalaryCardAtAll()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        // Maaşı görmeyen ek ödemeyi de göremez — uçta durur.
        var client = await CreateClientForRoleAsync("Teknik Koordinatör");

        var response = await client.GetAsync(
            $"/api/hr/payroll/salary-definitions?companyId={context.CompanyId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task WithoutExtraPaymentPermission_CardHidesExtraButKeepsOfficialNet()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        // Kullanıcı bazında ek ödeme izni kapatılmış bir İK Sorumlusu.
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();

            const string password = "SalaryCard!2026";
            var username = $"test-kisit-{Guid.NewGuid():N}"[..40];
            var hash = passwordService.Hash(password);

            var user = new AppUser
            {
                Username = username,
                FullName = "Kısıtlı İK",
                PasswordHash = hash.Hash,
                PasswordSalt = hash.Salt,
                IsActive = true,
                WorkHoursExempt = true
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();

            var role = await db.Roles.SingleAsync(x => x.Name == "İK Sorumlusu");
            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
            db.UserDataScopes.Add(new UserDataScope
            {
                UserId = user.Id,
                ScopeType = DataScopeType.All
            });

            var permission = await db.Permissions.SingleAsync(
                x => x.Key == PermissionCatalog.Keys.ExtraPaymentView);

            db.UserPermissionOverrides.Add(new UserPermissionOverride
            {
                UserId = user.Id,
                PermissionId = permission.Id,
                Effect = PermissionOverrideEffect.Deny
            });
            await db.SaveChangesAsync();

            var client = fixture.Factory.CreateClient();
            var token = await AuthHelper.LoginAsync(client, username, password);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            var card = await GetCardAsync(client, context.CompanyId, context.PersonnelId);

            // Resmi net görünür.
            Assert.Equal(45_000m, card.GetProperty("officialNetSalary").GetDecimal());

            // Elden tutar ve toplam projeksiyondan hiç çıkmaz.
            Assert.Equal(JsonValueKind.Null,
                card.GetProperty("extraPaymentMonthlyAmount").ValueKind);
            Assert.Equal(JsonValueKind.Null,
                card.GetProperty("totalTakeHome").ValueKind);
            Assert.True(card.GetProperty("extraPaymentHidden").GetBoolean());
        }
    }

    [Fact]
    public async Task ExtraPayment_DoesNotChangeOfficialPayroll()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, extraMonthly: 25_000m);
        var client = await CreateClientForRoleAsync("İK Sorumlusu");

        var response = await client.PostAsJsonAsync(
            "/api/hr/payroll/records/calculate-company",
            new { companyId = context.CompanyId, year = Year, month = 1, recalculateExisting = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        var record = await hrDb.PayrollRecords.AsNoTracking()
            .SingleAsync(x => x.CompanyId == context.CompanyId &&
                              x.PersonnelId == context.PersonnelId &&
                              x.Year == Year && x.Month == 1);

        // MUHASEBE İZOLASYONU: elden ödeme resmi bordroya girmez.
        // Kart net esaslı ve hedef 45.000; elden 25.000 buna eklenmez.
        Assert.Equal(45_000m, record.OfficialNetPayableAmount);
        Assert.DoesNotContain(25_000m, new[]
        {
            record.GrossSalary, record.TotalEarnings, record.OfficialNetPayableAmount
        });
    }
}
