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
/// Bordro giderinin masraf merkezi kırılımı.
///
/// Merkez personelinin gideri merkez ofisin masraf merkezine, şantiye
/// personelininki çalıştığı projeye yazılır. İki güvence birden aranır:
/// kırılım doğru olacak VE fiş dengesi bozulmayacak — yanlış bölünmüş
/// bir gider satırı defteri sessizce tutarsız bırakırdı.
/// </summary>
[Collection("Integration")]
public sealed class PayrollCostCenterTests(DatabaseFixture fixture)
{
    private const int Year = 2026;
    private const int Month = 7;

    private sealed record Context(
        Guid CompanyId,
        string CompanyCode,
        string HeadOfficeCostCenter,
        string ProjectCode,
        Guid HeadOfficePersonnelId,
        Guid SitePersonnelId);

    private static async Task SeedChartOfAccountsAsync(AppDbContext db, Guid companyId)
    {
        db.AccountingAccounts.AddRange(
            new AccountingAccount
            {
                CompanyId = companyId, Code = "770", Name = "Genel Yönetim Giderleri",
                Nature = AccountingAccountNature.Debit, Level = 3, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "335", Name = "Personele Borçlar",
                Nature = AccountingAccountNature.Credit, Level = 3, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "360", Name = "Ödenecek Vergi ve Fonlar",
                Nature = AccountingAccountNature.Credit, Level = 3, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "361", Name = "Ödenecek SGK Kesintileri",
                Nature = AccountingAccountNature.Credit, Level = 3, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "195", Name = "İş Avansları",
                Nature = AccountingAccountNature.Debit, Level = 3, IsPostingAllowed = true
            });

        await db.SaveChangesAsync();
    }

    private async Task<Context> CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var company = await db.Companies.SingleAsync(x => x.Id == project.CompanyId);

        var headOffice = await db.Branches.SingleAsync(
            x => x.CompanyId == company.Id && x.IsHeadOffice);
        headOffice.Name = "Merkez Ofis";
        headOffice.CostCenterCode = $"MERKEZ-{suffix}";

        await SeedChartOfAccountsAsync(db, company.Id);

        db.CompanyPayrollSettings.Add(new CompanyPayrollSettings
        {
            CompanyId = company.Id,
            Year = Year,
            MinimumWageGross = 33_030m,
            MinimumWageNet = 28_075.50m,
            SgkBaseFloor = 33_030m,
            SgkBaseCeiling = 247_725m,
            VerifiedAtUtc = DateTime.UtcNow,
            TaxBrackets =
            [
                new() { Order = 1, LowerBound = 0m, UpperBound = 200_000m, Rate = 15m },
                new() { Order = 2, LowerBound = 200_000m, UpperBound = null, Rate = 20m }
            ]
        });

        var site = new ProjectSite
        {
            ProjectId = project.Id,
            Code = $"SNT-{suffix}",
            Name = $"Test Şantiye {suffix}"
        };
        db.ProjectSites.Add(site);

        var headOfficePersonnel = new Personnel
        {
            CompanyId = company.Id,
            EmployeeNumber = $"MRK-{suffix}",
            FirstName = "Merkez",
            LastName = "Personeli",
            Status = PersonnelStatus.Active,
            WorkLocationType = WorkLocationType.HeadOffice,
            BranchId = headOffice.Id
        };

        var sitePersonnel = new Personnel
        {
            CompanyId = company.Id,
            EmployeeNumber = $"SHA-{suffix}",
            FirstName = "Saha",
            LastName = "Personeli",
            Status = PersonnelStatus.Active,
            WorkLocationType = WorkLocationType.ProjectSite
        };

        db.Personnel.AddRange(headOfficePersonnel, sitePersonnel);
        await db.SaveChangesAsync();

