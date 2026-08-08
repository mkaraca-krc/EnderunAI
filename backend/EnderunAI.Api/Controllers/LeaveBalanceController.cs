using EnderunAI.Api.Data;
using EnderunAI.Api.Data.HumanResources;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.HumanResources;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.HumanResources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Yıllık izin bakiyesi.
///
/// Hak ediş kuralı burada TEKRAR YAZILMADI; çıkış tazminatında
/// kullanılan kademe tablosunun aynısı okunuyor. İkinci bir kural,
/// aynı personel için ekranda ve çıkışta farklı iki rakam üretirdi.
/// </summary>
[ApiController]
[Authorize]
[Route("api/hr/izin-bakiye")]
public sealed class LeaveBalanceController(AppDbContext db, HrDbContext hrDb)
    : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.AttendancePayrollView)]
    public async Task<IActionResult> Get(
        [FromQuery] Guid companyId,
        [FromQuery] Guid? personnelId,
        CancellationToken cancellationToken)
    {
        var query = db.Personnel
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.IsActive &&
                        x.Status == PersonnelStatus.Active);

        if (personnelId is Guid id)
            query = query.Where(x => x.Id == id);

        var personnel = await query
            .OrderBy(x => x.FirstName).ThenBy(x => x.LastName)
            .Select(x => new
            {
                x.Id,
                x.EmployeeNumber,
                FullName = x.FirstName + " " + x.LastName,
                x.EmploymentStartDate
            })
            .ToListAsync(cancellationToken);

        var ids = personnel.Select(x => x.Id).ToList();

        // Yalnızca YILLIK izin bakiyeye girer; rapor, mazeret ve
        // ücretsiz izin ayrı kalemler ve hak edişten düşmez.
        var leaveDays = await hrDb.LeaveRequests
            .AsNoTracking()
            .Where(x => ids.Contains(x.PersonnelId) &&
                        x.LeaveType == HrLeaveType.Annual &&
                        (x.Status == HrApprovalStatus.Approved ||
                         x.Status == HrApprovalStatus.Pending))
            .Select(x => new { x.PersonnelId, x.Status, x.TotalDays })
            .ToListAsync(cancellationToken);

        var used = leaveDays
            .Where(x => x.Status == HrApprovalStatus.Approved)
            .GroupBy(x => x.PersonnelId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.TotalDays));

        var pending = leaveDays
            .Where(x => x.Status == HrApprovalStatus.Pending)
            .GroupBy(x => x.PersonnelId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.TotalDays));

        var asOf = DateOnly.FromDateTime(DateTime.UtcNow);

        var balances = personnel
            .Select(x => LeaveBalanceCalculator.Calculate(
                new LeaveBalanceInput(
                    x.Id,
                    x.EmployeeNumber,
                    x.FullName,
                    x.EmploymentStartDate,
                    used.GetValueOrDefault(x.Id),
                    pending.GetValueOrDefault(x.Id)),
                asOf))
            .ToList();

        return Ok(new
        {
            asOf,
            personnelCount = balances.Count,
            totalEntitlementDays = balances.Sum(x => x.EntitlementDays),
            totalRemainingDays = balances.Sum(x => x.RemainingDays),
            // Hak edişi aşmış olanlar: avans izin verilmiş ya da veri
            // eksik; ikisi de bakılmayı hak ediyor.
            overdraftCount = balances.Count(x => x.RemainingDays < 0m),
            withoutStartDateCount = balances.Count(x => x.ServiceDays == 0 &&
                                                        x.NextAccrualDate is null),
            items = balances
        });
    }
}
