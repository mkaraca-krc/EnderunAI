using EnderunAI.Api.Data;
using EnderunAI.Api.Data.HumanResources;
using EnderunAI.Api.Models.HumanResources;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Aylık bordro maliyet raporu: brütten işverene toplam maliyete kadar
/// tüm kırılım ve proje/şantiye bazlı işçilik dağılımı.
///
/// Tümü ücret gizliliğine tabidir — saha ve teknik roller bu uca hiç
/// erişemez.
/// </summary>
[ApiController]
[Authorize]
[Route("api/hr/payroll/cost-report")]
public sealed class PayrollCostReportController(
    HrDbContext hrDb,
    AppDbContext appDb) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.SalaryView)]
    public async Task<IActionResult> Get(
        [FromQuery] Guid companyId,
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
            return BadRequest(new { message = "Şirket seçimi zorunludur." });

        if (month is < 1 or > 12)
            return BadRequest(new { message = "Ay 1 ile 12 arasında olmalıdır." });

        var records = await hrDb.PayrollRecords
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Year == year && x.Month == month)
            .ToListAsync(cancellationToken);

        var personnelIds = records.Select(x => x.PersonnelId).ToList();

        var personnelById = await appDb.Personnel
            .AsNoTracking()
            .Where(x => personnelIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.EmployeeNumber,
                FullName = x.FirstName + " " + x.LastName,
                x.JobTitle
            })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var periodStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = periodStart.AddMonths(1).AddTicks(-1);

        // Proje ve şantiye bazlı işçilik dağılımı puantajdan üretilen
        // maliyet kayıtlarından gelir.
        var projectBreakdown = await appDb.HrProjectLaborCosts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.WorkDate >= periodStart && x.WorkDate <= periodEnd)
            .GroupBy(x => new { x.ProjectId, x.ProjectSiteId })
            .Select(g => new
            {
                g.Key.ProjectId,
                g.Key.ProjectSiteId,
                NormalCost = g.Sum(x => x.NormalCost),
                OvertimeCost = g.Sum(x => x.OvertimeCost),
                HolidayCost = g.Sum(x => x.SundayCost + x.PublicHolidayCost),
                TotalCost = g.Sum(x => x.TotalLaborCost),
                DayCount = g.Count()
            })
            .ToListAsync(cancellationToken);

        var projectIds = projectBreakdown.Select(x => x.ProjectId).Distinct().ToList();
        var siteIds = projectBreakdown
            .Where(x => x.ProjectSiteId != null)
            .Select(x => x.ProjectSiteId!.Value)
            .Distinct()
            .ToList();

        var projectNames = await appDb.Projects
            .AsNoTracking()
            .Where(x => projectIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Code, x.Name })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var siteNames = await appDb.ProjectSites
            .AsNoTracking()
            .Where(x => siteIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Code, x.Name })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var employerBurden = records.Sum(
            x => x.SgkEmployerAmount + x.UnemploymentEmployerAmount);

        return Ok(new
        {
            companyId,
            year,
            month,
            personnelCount = records.Count,
            paidCount = records.Count(x => x.Status == PayrollStatus.Paid),

            totals = new
            {
                grossSalary = records.Sum(x => x.GrossSalary),
                normalWorkAmount = records.Sum(x => x.NormalWorkAmount),
                overtimeAmount = records.Sum(x => x.OvertimeAmount),
                holidayAmount = records.Sum(
                    x => x.SundayWorkAmount + x.PublicHolidayAmount),
                totalEarnings = records.Sum(x => x.TotalEarnings),

                sgkEmployee = records.Sum(x => x.SgkEmployeeDeduction),
                unemploymentEmployee = records.Sum(x => x.UnemploymentEmployeeDeduction),
                incomeTax = records.Sum(x => x.IncomeTaxDeduction),
                stampTax = records.Sum(x => x.StampTaxDeduction),
                advanceAndOther = records.Sum(
                    x => x.AdvanceDeduction + x.OtherDeductionAmount),
                totalDeductions = records.Sum(x => x.TotalDeductions),

                netPayable = records.Sum(x => x.OfficialNetPayableAmount),

                sgkEmployer = records.Sum(x => x.SgkEmployerAmount),
                unemploymentEmployer = records.Sum(x => x.UnemploymentEmployerAmount),
                employerBurden,
                totalEmployerCost = records.Sum(x => x.TotalEmployerCost),

                // İstisnalar sayesinde kesilmeyen vergi — raporda ayrıca
                // gösterilir, maliyeti değiştirmez.
                incomeTaxExemption = records.Sum(x => x.IncomeTaxExemption),
                stampTaxExemption = records.Sum(x => x.StampTaxExemption)
            },

            personnel = records
                .OrderByDescending(x => x.TotalEmployerCost)
                .Select(x => new
                {
                    x.PersonnelId,
                    employeeNumber = personnelById.TryGetValue(x.PersonnelId, out var p)
                        ? p.EmployeeNumber
                        : null,
                    fullName = personnelById.TryGetValue(x.PersonnelId, out var p2)
                        ? p2.FullName
                        : null,
                    jobTitle = personnelById.TryGetValue(x.PersonnelId, out var p3)
                        ? p3.JobTitle
                        : null,
                    x.GrossSalary,
                    x.OvertimeAmount,
                    x.TotalEarnings,
                    x.TotalDeductions,
                    x.OfficialNetPayableAmount,
                    x.SgkEmployerAmount,
                    x.UnemploymentEmployerAmount,
                    x.TotalEmployerCost,
                    status = (int)x.Status
                })
                .ToList(),

            projectBreakdown = projectBreakdown
                .OrderByDescending(x => x.TotalCost)
                .Select(x => new
                {
                    x.ProjectId,
                    projectCode = projectNames.TryGetValue(x.ProjectId, out var pr)
                        ? pr.Code
                        : null,
                    projectName = projectNames.TryGetValue(x.ProjectId, out var pr2)
                        ? pr2.Name
                        : null,
                    x.ProjectSiteId,
                    siteCode = x.ProjectSiteId != null &&
                               siteNames.TryGetValue(x.ProjectSiteId.Value, out var st)
                        ? st.Code
                        : null,
                    siteName = x.ProjectSiteId != null &&
                               siteNames.TryGetValue(x.ProjectSiteId.Value, out var st2)
                        ? st2.Name
                        : null,
                    x.NormalCost,
                    x.OvertimeCost,
                    x.HolidayCost,
                    x.TotalCost,
                    x.DayCount
                })
                .ToList()
        });
    }
}
