using EnderunAI.Api.Data;
using EnderunAI.Api.Data.HumanResources;
using EnderunAI.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/hr/career")]
public sealed class HrCareerController(
    AppDbContext db,
    HrDbContext hrDb) : ControllerBase
{
    private static readonly string[] KindByActionType =
    {
        "hire", "promotion", "position-change", "department-change",
        "salary-change", "project-change", "terminate"
    };

    private static readonly Dictionary<string, HrCareerActionType> ActionTypeByKind =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["hire"] = HrCareerActionType.Hire,
            ["promotion"] = HrCareerActionType.Promotion,
            ["position-change"] = HrCareerActionType.PositionChange,
            ["department-change"] = HrCareerActionType.DepartmentChange,
            ["salary-change"] = HrCareerActionType.SalaryChange,
            ["project-change"] = HrCareerActionType.ProjectChange,
            ["terminate"] = HrCareerActionType.Terminate
        };

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        CancellationToken cancellationToken)
    {
        var query = db.HrCareerHistories.AsNoTracking().AsQueryable();
        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        var rows = await query
            .OrderByDescending(x => x.EffectiveDate)
            .ToListAsync(cancellationToken);

        return Ok(await ToDtosAsync(rows, cancellationToken));
    }

    [HttpGet("personnel/{personnelId:guid}")]
    public async Task<IActionResult> GetPersonnelHistory(
        Guid personnelId,
        CancellationToken cancellationToken)
    {
        var rows = await db.HrCareerHistories.AsNoTracking()
            .Where(x => x.PersonnelId == personnelId)
            .OrderByDescending(x => x.EffectiveDate)
            .ToListAsync(cancellationToken);

        return Ok(await ToDtosAsync(rows, cancellationToken));
    }

    [HttpGet("analysis/{personnelId:guid}")]
    public async Task<IActionResult> GetPersonnelAnalysis(
        Guid personnelId,
        CancellationToken cancellationToken)
    {
        var personnel = await db.Personnel.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == personnelId, cancellationToken);

        if (personnel is null)
            return NotFound(new { message = "Personel bulunamadı." });

        var rows = await db.HrCareerHistories.AsNoTracking()
            .Where(x => x.PersonnelId == personnelId)
            .OrderByDescending(x => x.EffectiveDate)
            .ToListAsync(cancellationToken);

        var lastPromotion = rows.FirstOrDefault(x => x.ActionType == HrCareerActionType.Promotion);
        var lastSalary = rows
            .Where(x => x.NewSalary.HasValue)
            .OrderByDescending(x => x.EffectiveDate)
            .FirstOrDefault();

        var monthsSincePromotion = lastPromotion is null
            ? (int?)null
            : (int)Math.Floor((DateTime.UtcNow - lastPromotion.EffectiveDate).TotalDays / 30);

        var readinessScore = monthsSincePromotion is >= 18 ? 80
            : monthsSincePromotion is >= 12 ? 55
            : 25;

        return Ok(new
        {
            personnelId,
            personnelName = $"{personnel.FirstName} {personnel.LastName}".Trim(),
            totalMovements = rows.Count,
            promotionCount = rows.Count(x => x.ActionType == HrCareerActionType.Promotion),
            departmentChangeCount = rows.Count(x => x.ActionType == HrCareerActionType.DepartmentChange),
            positionChangeCount = rows.Count(x => x.ActionType == HrCareerActionType.PositionChange),
            projectChangeCount = rows.Count(x => x.ActionType == HrCareerActionType.ProjectChange),
            salaryChangeCount = rows.Count(x => x.ActionType == HrCareerActionType.SalaryChange),
            currentSalary = lastSalary?.NewSalary ?? personnel.MonthlySalary,
            lastPromotionDate = lastPromotion?.EffectiveDate,
            nextPromotionCandidate = monthsSincePromotion is >= 18,
            promotionReadinessScore = readinessScore,
            careerSummary = rows.Count == 0
                ? "Bu personel için henüz kariyer hareketi kaydedilmemiş."
                : $"{rows.Count} kariyer hareketi kayıtlı, son hareket {rows[0].EffectiveDate:d}.",
            recommendations = monthsSincePromotion is >= 18
                ? new[] { "Terfi değerlendirmesi için aday olabilir." }
                : Array.Empty<string>()
        });
    }

    [HttpPost("{kind}")]
    public async Task<IActionResult> Create(
        string kind,
        CreateCareerMovementRequest request,
        CancellationToken cancellationToken)
    {
        if (!ActionTypeByKind.TryGetValue(kind, out var actionType))
            return BadRequest(new { message = "Geçersiz kariyer hareketi tipi." });

        var personnel = await db.Personnel
            .SingleOrDefaultAsync(x => x.Id == request.PersonnelId, cancellationToken);

        if (personnel is null)
            return NotFound(new { message = "Personel bulunamadı." });

        var item = new HrCareerHistory
        {
            CompanyId = personnel.CompanyId,
            PersonnelId = request.PersonnelId,
            ActionType = actionType,
            EffectiveDate = DateTime.SpecifyKind(
                (request.EffectiveDate ?? DateTime.UtcNow).Date, DateTimeKind.Utc),
            PreviousDepartmentId = request.PreviousDepartmentId,
            NewDepartmentId = request.NewDepartmentId,
            PreviousPositionId = request.PreviousPositionId,
            NewPositionId = request.NewPositionId,
            PreviousProjectId = request.PreviousProjectId,
            NewProjectId = request.NewProjectId,
            PreviousSalary = request.PreviousSalary,
            NewSalary = request.NewSalary,
            Reason = request.Reason?.Trim(),
            Notes = request.Notes?.Trim()
        };

        db.HrCareerHistories.Add(item);

        if (actionType == HrCareerActionType.Terminate)
        {
            personnel.Status = PersonnelStatus.Terminated;
            personnel.EmploymentEndDate = item.EffectiveDate;
            personnel.UpdatedAtUtc = DateTime.UtcNow;
        }
        else if (request.NewSalary.HasValue)
        {
            personnel.MonthlySalary = request.NewSalary;
            personnel.UpdatedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Kariyer hareketi kaydedildi.",
            item.Id
        });
    }

    private async Task<IReadOnlyList<object>> ToDtosAsync(
        IReadOnlyList<HrCareerHistory> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
            return Array.Empty<object>();

        var personnelIds = rows.Select(x => x.PersonnelId).Distinct().ToArray();
        var personnelMap = await db.Personnel.AsNoTracking()
            .Where(x => personnelIds.Contains(x.Id))
            .ToDictionaryAsync(
                x => x.Id,
                x => new { Name = $"{x.FirstName} {x.LastName}".Trim(), x.EmployeeNumber },
                cancellationToken);

        var projectIds = rows
            .SelectMany(x => new[] { x.PreviousProjectId, x.NewProjectId })
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();
        var projectNames = await db.Projects.AsNoTracking()
            .Where(x => projectIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var departmentIds = rows
            .SelectMany(x => new[] { x.PreviousDepartmentId, x.NewDepartmentId })
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();
        var departmentNames = await hrDb.Departments.AsNoTracking()
            .Where(x => departmentIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var positionIds = rows
            .SelectMany(x => new[] { x.PreviousPositionId, x.NewPositionId })
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();
        var positionNames = await hrDb.Positions.AsNoTracking()
            .Where(x => positionIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Title, cancellationToken);

        return rows.Select(x => (object)new
        {
            x.Id,
            x.PersonnelId,
            personnelName = personnelMap.GetValueOrDefault(x.PersonnelId)?.Name,
            employeeNumber = personnelMap.GetValueOrDefault(x.PersonnelId)?.EmployeeNumber,
            movementType = (int)x.ActionType,
            movementTypeName = x.ActionType.ToString(),
            type = KindByActionType[(int)x.ActionType],
            typeName = x.ActionType.ToString(),
            effectiveDate = x.EffectiveDate,
            movementDate = x.EffectiveDate,
            date = x.EffectiveDate,
            oldDepartmentId = x.PreviousDepartmentId,
            oldDepartmentName = x.PreviousDepartmentId.HasValue
                ? departmentNames.GetValueOrDefault(x.PreviousDepartmentId.Value) : null,
            newDepartmentId = x.NewDepartmentId,
            newDepartmentName = x.NewDepartmentId.HasValue
                ? departmentNames.GetValueOrDefault(x.NewDepartmentId.Value) : null,
            oldPositionId = x.PreviousPositionId,
            oldPositionName = x.PreviousPositionId.HasValue
                ? positionNames.GetValueOrDefault(x.PreviousPositionId.Value) : null,
            newPositionId = x.NewPositionId,
            newPositionName = x.NewPositionId.HasValue
                ? positionNames.GetValueOrDefault(x.NewPositionId.Value) : null,
            oldProjectId = x.PreviousProjectId,
            oldProjectName = x.PreviousProjectId.HasValue
                ? projectNames.GetValueOrDefault(x.PreviousProjectId.Value) : null,
            newProjectId = x.NewProjectId,
            newProjectName = x.NewProjectId.HasValue
                ? projectNames.GetValueOrDefault(x.NewProjectId.Value) : null,
            oldSalary = x.PreviousSalary,
            newSalary = x.NewSalary,
            reason = x.Reason,
            notes = x.Notes,
            createdAt = x.CreatedAtUtc,
            createdAtUtc = x.CreatedAtUtc
        }).ToList();
    }
}

public sealed record CreateCareerMovementRequest(
    Guid PersonnelId,
    DateTime? EffectiveDate,
    Guid? PreviousDepartmentId,
    Guid? NewDepartmentId,
    Guid? PreviousPositionId,
    Guid? NewPositionId,
    Guid? PreviousProjectId,
    Guid? NewProjectId,
    decimal? PreviousSalary,
    decimal? NewSalary,
    string? Reason,
    string? Notes);
