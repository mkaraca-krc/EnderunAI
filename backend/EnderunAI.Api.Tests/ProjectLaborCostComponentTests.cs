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
/// Ek ücret kalemlerinin proje işçilik maliyetine uçtan uca yansıması.
///
/// Zincir kopuktu: HrProjectLaborCost'un MealCost, AccommodationCost,
/// ShuttleCost ve CompensationCost alanları toplama ve kâr hesabına
/// giriyor ama hiçbir yerde yazılmıyordu. Canlıda bugüne kadar
/// yazılmış tüm satırlarda dördü de sıfırdı: yemek, konaklama ve
/// servis maliyeti kâra hiç yansımadığı için kâr olduğundan yüksek
/// görünüyordu.
/// </summary>
[Collection("Integration")]
public sealed class ProjectLaborCostComponentTests(DatabaseFixture fixture)
{
    private const int Year = 2026;
    private const int Month = 7;
    private const decimal Gross = 60_000m;

    private sealed record Context(
        Guid CompanyId, Guid ProjectId, Guid PersonnelId);

    private async Task<Context> CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        db.CompanyPayrollSettings.Add(new CompanyPayrollSettings
        {
            CompanyId = project.CompanyId,
            Year = Year,
            MinimumWageGross = 33_030m,
            MinimumWageNet = 28_075.50m,
            SgkBaseFloor = 33_030m,
            SgkBaseCeiling = 247_725m,
            DailyWorkHours = 7.5m,
            TaxBrackets =
            [
                new() { Order = 1, LowerBound = 0m, UpperBound = 200_000m, Rate = 15m },
                new() { Order = 2, LowerBound = 200_000m, UpperBound = null, Rate = 20m }
            ]
        });

        var personnel = new Personnel
        {
            CompanyId = project.CompanyId,
            EmployeeNumber = $"MLY-{suffix}",
            FirstName = "Maliyet",
            LastName = "Test",
            Status = PersonnelStatus.Active
        };

        db.Personnel.Add(personnel);
        await db.SaveChangesAsync();

        hrDb.SalaryDefinitions.Add(new HrSalaryDefinition
        {
            CompanyId = project.CompanyId,
            PersonnelId = personnel.Id,
            EffectiveStartDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            GrossSalary = Gross,
            CurrencyCode = "TRY"
        });

        await hrDb.SaveChangesAsync();

