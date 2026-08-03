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
/// Faz E2: aylık toplu bordro hesabının uçtan uca davranışı — ücret
/// kartının tek doğru kaynak olması ve kartı olmayan personel için
/// sessizce tutar uydurulmaması.
/// </summary>
[Collection("Integration")]
public sealed class PayrollCompanyCalculationTests(DatabaseFixture fixture)
{
    private const int Year = 2026;

    private sealed record Context(Guid CompanyId, Guid WithCardId, Guid WithoutCardId);

    private async Task<Context> CreateContextAsync(string suffix)
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
            SgkBaseCeiling = 247_725m,
            TaxBrackets = new List<PayrollTaxBracket>
            {
                new() { Order = 1, LowerBound = 0m, UpperBound = 200_000m, Rate = 15m },
                new() { Order = 2, LowerBound = 200_000m, UpperBound = null, Rate = 20m }
            }
        });

        var withCard = NewPersonnel(company.Id, suffix, "Kartli");
        var withoutCard = NewPersonnel(company.Id, suffix, "Kartsiz");
        db.Personnel.AddRange(withCard, withoutCard);
        await db.SaveChangesAsync();

        // Yalnızca ilk personelin dönemde yürürlükte ücret kartı var.
        hrDb.SalaryDefinitions.Add(new HrSalaryDefinition
        {
            CompanyId = company.Id,
            PersonnelId = withCard.Id,
            EffectiveStartDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            GrossSalary = 33_030m,
            NetSalary = 28_075.50m,
            DailyRate = 1_101m,
            HourlyRate = 146.80m,
            CurrencyCode = "TRY"
        });
        await hrDb.SaveChangesAsync();

        return new Context(company.Id, withCard.Id, withoutCard.Id);
    }

    private static Personnel NewPersonnel(Guid companyId, string suffix, string name) =>
        new()
        {
            CompanyId = companyId,
            EmployeeNumber = $"{name}-{suffix}"[..Math.Min(20, $"{name}-{suffix}".Length)],
            FirstName = name,
            LastName = "Test",
            // Personel kartındaki maaş artık kullanılmıyor; kullanılsaydı
            // bu değer brüt sanılıp asgari ücret altı bordro üretirdi.
            MonthlySalary = 28_075.50m,
            Status = PersonnelStatus.Active
        };

    [Fact]
    public async Task Calculate_UsesSalaryCardAndSkipsPersonnelWithoutOne()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync(
            "/api/hr/payroll/records/calculate-company",
            new { companyId = context.CompanyId, year = Year, month = 1, recalculateExisting = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(1, payload.GetProperty("createdCount").GetInt32());
        Assert.Equal(1, payload.GetProperty("missingSalaryDefinitionCount").GetInt32());

        using var scope = fixture.Factory.Services.CreateScope();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        var records = await hrDb.PayrollRecords
            .Where(x => x.CompanyId == context.CompanyId && x.Year == Year && x.Month == 1)
            .ToListAsync();

        var record = Assert.Single(records);
        Assert.Equal(context.WithCardId, record.PersonnelId);

        // Ücret kartındaki brüt kullanıldı, personel kartındaki net değil.
        Assert.Equal(33_030m, record.GrossSalary);
        Assert.Equal(4_624.20m, record.SgkEmployeeDeduction);
        Assert.Equal(330.30m, record.UnemploymentEmployeeDeduction);
        Assert.Equal(0m, record.IncomeTaxDeduction);
        Assert.Equal(28_075.50m, record.OfficialNetPayableAmount);

        // Kartsız personel için hiçbir kayıt üretilmedi.
        Assert.DoesNotContain(records, x => x.PersonnelId == context.WithoutCardId);
    }

    [Fact]
    public async Task Calculate_CarriesCumulativeTaxBaseAcrossMonths()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        foreach (var month in new[] { 1, 2 })
        {
            var response = await client.PostAsJsonAsync(
                "/api/hr/payroll/records/calculate-company",
                new { companyId = context.CompanyId, year = Year, month, recalculateExisting = true });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var scope = fixture.Factory.Services.CreateScope();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        var january = await hrDb.PayrollRecords.SingleAsync(
            x => x.CompanyId == context.CompanyId && x.Month == 1);
        var february = await hrDb.PayrollRecords.SingleAsync(
            x => x.CompanyId == context.CompanyId && x.Month == 2);

        Assert.Equal(28_075.50m, january.CumulativeIncomeTaxBase);

        // Şubat, ocağın matrahı üzerine devrediyor.
        Assert.Equal(56_151m, february.CumulativeIncomeTaxBase);
    }

    /// <summary>
    /// Onaylı puantajdaki fazla mesai ve tatil çalışması bordroya
    /// çarpanlarıyla yansımalı; devamsız gün ücretten düşmeli.
    /// </summary>
    [Fact]
    public async Task Calculate_AppliesApprovedAttendanceToEarnings()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // 29 tam gün + 1 devamsız gün, toplam 10 saat fazla mesai.
            for (var day = 1; day <= 30; day++)
            {
                db.AttendanceRecords.Add(new AttendanceRecord
                {
                    CompanyId = context.CompanyId,
                    PersonnelId = context.WithCardId,
                    WorkDate = new DateTime(Year, 4, day, 0, 0, 0, DateTimeKind.Utc),
                    Status = day == 30
                        ? (int)AttendanceStatus.Absent
                        : (int)AttendanceStatus.Worked,
                    NormalHours = day == 30 ? 0m : 7.5m,
                    OvertimeHours = day <= 2 ? 5m : 0m,
                    IsApproved = true
                });
            }

            await db.SaveChangesAsync();
        }

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync(
            "/api/hr/payroll/records/calculate-company",
            new { companyId = context.CompanyId, year = Year, month = 4, recalculateExisting = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var verifyScope = fixture.Factory.Services.CreateScope();
        var hrDb = verifyScope.ServiceProvider.GetRequiredService<HrDbContext>();

        var record = await hrDb.PayrollRecords.SingleAsync(
            x => x.CompanyId == context.CompanyId && x.Month == 4);

        // 29 ücrete esas gün × 1.101 günlük ücret
        Assert.Equal(31_929m, record.NormalWorkAmount);

        // 10 saat × 146,80 × 1,5
        Assert.Equal(2_202m, record.OvertimeAmount);

        Assert.Equal(34_131m, record.TotalEarnings);

        // Devamsızlık nedeniyle brüt aylık ücretin altında kalmasına
        // rağmen fazla mesai toplam kazancı yukarı çekiyor.
        Assert.True(record.TotalEarnings > record.GrossSalary);
    }

    /// <summary>
    /// Onaylanmamış puantaj bordroya girmemeli.
    /// </summary>
    [Fact]
    public async Task Calculate_IgnoresUnapprovedAttendance()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.AttendanceRecords.Add(new AttendanceRecord
            {
                CompanyId = context.CompanyId,
                PersonnelId = context.WithCardId,
                WorkDate = new DateTime(Year, 5, 1, 0, 0, 0, DateTimeKind.Utc),
                Status = (int)AttendanceStatus.Worked,
                OvertimeHours = 20m,
                IsApproved = false
            });

            await db.SaveChangesAsync();
        }

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await client.PostAsJsonAsync(
            "/api/hr/payroll/records/calculate-company",
            new { companyId = context.CompanyId, year = Year, month = 5, recalculateExisting = true });

        using var verifyScope = fixture.Factory.Services.CreateScope();
        var hrDb = verifyScope.ServiceProvider.GetRequiredService<HrDbContext>();

        var record = await hrDb.PayrollRecords.SingleAsync(
            x => x.CompanyId == context.CompanyId && x.Month == 5);

        Assert.Equal(0m, record.OvertimeAmount);
        // Puantaj sayılmadığı için tam aylık ücret geçerli.
        Assert.Equal(33_030m, record.NormalWorkAmount);
    }

    /// <summary>
    /// Projeli puantaj günleri şantiye kırılımıyla işçilik maliyetine
    /// dönüşmeli.
    /// </summary>
    [Fact]
    public async Task Calculate_GeneratesProjectLaborCostsWithSiteBreakdown()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        Guid projectId;
        Guid siteId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var branch = await db.Branches.FirstAsync(x => x.CompanyId == context.CompanyId);
            var employer = await db.CurrentAccounts
                .FirstAsync(x => x.CompanyId == context.CompanyId);

            var project = new Project
            {
                CompanyId = context.CompanyId,
                BranchId = branch.Id,
                EmployerCurrentAccountId = employer.Id,
                Code = $"PRJ-{suffix}",
                Name = $"Test Proje {suffix}",
                CurrencyCode = "TRY",
                Status = ProjectStatus.Active
            };
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            projectId = project.Id;

            var site = new ProjectSite
            {
                ProjectId = projectId,
                Code = $"SNT-{suffix}",
                Name = $"Test Şantiye {suffix}"
            };
            db.ProjectSites.Add(site);
            await db.SaveChangesAsync();
            siteId = site.Id;

            db.AttendanceRecords.Add(new AttendanceRecord
            {
                CompanyId = context.CompanyId,
                PersonnelId = context.WithCardId,
                ProjectId = projectId,
                ProjectSiteId = siteId,
                WorkDate = new DateTime(Year, 6, 2, 0, 0, 0, DateTimeKind.Utc),
                Status = (int)AttendanceStatus.Worked,
                NormalHours = 7.5m,
                OvertimeHours = 2m,
                IsApproved = true
            });

            await db.SaveChangesAsync();
        }

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await client.PostAsJsonAsync(
            "/api/hr/payroll/records/calculate-company",
            new { companyId = context.CompanyId, year = Year, month = 6, recalculateExisting = true });

        using var verifyScope = fixture.Factory.Services.CreateScope();
        var db2 = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cost = await db2.HrProjectLaborCosts.SingleAsync(
            x => x.CompanyId == context.CompanyId && x.ProjectId == projectId);

        Assert.Equal(siteId, cost.ProjectSiteId);
        Assert.Equal(1_101m, cost.NormalCost);
        Assert.Equal(440.40m, cost.OvertimeCost); // 146,80 × 1,5 × 2
        Assert.Equal(1_541.40m, cost.TotalLaborCost);
    }

    [Fact]
    public async Task Calculate_FailsWhenPayrollParametersAreMissing()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync(
            "/api/hr/payroll/records/calculate-company",
            new { companyId = company.Id, year = Year, month = 1, recalculateExisting = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "bordro parametreleri",
            (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("message").GetString()!,
            StringComparison.OrdinalIgnoreCase);
    }
}
