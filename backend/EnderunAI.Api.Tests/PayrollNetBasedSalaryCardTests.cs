using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
/// Net esaslı ücret kartı: kartta girilen net her ay birebir çıkmalı,
/// brüt esaslı kartların davranışı ise hiç değişmemeli.
/// </summary>
[Collection("Integration")]
public sealed class PayrollNetBasedSalaryCardTests(DatabaseFixture fixture)
{
    private const int Year = 2026;

    private sealed record Context(Guid CompanyId, Guid NetBasedId, Guid GrossBasedId);

    /// <summary>
    /// Biri net esaslı biri brüt esaslı iki personel kurar. Brüt esaslı
    /// olan geriye uyum tanığı: onun bordrosu değişmemeli.
    /// </summary>
    private async Task<Context> CreateContextAsync(
        string suffix, decimal targetNet = 45_000m)
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
                new() { Order = 2, LowerBound = 190_000m, UpperBound = 400_000m, Rate = 20m },
                new() { Order = 3, LowerBound = 400_000m, UpperBound = null, Rate = 27m }
            }
        });

        var netBased = NewPersonnel(company.Id, suffix, "Netli");
        var grossBased = NewPersonnel(company.Id, suffix, "Brutlu");
        db.Personnel.AddRange(netBased, grossBased);
        await db.SaveChangesAsync();

        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        hrDb.SalaryDefinitions.AddRange(
            new HrSalaryDefinition
            {
                CompanyId = company.Id,
                PersonnelId = netBased.Id,
                EffectiveStartDate = start,
                SalaryBasis = SalaryBasis.Net,
                TargetNetSalary = targetNet,
                CurrencyCode = "TRY"
            },
            new HrSalaryDefinition
            {
                CompanyId = company.Id,
                PersonnelId = grossBased.Id,
                EffectiveStartDate = start,
                SalaryBasis = SalaryBasis.Gross,
                GrossSalary = 33_030m,
                NetSalary = 28_075.50m,
                CurrencyCode = "TRY"
            });

        await hrDb.SaveChangesAsync();

        return new Context(company.Id, netBased.Id, grossBased.Id);
    }

    private static Personnel NewPersonnel(Guid companyId, string suffix, string name) =>
        new()
        {
            CompanyId = companyId,
            EmployeeNumber = $"{name}-{suffix}"[..Math.Min(20, $"{name}-{suffix}".Length)],
            FirstName = name,
            LastName = "Test",
            Status = PersonnelStatus.Active
        };

    private async Task CalculateAsync(HttpClient client, Guid companyId, int month)
    {
        var response = await client.PostAsJsonAsync(
            "/api/hr/payroll/records/calculate-company",
            new { companyId, year = Year, month, recalculateExisting = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HrPayrollRecord> LoadRecordAsync(
        Guid companyId, Guid personnelId, int month)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        return await hrDb.PayrollRecords
            .AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId &&
                              x.PersonnelId == personnelId &&
                              x.Year == Year && x.Month == month);
    }

    [Fact]
    public async Task NetBasedCard_ProducesExactlyTheAgreedNet()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, targetNet: 45_000m);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await CalculateAsync(client, context.CompanyId, month: 1);

        var record = await LoadRecordAsync(context.CompanyId, context.NetBasedId, 1);

        Assert.Equal(45_000m, record.OfficialNetPayableAmount);
        // Brüt sistem tarafından bulundu; netten büyük olmalı.
        Assert.True(record.GrossSalary > 45_000m);
    }

    [Fact]
    public async Task NetBasedCard_StaysExactAsTaxBracketRises()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, targetNet: 60_000m);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        // Yıl boyunca kümülatif matrah büyür ve üst dilime geçilir.
        foreach (var month in Enumerable.Range(1, 12))
            await CalculateAsync(client, context.CompanyId, month);

        var january = await LoadRecordAsync(context.CompanyId, context.NetBasedId, 1);
        var december = await LoadRecordAsync(context.CompanyId, context.NetBasedId, 12);

        // Asıl vaat: her ay tam olarak anlaşılan net.
        Assert.Equal(60_000m, january.OfficialNetPayableAmount);
        Assert.Equal(60_000m, december.OfficialNetPayableAmount);

        // Artan vergiyi şirket üstlenir: aralık brütü ocaktan yüksek.
        Assert.True(december.GrossSalary > january.GrossSalary,
            $"Aralık brütü {december.GrossSalary:N2}, ocak {january.GrossSalary:N2}");
    }

    [Fact]
    public async Task GrossBasedCard_BehaviourIsUnchanged()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await CalculateAsync(client, context.CompanyId, month: 1);

        var record = await LoadRecordAsync(context.CompanyId, context.GrossBasedId, 1);

        // Asgari ücretli brüt esaslı kart: mevcut testteki rakamların aynısı.
        Assert.Equal(33_030m, record.GrossSalary);
        Assert.Equal(4_624.20m, record.SgkEmployeeDeduction);
        Assert.Equal(330.30m, record.UnemploymentEmployeeDeduction);
        Assert.Equal(0m, record.IncomeTaxDeduction);
        Assert.Equal(28_075.50m, record.OfficialNetPayableAmount);
    }

    [Fact]
    public async Task NetBasedCard_WithoutTargetNet_IsSkipped()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

            var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

            db.CompanyPayrollSettings.Add(new CompanyPayrollSettings
            {
                CompanyId = company.Id,
                Year = Year,
                MinimumWageGross = 33_030m,
                SgkBaseFloor = 33_030m,
                SgkBaseCeiling = 297_270m,
                TaxBrackets = new List<PayrollTaxBracket>
                {
                    new() { Order = 1, LowerBound = 0m, UpperBound = null, Rate = 15m }
                }
            });

            var person = NewPersonnel(company.Id, suffix, "Eksik");
            db.Personnel.Add(person);
            await db.SaveChangesAsync();

            // Net esaslı ama hedef net boş: tutar uydurulmamalı.
            hrDb.SalaryDefinitions.Add(new HrSalaryDefinition
            {
                CompanyId = company.Id,
                PersonnelId = person.Id,
                EffectiveStartDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                SalaryBasis = SalaryBasis.Net,
                TargetNetSalary = 0m,
                GrossSalary = 50_000m,
                CurrencyCode = "TRY"
            });
            await hrDb.SaveChangesAsync();

            var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

            var response = await client.PostAsJsonAsync(
                "/api/hr/payroll/records/calculate-company",
                new { companyId = company.Id, year = Year, month = 1, recalculateExisting = true });

            var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(0, payload.GetProperty("createdCount").GetInt32());
            Assert.Equal(1, payload.GetProperty("missingSalaryDefinitionCount").GetInt32());
        }
    }

    [Fact]
    public async Task NetToGrossEndpoint_ReturnsBreakdown()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync("/api/hr/payroll/net-to-gross",
            new { companyId = context.CompanyId, year = Year, targetNet = 45_000m, month = 1 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(payload.GetProperty("isExact").GetBoolean());
        Assert.Equal(45_000m, payload.GetProperty("achievedNet").GetDecimal());
        Assert.True(payload.GetProperty("grossSalary").GetDecimal() > 45_000m);
        // Kesinti kırılımı ekranda gösterilebilsin diye birlikte dönüyor.
        Assert.True(payload.GetProperty("sgkEmployee").GetDecimal() > 0m);
        Assert.True(payload.GetProperty("totalEmployerCost").GetDecimal() > 0m);
    }

    [Fact]
    public async Task NetToGrossEndpoint_WithoutSettings_ExplainsInsteadOfGuessing()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        // Parametre yoksa yaklaşık bir tutar üretilmez.
        var response = await client.PostAsJsonAsync("/api/hr/payroll/net-to-gross",
            new { companyId = company.Id, year = 2099, targetNet = 45_000m, month = 1 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SavingNetCard_FillsReferenceGrossFromJanuary()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var person = NewPersonnel(context.CompanyId, $"{suffix}b", "Yeni");

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Personnel.Add(person);
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync(
            "/api/hr/payroll/salary-definitions",
            new
            {
                companyId = context.CompanyId,
                personnelId = person.Id,
                effectiveStartDate = "2026-01-01",
                effectiveEndDate = (string?)null,
                grossSalary = 0m,
                netSalary = 0m,
                dailyRate = 0m,
                hourlyRate = 0m,
                overtimeMultiplier = 1.5m,
                sundayMultiplier = 2m,
                publicHolidayMultiplier = 2m,
                currencyCode = "TRY",
                description = (string?)null,
                salaryBasis = 1,
                targetNetSalary = 45_000m
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(1, payload.GetProperty("salaryBasis").GetInt32());
        Assert.Equal("Net esaslı", payload.GetProperty("salaryBasisName").GetString());
        Assert.Equal(45_000m, payload.GetProperty("targetNetSalary").GetDecimal());
        // Brüt sistemce dolduruldu — kullanıcı girmedi.
        Assert.True(payload.GetProperty("grossSalary").GetDecimal() > 45_000m);
    }

    [Fact]
    public async Task NetCard_WithoutTargetNet_IsRejectedOnSave()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var person = NewPersonnel(context.CompanyId, $"{suffix}c", "Hatali");

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Personnel.Add(person);
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync(
            "/api/hr/payroll/salary-definitions",
            new
            {
                companyId = context.CompanyId,
                personnelId = person.Id,
                effectiveStartDate = "2026-01-01",
                effectiveEndDate = (string?)null,
                grossSalary = 50_000m,
                netSalary = 0m,
                dailyRate = 0m,
                hourlyRate = 0m,
                overtimeMultiplier = 1.5m,
                sundayMultiplier = 2m,
                publicHolidayMultiplier = 2m,
                currencyCode = "TRY",
                description = (string?)null,
                salaryBasis = 1,
                targetNetSalary = 0m
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
