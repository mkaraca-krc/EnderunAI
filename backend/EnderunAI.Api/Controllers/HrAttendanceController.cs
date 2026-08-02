using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/hr/attendance")]
public sealed class HrAttendanceController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.AttendancePayrollView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] Guid? personnelId,
        [FromQuery] int? status,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        CancellationToken cancellationToken)
    {
        var query = db.AttendanceRecords.AsNoTracking().AsQueryable();

        if (companyId.HasValue) query = query.Where(x => x.CompanyId == companyId.Value);
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId.Value);
        if (personnelId.HasValue) query = query.Where(x => x.PersonnelId == personnelId.Value);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        if (startDate.HasValue)
        {
            var start = DateTime.SpecifyKind(startDate.Value.Date, DateTimeKind.Utc);
            query = query.Where(x => x.WorkDate >= start);
        }
        if (endDate.HasValue)
        {
            var end = DateTime.SpecifyKind(endDate.Value.Date, DateTimeKind.Utc);
            query = query.Where(x => x.WorkDate <= end);
        }

        var items = await query
            .OrderByDescending(x => x.WorkDate)
            .ToListAsync(cancellationToken);

        return Ok(items.Select(ToDto));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.AttendancePayrollView)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var item = await db.AttendanceRecords.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        return item is null
            ? NotFound(new { message = "Puantaj kaydı bulunamadı." })
            : Ok(ToDto(item));
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.AttendancePayrollCreate)]
    public async Task<IActionResult> Create(
        SaveAttendanceRequest request,
        CancellationToken cancellationToken)
    {
        var workDate = DateTime.SpecifyKind(request.WorkDate.Date, DateTimeKind.Utc);

        var duplicate = await db.AttendanceRecords.AnyAsync(
            x => x.CompanyId == request.CompanyId &&
                 x.PersonnelId == request.PersonnelId &&
                 x.WorkDate == workDate,
            cancellationToken);

        if (duplicate)
        {
            return Conflict(new
            {
                message = "Bu personel için bu tarihte zaten bir puantaj kaydı var."
            });
        }

        var item = new AttendanceRecord
        {
            CompanyId = request.CompanyId,
            ProjectId = request.ProjectId,
            PersonnelId = request.PersonnelId,
            WorkDate = workDate
        };

        Apply(item, request);

        db.AttendanceRecords.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(item));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.AttendancePayrollEdit)]
    public async Task<IActionResult> Update(
        Guid id,
        SaveAttendanceRequest request,
        CancellationToken cancellationToken)
    {
        var item = await db.AttendanceRecords.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "Puantaj kaydı bulunamadı." });

        Apply(item, request);
        item.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(item));
    }

    [HttpPost("{id:guid}/approve")]
    [RequirePermission(PermissionCatalog.Keys.AttendancePayrollApprove)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken)
    {
        var item = await db.AttendanceRecords.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "Puantaj kaydı bulunamadı." });

        item.IsApproved = true;
        item.ApprovedAtUtc = DateTime.UtcNow;
        item.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(item));
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.AttendancePayrollDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var item = await db.AttendanceRecords.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "Puantaj kaydı bulunamadı." });

        item.IsActive = false;
        item.IsDeleted = true;
        item.DeletedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = "Puantaj kaydı silindi." });
    }

    [HttpGet("summary")]
    [RequirePermission(PermissionCatalog.Keys.AttendancePayrollView)]
    public async Task<IActionResult> GetSummary(
        [FromQuery] Guid personnelId,
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate,
        CancellationToken cancellationToken)
    {
        var start = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);
        var end = DateTime.SpecifyKind(endDate.Date, DateTimeKind.Utc);

        var rows = await db.AttendanceRecords.AsNoTracking()
            .Where(x =>
                x.PersonnelId == personnelId &&
                x.WorkDate >= start &&
                x.WorkDate <= end)
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            personnelId,
            startDate = start,
            endDate = end,
            presentDays = rows.Count(x => x.Status == 1),
            leaveDays = rows.Count(x => x.Status == 2),
            absenceDays = rows.Count(x => x.Status == 0),
            normalHours = rows.Sum(x => x.NormalHours),
            overtimeHours = rows.Sum(x => x.OvertimeHours),
            nightShiftHours = rows.Sum(x => x.NightShiftHours),
            sundayHours = rows.Sum(x => x.SundayHours),
            publicHolidayHours = rows.Sum(x => x.PublicHolidayHours),
            totalHours = rows.Sum(x => x.TotalHours)
        });
    }

    private static void Apply(AttendanceRecord item, SaveAttendanceRequest request)
    {
        item.Status = request.Status;
        item.CheckInTime = request.CheckInTime;
        item.CheckOutTime = request.CheckOutTime;
        item.NormalHours = request.NormalHours;
        item.OvertimeHours = request.OvertimeHours;
        item.NightShiftHours = request.NightShiftHours;
        item.SundayHours = request.SundayHours;
        item.PublicHolidayHours = request.PublicHolidayHours;
        item.TotalHours = request.NormalHours + request.OvertimeHours +
                           request.NightShiftHours + request.SundayHours +
                           request.PublicHolidayHours;
        item.TeamName = request.TeamName?.Trim();
        item.RoleName = request.RoleName?.Trim();
        item.WorkItemCode = request.WorkItemCode?.Trim();
        item.WorkItemName = request.WorkItemName?.Trim();
        item.LocationName = request.LocationName?.Trim();
        item.Description = request.Description?.Trim();
    }

    private static object ToDto(AttendanceRecord x) => new
    {
        x.Id,
        x.CompanyId,
        x.ProjectId,
        x.PersonnelId,
        x.WorkDate,
        x.Status,
        StatusName = AttendanceStatusName(x.Status),
        CheckInTime = x.CheckInTime?.ToString(),
        CheckOutTime = x.CheckOutTime?.ToString(),
        x.NormalHours,
        x.OvertimeHours,
        x.NightShiftHours,
        x.SundayHours,
        x.PublicHolidayHours,
        x.TotalHours,
        x.TeamName,
        x.RoleName,
        x.WorkItemCode,
        x.WorkItemName,
        x.LocationName,
        x.IsApproved,
        x.ApprovedByUserId,
        x.ApprovedAtUtc,
        x.Description,
        x.CreatedAtUtc
    };

    private static string AttendanceStatusName(int status) => status switch
    {
        0 => "Devamsız",
        1 => "Çalıştı",
        2 => "İzinli",
        3 => "Raporlu",
        4 => "Resmi Tatil",
        5 => "Hafta Tatili",
        6 => "Ücretsiz İzin",
        7 => "Devamsız (Mazeretli)",
        8 => "Yarım Gün",
        9 => "Diğer",
        _ => "Bilinmiyor"
    };
}

public sealed record SaveAttendanceRequest(
    Guid CompanyId,
    Guid? ProjectId,
    Guid PersonnelId,
    DateTime WorkDate,
    int Status,
    TimeSpan? CheckInTime,
    TimeSpan? CheckOutTime,
    decimal NormalHours,
    decimal OvertimeHours,
    decimal NightShiftHours,
    decimal SundayHours,
    decimal PublicHolidayHours,
    string? TeamName,
    string? RoleName,
    string? WorkItemCode,
    string? WorkItemName,
    string? LocationName,
    string? Description);
