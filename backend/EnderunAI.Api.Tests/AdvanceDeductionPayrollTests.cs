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
/// Avans kesintisinin bordroya yansıması — uçtan uca (H6).
///
/// Denetimde bulunan eksik buydu: taksit sayısı giriliyor, bordro
/// çalışıyor, kesinti SIFIR kalıyordu çünkü AdvanceDeduction alanına
/// kod hiçbir yerde değer yazmıyordu.
///
/// İki güvence:
/// - Yalnızca ÖDENMİŞ avans kesilir; onaylı ama ödenmemiş avans
///   bekler. Verilmemiş parayı geri almak olmaz.
/// - Bordro yeniden hesaplanınca kesinti İKİ KEZ düşmez.
/// </summary>
[Collection("Integration")]
public sealed class AdvanceDeductionPayrollTests(DatabaseFixture fixture)
{
    private const int Year = 2026;
    private const int Month = 6;

    private sealed record Context(Guid CompanyId, Guid PersonnelId);

    private async Task<HttpClient> ClientAsync() =>
        await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    private async Task<Context> CreateContextAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        db.CompanyPayrollSettings.Add(new CompanyPayrollSettings
        {
            CompanyId = company.Id,
            Year = Year,
            MinimumWageGross = 33_030m,
            MinimumWageNet = 28_075m,
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

        var personnel = await TestDataFactory.CreatePersonnelAsync(
            db, company.Id, suffix);

        await db.SaveChangesAsync();

        hrDb.SalaryDefinitions.Add(new HrSalaryDefinition
        {
            CompanyId = company.Id,
            PersonnelId = personnel.Id,
            GrossSalary = 80_000m,
            NetSalary = 60_000m,
            CurrencyCode = "TRY",
            EffectiveStartDate = new DateTime(Year, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        await hrDb.SaveChangesAsync();

        return new Context(company.Id, personnel.Id);
    }

    /// <summary>Avans açar; istenirse onaylayıp ödenmiş işaretler.</summary>
    private async Task<Guid> CreateAdvanceAsync(
        HttpClient client,
        Context context,
        decimal amount,
        int installments,
        bool markPaid)
    {
        var created = await client.PostAsJsonAsync("/api/hr/workforce/advances", new
        {
            companyId = context.CompanyId,
            personnelId = context.PersonnelId,
            requestDate = new DateTime(Year, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            requestedAmount = amount,
            currencyCode = "TRY",
            deductionInstallmentCount = installments,
            firstDeductionDate = new DateTime(Year, Month, 1, 0, 0, 0, DateTimeKind.Utc),
            reason = "Test avansı"
        });

        created.EnsureSuccessStatusCode();

        var id = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        // Onaylanan tutarı talep edilenle eşitliyoruz.
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();
            var advance = await hrDb.AdvanceRequests.SingleAsync(x => x.Id == id);

            advance.ApprovedAmount = amount;
            await hrDb.SaveChangesAsync();
        }

        await client.PostAsJsonAsync($"/api/hr/workforce/advances/{id}/approve", new { });

        if (markPaid)
        {
            await client.PostAsJsonAsync(
                $"/api/hr/workforce/advances/{id}/paid",
                new { paymentReference = "TEST-1" });
        }

        return id;
    }

    private static Task<HttpResponseMessage> CalculateAsync(
        HttpClient client, Context context, bool recalculate = false) =>
        client.PostAsJsonAsync("/api/hr/payroll/records/calculate-company", new
        {
            companyId = context.CompanyId,
            year = Year,
            month = Month,
            recalculateExisting = recalculate
        });

    private async Task<HrPayrollRecord> PayrollAsync(Context context)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        return await hrDb.PayrollRecords.AsNoTracking().SingleAsync(
            x => x.CompanyId == context.CompanyId &&
                 x.Year == Year && x.Month == Month);
    }

    // ---------- Kesinti ----------

    /// <summary>
    /// 12.000 TL avans, 3 taksit → ilk ay 4.000 TL kesilir ve net o
    /// kadar azalır.
    /// </summary>
    [Fact]
    public async Task PaidAdvance_IsDeductedFromPayroll()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await CreateAdvanceAsync(client, context, 12_000m, 3, markPaid: true);

        var response = await CalculateAsync(client, context);
        response.EnsureSuccessStatusCode();

        var record = await PayrollAsync(context);

        Assert.Equal(4_000m, record.AdvanceDeduction);
    }

    /// <summary>
    /// Onaylı ama ÖDENMEMİŞ avans kesilmez: verilmemiş parayı geri
    /// almak olmaz.
    /// </summary>
    [Fact]
    public async Task ApprovedButUnpaidAdvance_IsNotDeducted()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await CreateAdvanceAsync(client, context, 12_000m, 3, markPaid: false);

        await CalculateAsync(client, context);

        var record = await PayrollAsync(context);

        Assert.Equal(0m, record.AdvanceDeduction);
    }

    /// <summary>Kesinti net ödenecek tutardan düşüyor.</summary>
    [Fact]
    public async Task Deduction_ReducesTheNetPayable()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await CalculateAsync(client, context);
        var before = await PayrollAsync(context);

        await CreateAdvanceAsync(client, context, 12_000m, 3, markPaid: true);
        await CalculateAsync(client, context, recalculate: true);

        var after = await PayrollAsync(context);

        Assert.Equal(4_000m, after.AdvanceDeduction);
        Assert.Equal(
            before.OfficialNetPayableAmount - 4_000m,
            after.OfficialNetPayableAmount);
    }

    /// <summary>
    /// Bordro yeniden hesaplanınca kesinti İKİ KEZ düşmez: dönemin
    /// defteri baştan yazılıyor.
    /// </summary>
    [Fact]
    public async Task Recalculation_DoesNotDoubleDeduct()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await CreateAdvanceAsync(client, context, 12_000m, 3, markPaid: true);

        await CalculateAsync(client, context);
        await CalculateAsync(client, context, recalculate: true);
        await CalculateAsync(client, context, recalculate: true);

        var record = await PayrollAsync(context);

        Assert.Equal(4_000m, record.AdvanceDeduction);

        using var scope = fixture.Factory.Services.CreateScope();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        // Bir avans, bir dönem, tek satır.
        Assert.Equal(1, await hrDb.AdvanceDeductions.CountAsync(
            x => x.CompanyId == context.CompanyId &&
                 x.Year == Year && x.Month == Month));
    }

    /// <summary>Kesinti defterine avans başına satır yazılıyor.</summary>
    [Fact]
    public async Task Deduction_IsRecordedInTheLedger()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var advanceId = await CreateAdvanceAsync(
            client, context, 9_000m, 3, markPaid: true);

        await CalculateAsync(client, context);

        using var scope = fixture.Factory.Services.CreateScope();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        var line = await hrDb.AdvanceDeductions.AsNoTracking().SingleAsync(
            x => x.AdvanceRequestId == advanceId);

        Assert.Equal(3_000m, line.Amount);
        Assert.Equal(3_000m, line.ScheduledAmount);
        Assert.Equal(context.PersonnelId, line.PersonnelId);
    }

    /// <summary>
    /// Avansı olmayan personelin bordrosu hiç değişmiyor — mevcut
    /// davranış korunuyor.
    /// </summary>
    [Fact]
    public async Task PersonnelWithoutAdvance_IsUnaffected()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await CalculateAsync(client, context);

        var record = await PayrollAsync(context);

        Assert.Equal(0m, record.AdvanceDeduction);
        Assert.True(record.OfficialNetPayableAmount > 0m);
    }
}
