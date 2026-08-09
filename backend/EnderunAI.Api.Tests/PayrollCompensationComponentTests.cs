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
/// Ek ücret kalemlerinin bordroya uçtan uca yansıması.
///
/// Bu zincir daha önce KOPUKTU: HrCompensationComponent yazılabiliyor
/// ama hiçbir yerde okunmuyordu; HrPayrollRecord'un kazanç alanları
/// (prim, yemek, yol, tazminat) okunuyor ama hiç yazılmıyordu. Prim ve
/// yemek ödeyen bir şirkette bordro eksik çıkıyor, ekranda "Yemek: 0
/// TL" yazıyordu.
/// </summary>
[Collection("Integration")]
public sealed class PayrollCompensationComponentTests(DatabaseFixture fixture)
{
    private const int Year = 2026;
    private const int Month = 6;
    private const decimal Gross = 60_000m;

    // Puantaj kurulmadığı için ödenen gün tam dönemdir: günlük
    // kalemlerin ve istisna tavanının çarpanı 30.
    private const decimal PeriodDays = 30m;

    private sealed record Context(Guid CompanyId, Guid PersonnelId);

    private async Task<Context> CreateContextAsync(
        string suffix,
        decimal? mealSgkCap = 150m,
        decimal? mealIncomeTaxCap = 200m)
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
            MealSgkExemptionDailyCap = mealSgkCap,
            MealIncomeTaxExemptionDailyCap = mealIncomeTaxCap,
            TaxBrackets = new List<PayrollTaxBracket>
            {
                new() { Order = 1, LowerBound = 0m, UpperBound = 190_000m, Rate = 15m },
                new() { Order = 2, LowerBound = 190_000m, UpperBound = 400_000m, Rate = 20m },
                new() { Order = 3, LowerBound = 400_000m, UpperBound = null, Rate = 27m }
            }
        });

        var personnel = new Personnel
        {
            CompanyId = company.Id,
            EmployeeNumber = $"KLM-{suffix}",
            FirstName = "Kalemli",
            LastName = "Test",
            Status = PersonnelStatus.Active
        };

        db.Personnel.Add(personnel);
        await db.SaveChangesAsync();

        hrDb.SalaryDefinitions.Add(new HrSalaryDefinition
        {
            CompanyId = company.Id,
            PersonnelId = personnel.Id,
            EffectiveStartDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            SalaryBasis = SalaryBasis.Gross,
            GrossSalary = Gross,
            CurrencyCode = "TRY"
        });

        await hrDb.SaveChangesAsync();

        return new Context(company.Id, personnel.Id);
    }

    private async Task AddComponentAsync(
        Guid companyId,
        Guid personnelId,
        int componentType,
        decimal amount,
        int calculationType = 1,       // Günlük
        int paymentMethod = 0,         // Bordro ile
        bool inKind = false,
        bool includeInPayroll = true,
        bool sgkBase = false,
        bool incomeTaxBase = false,
        bool stampTaxBase = true,
        string name = "Kalem")
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.HrCompensationComponents.Add(new HrCompensationComponent
        {
            CompanyId = companyId,
            PersonnelId = personnelId,
            Code = name,
            Name = name,
            ComponentType = componentType,
            CalculationType = calculationType,
            PaymentMethod = paymentMethod,
            Amount = amount,
            CurrencyCode = "TRY",
            EffectiveStartDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            IsAttendanceBased = false,
            IsInKindBenefit = inKind,
            IncludeInPayroll = includeInPayroll,
            IncludeInSgkBase = sgkBase,
            IncludeInIncomeTaxBase = incomeTaxBase,
            IncludeInStampTaxBase = stampTaxBase,
            IsActive = true
        });

        await db.SaveChangesAsync();
    }

    private async Task<HttpResponseMessage> CalculateAsync(Guid companyId)
    {
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        return await client.PostAsJsonAsync(
            "/api/hr/payroll/records/calculate-company",
            new { companyId, year = Year, month = Month, recalculateExisting = true });
    }

    private async Task<HrPayrollRecord> LoadRecordAsync(Context context)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        return await hrDb.PayrollRecords.AsNoTracking()
            .SingleAsync(x => x.CompanyId == context.CompanyId &&
                              x.PersonnelId == context.PersonnelId &&
                              x.Year == Year && x.Month == Month);
    }

    /// <summary>
    /// Kalemler bordro kaydına türlerine göre yazılır ve brüte eklenir.
    /// Eskiden altısı da sıfır kalıyordu.
    /// </summary>
    [Fact]
    public async Task Components_LandOnPayrollRecordFields()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        await AddComponentAsync(context.CompanyId, context.PersonnelId,
            componentType: 0, amount: 5_000m, calculationType: 0, name: "Prim");
        await AddComponentAsync(context.CompanyId, context.PersonnelId,
            componentType: 3, amount: 100m, name: "Yemek");
        await AddComponentAsync(context.CompanyId, context.PersonnelId,
            componentType: 2, amount: 50m, sgkBase: true, incomeTaxBase: true,
            name: "Yol");
        await AddComponentAsync(context.CompanyId, context.PersonnelId,
            componentType: 7, amount: 800m, calculationType: 0, name: "İcra");

        Assert.Equal(HttpStatusCode.OK,
            (await CalculateAsync(context.CompanyId)).StatusCode);

        var record = await LoadRecordAsync(context);

        Assert.Equal(5_000m, record.BonusAmount);
        Assert.Equal(100m * PeriodDays, record.MealAmount);
        Assert.Equal(50m * PeriodDays, record.TravelAmount);
        Assert.Equal(800m, record.OtherDeductionAmount);

        // Brüt maaş + kalemler
        Assert.Equal(
            record.NormalWorkAmount + 5_000m + 3_000m + 1_500m,
            record.TotalEarnings);
    }

    /// <summary>
    /// ASIL GÜVENCE: kalem tavanı aştığında tavana kadarı istisna,
    /// aşan kısım hem SGK hem gelir vergisi matrahına giriyor.
    ///
    /// Yemek 30 × 300 = 9.000.
    /// SGK tavanı 30 × 150 = 4.500 → prime esas kazanca 4.500 girer.
    /// GV tavanı 30 × 200 = 6.000 → vergi matrahına 3.000 girer.
    /// </summary>
    [Fact]
    public async Task MealOverCap_ExcessEntersBothBases()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        await AddComponentAsync(context.CompanyId, context.PersonnelId,
            componentType: 3, amount: 300m, name: "Yemek");

        Assert.Equal(HttpStatusCode.OK,
            (await CalculateAsync(context.CompanyId)).StatusCode);

        var record = await LoadRecordAsync(context);

        Assert.Equal(9_000m, record.MealAmount);

        // SGK matrahı: toplam kazanç − istisna (4.500)
        Assert.Equal(record.TotalEarnings - 4_500m, record.SgkBase);

        // Gelir vergisi matrahı: (toplam kazanç − 6.000) − işçi payı
        // primler. İstisna edilen kısım matraha hiç girmez.
        var employeeContributions =
            record.SgkEmployeeDeduction + record.UnemploymentEmployeeDeduction;

        Assert.Equal(
            record.TotalEarnings - 6_000m - employeeContributions,
            record.IncomeTaxBase);
    }

    /// <summary>Ayni yardımda tavan yok: tamamı her iki matrahtan da çıkar.</summary>
    [Fact]
    public async Task InKindMeal_IsFullyExempt()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        await AddComponentAsync(context.CompanyId, context.PersonnelId,
            componentType: 3, amount: 300m, inKind: true, name: "İşyeri Yemeği");

        Assert.Equal(HttpStatusCode.OK,
            (await CalculateAsync(context.CompanyId)).StatusCode);

        var record = await LoadRecordAsync(context);

        Assert.Equal(9_000m, record.MealAmount);
        Assert.Equal(record.TotalEarnings - 9_000m, record.SgkBase);
    }

    /// <summary>
    /// Tavan tanımlı değilse istisna uygulanmaz ve hesap sonucu bunu
    /// uyarı olarak bildirir — bordro sessizce eksik vergiyle çıkmaz.
    /// </summary>
    [Fact]
    public async Task MissingCap_ProducesWarningAndNoExemption()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(
            suffix, mealSgkCap: null, mealIncomeTaxCap: null);

        await AddComponentAsync(context.CompanyId, context.PersonnelId,
            componentType: 3, amount: 300m, name: "Yemek");

        var response = await CalculateAsync(context.CompanyId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        Assert.Contains("istisna tavanı", raw);

        var record = await LoadRecordAsync(context);

        // İstisna yok: kalemin tamamı prime esas kazançta.
        Assert.Equal(record.TotalEarnings, record.SgkBase);
    }

    /// <summary>
    /// Nakit ödeme resmî bordroya girmez ve bunu uyarı olarak söyler.
    /// Elden ödeme sistemin başka yerinde de resmî akıştan ayrı.
    /// </summary>
    [Fact]
    public async Task CashComponent_StaysOutOfPayroll()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        await AddComponentAsync(context.CompanyId, context.PersonnelId,
            componentType: 0, amount: 10_000m, calculationType: 0,
            paymentMethod: 1, name: "Elden Prim");

        var response = await CalculateAsync(context.CompanyId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        Assert.Contains("nakit ödeme", raw);

        var record = await LoadRecordAsync(context);

        Assert.Equal(0m, record.BonusAmount);
        Assert.Equal(record.NormalWorkAmount, record.TotalEarnings);
    }

    /// <summary>
    /// Kalemi olmayan personelin bordrosu değişmiyor: bağlama geriye
    /// dönük bir fark yaratmadı.
    /// </summary>
    [Fact]
    public async Task WithoutComponents_PayrollIsUnchanged()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        Assert.Equal(HttpStatusCode.OK,
            (await CalculateAsync(context.CompanyId)).StatusCode);

        var record = await LoadRecordAsync(context);

        Assert.Equal(0m, record.BonusAmount);
        Assert.Equal(0m, record.MealAmount);
        Assert.Equal(0m, record.TravelAmount);
        Assert.Equal(0m, record.OtherDeductionAmount);
        Assert.Equal(record.NormalWorkAmount, record.TotalEarnings);
    }

    /// <summary>
    /// Bordro ön kontrolü, tavanı tanımsız bırakılmış yemek/yol
    /// yardımını bordro ÜRETİLMEDEN önce bildirir.
    /// </summary>
    [Fact]
    public async Task Readiness_WarnsAboutUndefinedCaps()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(
            suffix, mealSgkCap: null, mealIncomeTaxCap: null);

        await AddComponentAsync(context.CompanyId, context.PersonnelId,
            componentType: 3, amount: 300m, name: "Yemek");

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.GetAsync(
            $"/api/hr/bordro-on-kontrol?companyId={context.CompanyId}" +
            $"&year={Year}&month={Month}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();

        Assert.Contains("yemek yardımının günlük SGK istisna tavanı", raw);
        Assert.Contains("gelir vergisi", raw);
    }
}