        return new Context(project.CompanyId, project.Id, personnel.Id);
    }

    /// <summary>Projeye onaylı puantaj günleri yazar.</summary>
    private async Task AddWorkDaysAsync(Context context, int dayCount)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        for (var day = 1; day <= dayCount; day++)
        {
            db.AttendanceRecords.Add(new AttendanceRecord
            {
                CompanyId = context.CompanyId,
                PersonnelId = context.PersonnelId,
                ProjectId = context.ProjectId,
                WorkDate = new DateTime(Year, Month, day, 0, 0, 0, DateTimeKind.Utc),
                Status = (int)AttendanceStatus.Worked,
                NormalHours = 7.5m,
                IsApproved = true
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task AddComponentAsync(
        Context context,
        int componentType,
        decimal amount,
        int calculationType = 1,   // Günlük
        int paymentMethod = 0,     // Bordro ile
        bool includeInProjectCost = true,
        bool includeInProgressPaymentCost = false,
        string name = "Kalem")
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.HrCompensationComponents.Add(new HrCompensationComponent
        {
            CompanyId = context.CompanyId,
            PersonnelId = context.PersonnelId,
            Code = name,
            Name = name,
            ComponentType = componentType,
            CalculationType = calculationType,
            PaymentMethod = paymentMethod,
            Amount = amount,
            CurrencyCode = "TRY",
            EffectiveStartDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            IncludeInPayroll = false,
            IncludeInProjectCost = includeInProjectCost,
            IncludeInProgressPaymentCost = includeInProgressPaymentCost,
            IsActive = true
        });

        await db.SaveChangesAsync();
    }

    private async Task CalculateAsync(Context context)
    {
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync(
            "/api/hr/payroll/records/calculate-company",
            new
            {
                companyId = context.CompanyId,
                year = Year,
                month = Month,
                recalculateExisting = true
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<List<HrProjectLaborCost>> LoadCostsAsync(Context context)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.HrProjectLaborCosts.AsNoTracking()
            .Where(x => x.ProjectId == context.ProjectId)
            .OrderBy(x => x.WorkDate)
            .ToListAsync();
    }

    /// <summary>
    /// Günlük kalem her çalışma gününe yazılır ve toplam maliyete
    /// girer. Eskiden dördü de sıfır kalıyor, TotalLaborCost salt
    /// puantaj ücretine eşit oluyordu.
    /// </summary>
    [Fact]
    public async Task DailyComponents_LandOnEveryWorkDay()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        await AddWorkDaysAsync(context, 10);
        await AddComponentAsync(context, componentType: 3, amount: 120m, name: "Yemek");
        await AddComponentAsync(context, componentType: 2, amount: 80m, name: "Servis");
        await AddComponentAsync(context, componentType: 4, amount: 200m, name: "Konaklama");

        await CalculateAsync(context);

        var costs = await LoadCostsAsync(context);

        Assert.Equal(10, costs.Count);
        Assert.All(costs, cost =>
        {
            Assert.Equal(120m, cost.MealCost);
            Assert.Equal(80m, cost.ShuttleCost);
            Assert.Equal(200m, cost.AccommodationCost);

            // Toplam artık bileşenleri içeriyor.
            Assert.Equal(
                cost.NormalCost + cost.OvertimeCost + cost.SundayCost +
                cost.PublicHolidayCost + 400m,
                cost.TotalLaborCost);
        });
    }

    /// <summary>
    /// Aylık kalemin TAMAMI çalışılan günlere dağılır: 6.000 TL / 20
    /// gün = 300 TL/gün, toplamda yine 6.000 TL. Sabit 30'a bölmek
    /// kalemin üçte birini hiçbir projeye yazmamak olurdu.
    /// </summary>
    [Fact]
    public async Task MonthlyComponent_FullyDistributesOverWorkedDays()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        await AddWorkDaysAsync(context, 20);
        await AddComponentAsync(context,
            componentType: 3, amount: 6_000m, calculationType: 0, name: "Aylık Yemek");

        await CalculateAsync(context);

        var costs = await LoadCostsAsync(context);

        Assert.Equal(20, costs.Count);
        Assert.All(costs, cost => Assert.Equal(300m, cost.MealCost));
        Assert.Equal(6_000m, costs.Sum(x => x.MealCost));
    }

    /// <summary>
    /// Nakit kalem elden kovasına gider: gerçek maliyettir ama ek
    /// ödeme yetkisi olmayan kullanıcıdan maskelenebilsin diye ayrı
    /// tutulur.
    /// </summary>
    [Fact]
    public async Task CashComponent_LandsOnMaskedBucket()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        await AddWorkDaysAsync(context, 5);
        await AddComponentAsync(context,
            componentType: 3, amount: 150m, paymentMethod: 1, name: "Elden Yemek");

        await CalculateAsync(context);

        var costs = await LoadCostsAsync(context);

        Assert.All(costs, cost =>
        {
            Assert.Equal(0m, cost.MealCost);
            Assert.Equal(150m, cost.CompensationCost);
        });
    }

    /// <summary>
    /// Hakediş maliyeti proje maliyetinin tamamı değil: işaretlenmemiş
    /// kalem şirketin üstünde kalır, proje kârını düşürür ama hakediş
    /// kârını değil.
    /// </summary>
    [Fact]
    public async Task ProgressPaymentCost_OnlyIncludesFlaggedComponents()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        await AddWorkDaysAsync(context, 5);
        await AddComponentAsync(context, componentType: 3, amount: 120m,
            includeInProgressPaymentCost: true, name: "Yemek");
        await AddComponentAsync(context, componentType: 4, amount: 300m,
            includeInProgressPaymentCost: false, name: "Konaklama");

        await CalculateAsync(context);

        var costs = await LoadCostsAsync(context);

        Assert.All(costs, cost =>
        {
            var wage = cost.NormalCost + cost.OvertimeCost +
                       cost.SundayCost + cost.PublicHolidayCost;

            Assert.Equal(wage + 420m, cost.TotalLaborCost);
            Assert.Equal(wage + 120m, cost.ProgressPaymentCost);
        });
    }

    /// <summary>
    /// Proje maliyetine dâhil edilmemiş kalem hiç yazılmaz; puantaj
    /// ücreti hakediş maliyetinde kalmaya devam eder.
    /// </summary>
    [Fact]
    public async Task ComponentOutsideProjectCost_IsIgnored()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        await AddWorkDaysAsync(context, 5);
        await AddComponentAsync(context, componentType: 3, amount: 120m,
            includeInProjectCost: false, name: "Yemek");

        await CalculateAsync(context);

        var costs = await LoadCostsAsync(context);

        Assert.All(costs, cost =>
        {
            var wage = cost.NormalCost + cost.OvertimeCost +
                       cost.SundayCost + cost.PublicHolidayCost;

            Assert.Equal(0m, cost.MealCost);
            Assert.Equal(wage, cost.TotalLaborCost);
            Assert.Equal(wage, cost.ProgressPaymentCost);
        });
    }

    /// <summary>
    /// Kalemi olmayan personelde davranış hiç değişmiyor: bağlama
    /// geriye dönük bir fark yaratmadı.
    /// </summary>
    [Fact]
    public async Task WithoutComponents_LaborCostIsWageOnly()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        await AddWorkDaysAsync(context, 5);
        await CalculateAsync(context);

        var costs = await LoadCostsAsync(context);

        Assert.All(costs, cost =>
        {
            var wage = cost.NormalCost + cost.OvertimeCost +
                       cost.SundayCost + cost.PublicHolidayCost;

            Assert.Equal(0m, cost.MealCost);
            Assert.Equal(0m, cost.AccommodationCost);
            Assert.Equal(0m, cost.ShuttleCost);
            Assert.Equal(0m, cost.CompensationCost);
            Assert.Equal(wage, cost.TotalLaborCost);
            Assert.Equal(wage, cost.ProgressPaymentCost);
        });
    }

    /// <summary>
    /// Yeni yazıcıdan geçmemiş satır (dışarıdan aktarılmış ya da eski
    /// biçimde kalmış) hakediş maliyetinde SIFIR sayılmaz: eski
    /// davranışa düşülür. Sıfır saymak, maliyeti hiç yokmuş gibi
    /// gösterip hakediş kârını şişirirdi — düzeltmeye çalıştığımız
    /// hatanın aynısı.
    /// </summary>
    [Fact]
    public async Task LegacyRowWithoutProgressPaymentCost_FallsBackToTotal()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.HrProjectLaborCosts.Add(new HrProjectLaborCost
            {
                CompanyId = context.CompanyId,
                ProjectId = context.ProjectId,
                PersonnelId = context.PersonnelId,
                WorkDate = new DateTime(Year, Month, 4, 0, 0, 0, DateTimeKind.Utc),
                NormalCost = 80_000m,
                TotalLaborCost = 100_000m,
                CompensationCost = 20_000m
                // ProgressPaymentCost bilerek yazılmadı.
            });

            await db.SaveChangesAsync();
        }

        var costs = await LoadCostsAsync(context);
        var legacy = Assert.Single(costs);

        Assert.Equal(0m, legacy.ProgressPaymentCost);

        // Hakediş maliyeti bu satırı yok saymamalı: yetkiliye 100.000,
        // yetkisize elden düşülmüş 80.000 görünür.
        var authorised = await ProgressPaymentCostAsync(context, canSeeCash: true);
        var restricted = await ProgressPaymentCostAsync(context, canSeeCash: false);

        Assert.Equal(100_000m, authorised);
        Assert.Equal(80_000m, restricted);
    }

    /// <summary>
    /// Hakediş kâr servisinin işçilik toplamını, servisi ayağa
    /// kaldırmadan aynı kuralla yeniden üretir.
    /// </summary>
    private async Task<decimal> ProgressPaymentCostAsync(
        Context context, bool canSeeCash)
    {
        var rows = await LoadCostsAsync(context);

        return rows.Sum(x =>
        {
            var total = x.ProgressPaymentCost <= 0m && x.TotalLaborCost > 0m
                ? x.TotalLaborCost
                : x.ProgressPaymentCost;

            var cash = x.ProgressPaymentCost <= 0m && x.TotalLaborCost > 0m
                ? x.CompensationCost
                : x.ProgressPaymentCompensationCost;

            return canSeeCash ? total : total - cash;
        });
    }

    /// <summary>
    /// Elle girilen maliyet satırı da bileşenleri kabul ediyor ve
    /// toplama dahil ediyor; eskiden yalnız normal/mesai/diğer
    /// alınıyordu.
    /// </summary>
    [Fact]
    public async Task ManualEntry_AcceptsComponentBreakdown()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync(
            $"/api/projects/{context.ProjectId}/labor-costs",
            new
            {
                personnelId = context.PersonnelId,
                workDate = new DateTime(Year, Month, 3, 0, 0, 0, DateTimeKind.Utc),
                normalHours = 7.5m,
                overtimeHours = 0m,
                normalCost = 2_000m,
                overtimeCost = 0m,
                otherCost = 100m,
                mealCost = 120m,
                accommodationCost = 300m,
                shuttleCost = 80m,
                compensationCost = 500m
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var costs = await LoadCostsAsync(context);
        var manual = Assert.Single(costs);

        Assert.Equal(120m, manual.MealCost);
        Assert.Equal(300m, manual.AccommodationCost);
        Assert.Equal(80m, manual.ShuttleCost);
        Assert.Equal(500m, manual.CompensationCost);
        Assert.Equal(3_100m, manual.TotalLaborCost);
        Assert.Equal(3_100m, manual.ProgressPaymentCost);
        Assert.Equal(500m, manual.ProgressPaymentCompensationCost);
    }
}
