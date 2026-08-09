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
/// Görevlendirmenin gün maliyetine etkisi.
///
/// DEĞİŞMEZ: toplam işçilik KORUNUR — yeniden dağıtılır, yaratılmaz.
/// Bir personelin bir aydaki toplam işçilik maliyeti, görevlendirme
/// öncesi ve sonrası AYNI kalır; yalnızca hangi projeye yazıldığı
/// değişir.
///
/// Bu yapı gereği sağlanıyor: gün zaten tek satır olarak yazılıyor,
/// görev yalnızca hedefi değiştiriyor. Ev projesinden "düşme" diye
/// ayrı bir işlem yok — gün oraya hiç yazılmıyor.
/// </summary>
[Collection("Integration")]
public sealed class DutyLaborCostShiftTests(DatabaseFixture fixture)
{
    private const int Year = 2026;
    private const int Month = 6;
    private const decimal Gross = 60_000m;

    /// <summary>Görev günleri: 8-12 Haziran = 5 gün.</summary>
    private static readonly DateTime DutyStart =
        new(Year, Month, 8, 0, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime DutyEnd =
        new(Year, Month, 12, 0, 0, 0, DateTimeKind.Utc);

    private sealed record Context(
        Guid CompanyId, Guid PersonnelId, Guid HomeProjectId,
        Guid TargetProjectId, Guid SurveyProjectId);

    private async Task<Context> CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        var home = await TestDataFactory.CreateProjectAsync(db, suffix);
        home.Status = ProjectStatus.Active;

        var target = new Project
        {
            CompanyId = home.CompanyId,
            BranchId = home.BranchId,
            Code = $"HDF-{suffix}",
            Name = $"Hedef Proje {suffix}",
            Status = ProjectStatus.Active,
            CurrencyCode = "TRY"
        };

        var survey = new Project
        {
            CompanyId = home.CompanyId,
            BranchId = home.BranchId,
            Code = $"KSF-{suffix}",
            Name = $"Keşif İşi {suffix}",
            Status = ProjectStatus.Kesif,
            CurrencyCode = "TRY"
        };

        var personnel = new Personnel
        {
            CompanyId = home.CompanyId,
            EmployeeNumber = $"GRV-{suffix}",
            FirstName = "Görevli",
            LastName = "Test",
            Status = PersonnelStatus.Active
        };

        db.Projects.AddRange(target, survey);
        db.Personnel.Add(personnel);

        db.CompanyPayrollSettings.Add(new CompanyPayrollSettings
        {
            CompanyId = home.CompanyId,
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

        await db.SaveChangesAsync();

        hrDb.SalaryDefinitions.Add(new HrSalaryDefinition
        {
            CompanyId = home.CompanyId,
            PersonnelId = personnel.Id,
            EffectiveStartDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            GrossSalary = Gross,
            CurrencyCode = "TRY"
        });

        await hrDb.SaveChangesAsync();

        return new Context(
            home.CompanyId, personnel.Id, home.Id, target.Id, survey.Id);
    }

    /// <summary>Ayın ilk 20 gününe ev projesinde onaylı puantaj yazar.</summary>
    private async Task AddHomeAttendanceAsync(Context context, int dayCount = 20)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        for (var day = 1; day <= dayCount; day++)
        {
            db.AttendanceRecords.Add(new AttendanceRecord
            {
                CompanyId = context.CompanyId,
                PersonnelId = context.PersonnelId,
                ProjectId = context.HomeProjectId,
                WorkDate = new DateTime(Year, Month, day, 0, 0, 0, DateTimeKind.Utc),
                Status = (int)AttendanceStatus.Worked,
                NormalHours = 7.5m,
                IsApproved = true
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Onaylı görev kaydı — onay akışı Blok 1'de sınandı.</summary>
    private async Task AddApprovedDutyAsync(
        Context context, PersonnelDutyType dutyType, Guid targetProjectId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.PersonnelDuties.Add(new PersonnelDuty
        {
            CompanyId = context.CompanyId,
            PersonnelId = context.PersonnelId,
            DutyType = dutyType,
            TargetProjectId = targetProjectId,
            SourceProjectId = context.HomeProjectId,
            StartDate = DutyStart,
            EndDate = DutyEnd,
            IsOutOfCity = true,
            DailyAllowance = 1_500m,
            Purpose = "Test görevi",
            Status = PersonnelDutyStatus.Approved,
            ApprovedAtUtc = DateTime.UtcNow
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
            .Where(x => x.PersonnelId == context.PersonnelId)
            .OrderBy(x => x.WorkDate)
            .ToListAsync();
    }

    // ---------------- 1. Çalışma görevi ----------------

    /// <summary>
    /// Çalışma görevi: 5 gün hedef projeye yazılıyor, aynı 5 gün ev
    /// projesinde YOK. Gün iki yere birden sayılmıyor.
    /// </summary>
    [Fact]
    public async Task WorkDuty_MovesTheDaysToTheTargetProject()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        await AddHomeAttendanceAsync(context);
        await AddApprovedDutyAsync(
            context, PersonnelDutyType.Work, context.TargetProjectId);

        await CalculateAsync(context);

        var costs = await LoadCostsAsync(context);

        Assert.Equal(20, costs.Count);

        var targetDays = costs.Where(x => x.ProjectId == context.TargetProjectId).ToList();
        var homeDays = costs.Where(x => x.ProjectId == context.HomeProjectId).ToList();

        Assert.Equal(5, targetDays.Count);
        Assert.Equal(15, homeDays.Count);

        // Hedefe giden günler tam olarak görev günleri.
        Assert.All(targetDays, cost =>
        {
            Assert.True(cost.WorkDate >= DutyStart && cost.WorkDate <= DutyEnd);
        });

        // Ev projesinde görev günü kalmamış.
        Assert.DoesNotContain(homeDays, cost =>
            cost.WorkDate >= DutyStart && cost.WorkDate <= DutyEnd);
    }

    /// <summary>
    /// ANA DEĞİŞMEZ: toplam işçilik korunuyor. Aynı ay, aynı puantaj;
    /// görevlendirmeli ve görevlendirmesiz toplam AYNI.
    /// </summary>
    [Fact]
    public async Task TotalLaborCost_IsPreservedNotCreated()
    {
        var withoutSuffix = Guid.NewGuid().ToString("N")[..8];
        var without = await CreateContextAsync(withoutSuffix);

        await AddHomeAttendanceAsync(without);
        await CalculateAsync(without);

        var baseline = (await LoadCostsAsync(without)).Sum(x => x.TotalLaborCost);

        var withSuffix = Guid.NewGuid().ToString("N")[..8];
        var with = await CreateContextAsync(withSuffix);

        await AddHomeAttendanceAsync(with);
        await AddApprovedDutyAsync(
            with, PersonnelDutyType.Work, with.TargetProjectId);

        await CalculateAsync(with);

        var shifted = await LoadCostsAsync(with);

        Assert.True(baseline > 0m, "Referans maliyet sıfır olmamalı.");
        Assert.Equal(baseline, shifted.Sum(x => x.TotalLaborCost));

        // Dağılım değişmiş ama toplam aynı.
        Assert.Equal(2, shifted.Select(x => x.ProjectId).Distinct().Count());
    }

    // ---------------- 2. Keşif ve ziyaret ----------------

    /// <summary>
    /// Keşif ve ziyaret görevinde işçilik günü HİÇ KAYMIYOR: kişi
    /// hedefte imalat üretmiyor, işçiliği kendi yerinde kalıyor.
    /// Hedefe yalnız masraf gider (Blok 3).
    /// </summary>
    [Theory]
    [InlineData(PersonnelDutyType.Survey)]
    [InlineData(PersonnelDutyType.Visit)]
    public async Task SurveyAndVisit_DoNotMoveLaborDays(PersonnelDutyType dutyType)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        await AddHomeAttendanceAsync(context);

        var target = dutyType == PersonnelDutyType.Survey
            ? context.SurveyProjectId
            : context.TargetProjectId;

        await AddApprovedDutyAsync(context, dutyType, target);

        await CalculateAsync(context);

        var costs = await LoadCostsAsync(context);

        // Bütün günler ev projesinde kalmış.
        Assert.Equal(20, costs.Count);
        Assert.All(costs, cost => Assert.Equal(context.HomeProjectId, cost.ProjectId));
        Assert.DoesNotContain(costs, cost => cost.ProjectId == target);
    }

    // ---------------- 3. Onay ve çakışma ----------------

    /// <summary>
    /// ONAYSIZ görev maliyeti kaydırmıyor: talep aşamasındaki bir
    /// görev projenin kârını değiştirmemeli.
    /// </summary>
    [Fact]
    public async Task UnapprovedDuty_DoesNotMoveAnything()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        await AddHomeAttendanceAsync(context);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.PersonnelDuties.Add(new PersonnelDuty
            {
                CompanyId = context.CompanyId,
                PersonnelId = context.PersonnelId,
                DutyType = PersonnelDutyType.Work,
                TargetProjectId = context.TargetProjectId,
                StartDate = DutyStart,
                EndDate = DutyEnd,
                DailyAllowance = 1_500m,
                Purpose = "Onay bekliyor",
                Status = PersonnelDutyStatus.Requested
            });

            await db.SaveChangesAsync();
        }

        await CalculateAsync(context);

        var costs = await LoadCostsAsync(context);

        Assert.All(costs, cost => Assert.Equal(context.HomeProjectId, cost.ProjectId));
    }

    /// <summary>
    /// Görev dönem sınırını aşsa bile yalnızca dönem içindeki günler
    /// kayıyor; her gün tek projeye yazılıyor.
    /// </summary>
    [Fact]
    public async Task EveryDay_IsWrittenExactlyOnce()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        await AddHomeAttendanceAsync(context);
        await AddApprovedDutyAsync(
            context, PersonnelDutyType.Work, context.TargetProjectId);

        await CalculateAsync(context);

        var costs = await LoadCostsAsync(context);

        // Aynı gün için iki satır yok.
        var duplicates = costs
            .GroupBy(x => x.WorkDate)
            .Where(g => g.Count() > 1)
            .ToList();

        Assert.Empty(duplicates);
        Assert.Equal(20, costs.Select(x => x.WorkDate).Distinct().Count());
    }

    /// <summary>
    /// Yeniden hesaplama maliyeti şişirmiyor: ikinci çalıştırma aynı
    /// toplamı veriyor.
    /// </summary>
    [Fact]
    public async Task Recalculation_IsIdempotent()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        await AddHomeAttendanceAsync(context);
        await AddApprovedDutyAsync(
            context, PersonnelDutyType.Work, context.TargetProjectId);

        await CalculateAsync(context);
        var first = (await LoadCostsAsync(context)).Sum(x => x.TotalLaborCost);

        await CalculateAsync(context);
        var second = await LoadCostsAsync(context);

        Assert.Equal(first, second.Sum(x => x.TotalLaborCost));
        Assert.Equal(20, second.Count);
    }
}