        db.ProjectSiteAssignments.Add(new ProjectSiteAssignment
        {
            PersonnelId = sitePersonnel.Id,
            ProjectSiteId = site.Id,
            StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();

        // İki personel de aynı brütte: kırılımın yarı yarıya olması
        // beklenir, böylece dağıtımın doğruluğu tek bakışta görünür.
        foreach (var personnelId in new[] { headOfficePersonnel.Id, sitePersonnel.Id })
        {
            hrDb.SalaryDefinitions.Add(new HrSalaryDefinition
            {
                CompanyId = company.Id,
                PersonnelId = personnelId,
                EffectiveStartDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                GrossSalary = 60_000m,
                NetSalary = 47_356.63m,
                CurrencyCode = "TRY"
            });
        }

        await hrDb.SaveChangesAsync();

        return new Context(
            company.Id,
            company.Code,
            headOffice.CostCenterCode,
            project.Code,
            headOfficePersonnel.Id,
            sitePersonnel.Id);
    }

    private async Task<HttpClient> PostPeriodAsync(Context context)
    {
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var calculate = await client.PostAsJsonAsync(
            "/api/hr/payroll/records/calculate-company",
            new
            {
                companyId = context.CompanyId,
                year = Year,
                month = Month,
                recalculateExisting = true
            });

        Assert.Equal(HttpStatusCode.OK, calculate.StatusCode);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

            var records = await hrDb.PayrollRecords
                .Where(x => x.CompanyId == context.CompanyId && x.Month == Month)
                .ToListAsync();

            Assert.Equal(2, records.Count);

            foreach (var record in records)
            {
                var approve = await client.PostAsync(
                    $"/api/hr/payroll/records/{record.Id}/approve", null);
                Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
            }
        }

        return client;
    }

    [Fact]
    public async Task PostPeriod_SplitsExpenseByCostCenter()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await PostPeriodAsync(context);

        var response = await client.PostAsJsonAsync(
            "/api/hr/payroll/periods/post",
            new { companyId = context.CompanyId, year = Year, month = Month });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var voucherId = payload.GetProperty("accountingVoucherId").GetGuid();

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var voucher = await db.AccountingVouchers
            .Include(x => x.Lines).ThenInclude(x => x.AccountingAccount)
            .SingleAsync(x => x.Id == voucherId);

        var expenseLines = voucher.Lines
            .Where(x => x.AccountingAccount.Code == "770")
            .ToList();

        Assert.Equal(2, expenseLines.Count);

        var headOfficeLine = expenseLines.Single(
            x => x.CostCenterCode == context.HeadOfficeCostCenter);
        var projectLine = expenseLines.Single(
            x => x.CostCenterCode == context.ProjectCode);

        // 60.000 brüt + işveren payı 12.450 = 72.450 (kişi başı).
        Assert.Equal(72_450m, headOfficeLine.DebitAmount);
        Assert.Equal(72_450m, projectLine.DebitAmount);

        // Bölünme fişin dengesini bozmamalı.
        Assert.Equal(voucher.TotalDebit, voucher.TotalCredit);
        Assert.Equal(144_900m, voucher.TotalDebit);
    }

    [Fact]
    public async Task PostPeriod_UsesSingleLineWhenEveryoneShareOneCostCenter()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        // Saha personelini de merkeze al: tek masraf merkezi kalır.
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var personnel = await db.Personnel.SingleAsync(
                x => x.Id == context.SitePersonnelId);
            var headOffice = await db.Branches.SingleAsync(
                x => x.CompanyId == context.CompanyId && x.IsHeadOffice);

            personnel.WorkLocationType = WorkLocationType.HeadOffice;
            personnel.BranchId = headOffice.Id;

            var assignments = await db.ProjectSiteAssignments
                .Where(x => x.PersonnelId == context.SitePersonnelId)
                .ToListAsync();

            foreach (var assignment in assignments)
                assignment.IsActive = false;

            await db.SaveChangesAsync();
        }

        var client = await PostPeriodAsync(context);

        var response = await client.PostAsJsonAsync(
            "/api/hr/payroll/periods/post",
            new { companyId = context.CompanyId, year = Year, month = Month });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var voucherId = payload.GetProperty("accountingVoucherId").GetGuid();

        using var verifyScope = fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var voucher = await verifyDb.AccountingVouchers
            .Include(x => x.Lines).ThenInclude(x => x.AccountingAccount)
            .SingleAsync(x => x.Id == voucherId);

        var expenseLine = voucher.Lines.Single(x => x.AccountingAccount.Code == "770");

        Assert.Equal(context.HeadOfficeCostCenter, expenseLine.CostCenterCode);
        Assert.Equal(144_900m, expenseLine.DebitAmount);
        Assert.Equal(voucher.TotalDebit, voucher.TotalCredit);
    }
}
