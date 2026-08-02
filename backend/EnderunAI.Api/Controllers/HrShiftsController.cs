using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/hr/workforce")]
public sealed class HrShiftsController(AppDbContext db) : ControllerBase
{
    [HttpGet("shifts")]
    public async Task<IActionResult> GetShifts(
        [FromQuery] Guid? companyId,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var query = db.HrShiftDefinitions.AsNoTracking().AsQueryable();

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x =>
                x.Code.ToLower().Contains(term) || x.Name.ToLower().Contains(term));
        }

        var items = await query.OrderBy(x => x.Code).ToListAsync(cancellationToken);
        return Ok(items.Select(ToShiftDto));
    }

    [HttpPost("shifts")]
    public async Task<IActionResult> CreateShift(
        SaveShiftRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "Vardiya kodu ve adı zorunludur." });

        var code = request.Code.Trim().ToUpperInvariant();
        var duplicate = await db.HrShiftDefinitions.AnyAsync(
            x => x.CompanyId == request.CompanyId && x.Code == code, cancellationToken);

        if (duplicate)
            return Conflict(new { message = "Bu şirket için aynı kodda bir vardiya zaten var." });

        var item = new HrShiftDefinition { CompanyId = request.CompanyId };
        ApplyShift(item, request, code);

        db.HrShiftDefinitions.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(ToShiftDto(item));
    }

    [HttpPut("shifts/{id:guid}")]
    public async Task<IActionResult> UpdateShift(
        Guid id,
        SaveShiftRequest request,
        CancellationToken cancellationToken)
    {
        var item = await db.HrShiftDefinitions.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "Vardiya bulunamadı." });

        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "Vardiya kodu ve adı zorunludur." });

        var code = request.Code.Trim().ToUpperInvariant();
        var duplicate = await db.HrShiftDefinitions.AnyAsync(
            x => x.Id != id && x.CompanyId == item.CompanyId && x.Code == code, cancellationToken);

        if (duplicate)
            return Conflict(new { message = "Bu şirket için aynı kodda bir vardiya zaten var." });

        ApplyShift(item, request, code);
        item.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToShiftDto(item));
    }

    [HttpDelete("shifts/{id:guid}")]
    public async Task<IActionResult> DeleteShift(Guid id, CancellationToken cancellationToken)
    {
        var item = await db.HrShiftDefinitions.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "Vardiya bulunamadı." });

        var hasAssignments = await db.HrShiftAssignments.AnyAsync(
            x => x.ShiftDefinitionId == id && x.EndDate == null, cancellationToken);

        if (hasAssignments)
            return Conflict(new { message = "Aktif ataması olan vardiya silinemez." });

        item.IsActive = false;
        item.IsDeleted = true;
        item.DeletedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = "Vardiya silindi." });
    }

    [HttpGet("shift-assignments")]
    public async Task<IActionResult> GetAssignments(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? personnelId,
        [FromQuery] Guid? projectId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        CancellationToken cancellationToken)
    {
        var query = db.HrShiftAssignments.AsNoTracking().AsQueryable();

        if (companyId.HasValue) query = query.Where(x => x.CompanyId == companyId.Value);
        if (personnelId.HasValue) query = query.Where(x => x.PersonnelId == personnelId.Value);
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId.Value);
        if (startDate.HasValue)
        {
            var start = DateTime.SpecifyKind(startDate.Value.Date, DateTimeKind.Utc);
            query = query.Where(x => !x.EndDate.HasValue || x.EndDate >= start);
        }
        if (endDate.HasValue)
        {
            var end = DateTime.SpecifyKind(endDate.Value.Date, DateTimeKind.Utc);
            query = query.Where(x => x.StartDate <= end);
        }

        var items = await query
            .OrderByDescending(x => x.StartDate)
            .ToListAsync(cancellationToken);

        return Ok(items.Select(ToAssignmentDto));
    }

    [HttpPost("shift-assignments")]
    public async Task<IActionResult> CreateAssignment(
        SaveShiftAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        var shiftExists = await db.HrShiftDefinitions.AnyAsync(
            x => x.Id == request.ShiftDefinitionId, cancellationToken);

        if (!shiftExists)
            return NotFound(new { message = "Vardiya bulunamadı." });

        var item = new HrShiftAssignment
        {
            CompanyId = request.CompanyId,
            PersonnelId = request.PersonnelId,
            ShiftDefinitionId = request.ShiftDefinitionId,
            ProjectId = request.ProjectId,
            StartDate = DateTime.SpecifyKind(request.StartDate.Date, DateTimeKind.Utc),
            EndDate = request.EndDate.HasValue
                ? DateTime.SpecifyKind(request.EndDate.Value.Date, DateTimeKind.Utc)
                : null,
            TeamName = request.TeamName?.Trim(),
            Description = request.Description?.Trim()
        };

        db.HrShiftAssignments.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(ToAssignmentDto(item));
    }

    [HttpDelete("shift-assignments/{id:guid}")]
    public async Task<IActionResult> DeleteAssignment(Guid id, CancellationToken cancellationToken)
    {
        var item = await db.HrShiftAssignments.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "Vardiya ataması bulunamadı." });

        item.IsActive = false;
        item.IsDeleted = true;
        item.DeletedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = "Vardiya ataması silindi." });
    }

    private static void ApplyShift(HrShiftDefinition item, SaveShiftRequest request, string code)
    {
        item.Code = code;
        item.Name = request.Name.Trim();
        item.StartTime = request.StartTime;
        item.EndTime = request.EndTime;
        item.BreakHours = request.BreakHours;
        item.DailyWorkingHours = request.DailyWorkingHours;
        item.IsNightShift = request.IsNightShift;
        item.Description = request.Description?.Trim();
    }

    private static object ToShiftDto(HrShiftDefinition x) => new
    {
        x.Id,
        x.CompanyId,
        x.Code,
        x.Name,
        StartTime = x.StartTime.ToString(),
        EndTime = x.EndTime.ToString(),
        x.BreakHours,
        x.DailyWorkingHours,
        x.IsNightShift,
        x.Description,
        x.CreatedAtUtc
    };

    private static object ToAssignmentDto(HrShiftAssignment x) => new
    {
        x.Id,
        x.CompanyId,
        x.PersonnelId,
        x.ShiftDefinitionId,
        x.ProjectId,
        x.StartDate,
        x.EndDate,
        x.TeamName,
        x.Description,
        x.CreatedAtUtc
    };
}

public sealed record SaveShiftRequest(
    Guid CompanyId,
    string Code,
    string Name,
    TimeSpan StartTime,
    TimeSpan EndTime,
    decimal BreakHours,
    decimal DailyWorkingHours,
    bool IsNightShift,
    string? Description);

public sealed record SaveShiftAssignmentRequest(
    Guid CompanyId,
    Guid PersonnelId,
    Guid ShiftDefinitionId,
    Guid? ProjectId,
    DateTime StartDate,
    DateTime? EndDate,
    string? TeamName,
    string? Description);
