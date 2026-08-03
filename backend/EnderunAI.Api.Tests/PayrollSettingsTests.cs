using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.HumanResources;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Faz E1: bordro parametreleri. Parametreler doğrulanmadan bordronun
/// kesinleştirilememesi (fail-closed) bu paketin en kritik kuralı —
/// doğrulanmamış varsayılanla üretilen resmi bordro, eksik prim ve
/// vergi beyanı demek.
/// </summary>
[Collection("Integration")]
public sealed class PayrollSettingsTests(DatabaseFixture fixture)
{
    private const int Year = 2026;

    private async Task<Guid> CreateCompanyWithSettingsAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        var settings = new CompanyPayrollSettings
        {
            CompanyId = company.Id,
            Year = Year,
            MinimumWageGross = 33_030m,
            MinimumWageNet = 28_075.50m,
            SgkBaseFloor = 33_030m,
            SgkBaseCeiling = 247_725m,
            TaxBrackets = new List<PayrollTaxBracket>
            {
                new() { Order = 1, LowerBound = 0m, UpperBound = 200_000m, Rate = 15m },
                new() { Order = 2, LowerBound = 200_000m, UpperBound = null, Rate = 20m }
            }
        };

        db.CompanyPayrollSettings.Add(settings);
        await db.SaveChangesAsync();

        return company.Id;
    }

    private static object BuildUpdatePayload(
        IReadOnlyCollection<object>? brackets = null,
        decimal minimumWageGross = 33_030m,
        decimal sgkBaseFloor = 33_030m,
        decimal sgkBaseCeiling = 247_725m) => new
        {
            minimumWageGross,
            minimumWageNet = 28_075.50m,
            sgkBaseFloor,
            sgkBaseCeiling,
            sgkEmployeeRate = 14m,
            unemploymentEmployeeRate = 1m,
            sgkEmployerRate = 20.75m,
            unemploymentEmployerRate = 2m,
            sgkEmployerDiscountEnabled = false,
            sgkEmployerDiscountPoints = 5m,
            stampTaxPerMille = 7.59m,
            minimumWageIncomeTaxExemptionEnabled = true,
            minimumWageStampTaxExemptionEnabled = true,
            taxBrackets = brackets ?? new object[]
            {
                new { order = 1, lowerBound = 0m, upperBound = (decimal?)200_000m, rate = 15m },
                new { order = 2, lowerBound = 200_000m, upperBound = (decimal?)null, rate = 20m }
            }
        };

    [Fact]
    public async Task Get_ReturnsSeededSettingsAsUnverified()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyWithSettingsAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.GetAsync(
            $"/api/payroll-settings?companyId={companyId}&year={Year}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(payload.GetProperty("isVerified").GetBoolean());
        Assert.Equal(33_030m, payload.GetProperty("minimumWageGross").GetDecimal());
        Assert.Equal(2, payload.GetProperty("taxBrackets").GetArrayLength());
    }

    [Fact]
    public async Task ApprovePayroll_IsRejectedWhileSettingsAreUnverified()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyWithSettingsAsync(suffix);
        var payrollId = await CreateCalculatedPayrollAsync(companyId);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsync(
            $"/api/hr/payroll/records/{payrollId}/approve", null);

        // HR modülü iş kuralı ihlallerini 400 ile döndürüyor (modül geneli
        // ExecuteAsync sarmalayıcısının yerleşik davranışı).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("doğrulanmadı", payload.GetProperty("message").GetString()!);
    }

    [Fact]
    public async Task ApprovePayroll_SucceedsAfterSettingsAreVerified()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyWithSettingsAsync(suffix);
        var payrollId = await CreateCalculatedPayrollAsync(companyId);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var verify = await client.PostAsJsonAsync(
            $"/api/payroll-settings/verify?companyId={companyId}&year={Year}",
            new { verificationNote = "2026 SGK tebliği ile karşılaştırıldı" });

        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        Assert.True((await verify.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("isVerified").GetBoolean());

        var approve = await client.PostAsync(
            $"/api/hr/payroll/records/{payrollId}/approve", null);

        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
    }

    [Fact]
    public async Task ApprovePayroll_IsRejectedWhenNoSettingsExistForYear()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        var payrollId = await CreateCalculatedPayrollAsync(company.Id);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsync(
            $"/api/hr/payroll/records/{payrollId}/approve", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "tanımlı değil",
            (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("message").GetString()!);
    }

    [Fact]
    public async Task Update_ResetsVerificationSoChangedValuesMustBeRechecked()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyWithSettingsAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await client.PostAsJsonAsync(
            $"/api/payroll-settings/verify?companyId={companyId}&year={Year}",
            new { verificationNote = "ilk doğrulama" });

        var update = await client.PutAsJsonAsync(
            $"/api/payroll-settings?companyId={companyId}&year={Year}",
            BuildUpdatePayload(minimumWageGross: 35_000m));

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var payload = await update.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(35_000m, payload.GetProperty("minimumWageGross").GetDecimal());
        Assert.False(payload.GetProperty("isVerified").GetBoolean());
    }

    [Theory]
    // İlk dilim 0'dan başlamalı.
    [InlineData(50_000, 200_000, 200_000, null, "alt sınırı 0")]
    // İki dilim arasında boşluk bırakılamaz.
    [InlineData(0, 200_000, 250_000, null, "boşluk veya çakışma")]
    // Dilimler çakışamaz.
    [InlineData(0, 200_000, 150_000, null, "boşluk veya çakışma")]
    public async Task Update_RejectsInconsistentTaxBrackets(
        decimal firstLower,
        decimal firstUpper,
        decimal secondLower,
        decimal? secondUpper,
        string expectedMessagePart)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyWithSettingsAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PutAsJsonAsync(
            $"/api/payroll-settings?companyId={companyId}&year={Year}",
            BuildUpdatePayload(new object[]
            {
                new { order = 1, lowerBound = firstLower, upperBound = (decimal?)firstUpper, rate = 15m },
                new { order = 2, lowerBound = secondLower, upperBound = secondUpper, rate = 20m }
            }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            expectedMessagePart,
            (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("message").GetString()!);
    }

    [Fact]
    public async Task Update_RejectsCeilingBelowFloor()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var companyId = await CreateCompanyWithSettingsAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PutAsJsonAsync(
            $"/api/payroll-settings?companyId={companyId}&year={Year}",
            BuildUpdatePayload(sgkBaseFloor: 33_030m, sgkBaseCeiling: 10_000m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "SGK tavanı",
            (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("message").GetString()!);
    }

    private async Task<Guid> CreateCalculatedPayrollAsync(Guid companyId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hrDb = scope.ServiceProvider
            .GetRequiredService<EnderunAI.Api.Data.HumanResources.HrDbContext>();

        var personnel = new Personnel
        {
            CompanyId = companyId,
            EmployeeNumber = $"PRS-{Guid.NewGuid():N}"[..12],
            FirstName = "Test",
            LastName = "Personel",
            MonthlySalary = 40_000m,
            Status = PersonnelStatus.Active
        };
        appDb.Personnel.Add(personnel);
        await appDb.SaveChangesAsync();

        var payroll = new HrPayrollRecord
        {
            CompanyId = companyId,
            PersonnelId = personnel.Id,
            Year = Year,
            Month = 1,
            GrossSalary = 40_000m,
            TotalEarnings = 40_000m,
            Status = PayrollStatus.Calculated
        };
        hrDb.PayrollRecords.Add(payroll);
        await hrDb.SaveChangesAsync();

        return payroll.Id;
    }
}
