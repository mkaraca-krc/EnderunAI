using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security.CurrentUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/tasks")]
public sealed class WorkTasksController(
    AppDbContext db,
    ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] Guid? assignedToUserId,
        [FromQuery] int? status,
        [FromQuery] int? priority,
        [FromQuery] bool? overdueOnly,
        CancellationToken cancellationToken)
    {
        var query = db.WorkTasks.AsNoTracking().AsQueryable();

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);
        if (projectId.HasValue)
            query = query.Where(x => x.ProjectId == projectId.Value);
        if (assignedToUserId.HasValue)
            query = query.Where(x => x.AssignedToUserId == assignedToUserId.Value);
        if (status.HasValue)
            query = query.Where(x => (int)x.Status == status.Value);
        if (priority.HasValue)
            query = query.Where(x => (int)x.Priority == priority.Value);

        var now = DateTime.UtcNow;
        if (overdueOnly == true)
        {
            query = query.Where(x =>
                x.DueDate.HasValue &&
                x.DueDate.Value < now &&
                x.Status != WorkTaskStatus.Completed &&
                x.Status != WorkTaskStatus.Cancelled);
        }

        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Ok(items.Select(ToDto));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var item = await db.WorkTasks.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        return item is null
            ? NotFound(new { message = "Görev bulunamadı." })
            : Ok(ToDto(item));
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] Guid? companyId,
        CancellationToken cancellationToken)
    {
        var query = db.WorkTasks.AsNoTracking().AsQueryable();
        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        var rows = await query
            .Select(x => new
            {
                x.Status,
                x.Priority,
                x.DueDate,
                x.AssignedToUserId,
                x.CompletedAtUtc
            })
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var today = now.Date;
        var openStatuses = new[] { WorkTaskStatus.Open, WorkTaskStatus.InProgress, WorkTaskStatus.Waiting };

        return Ok(new
        {
            totalOpen = rows.Count(x => openStatuses.Contains(x.Status)),
            assignedToMe = rows.Count(x =>
                openStatuses.Contains(x.Status) &&
                x.AssignedToUserId == currentUser.UserId),
            dueToday = rows.Count(x =>
                openStatuses.Contains(x.Status) &&
                x.DueDate.HasValue &&
                x.DueDate.Value.Date == today),
            overdue = rows.Count(x =>
                openStatuses.Contains(x.Status) &&
                x.DueDate.HasValue &&
                x.DueDate.Value < now),
            critical = rows.Count(x =>
                openStatuses.Contains(x.Status) &&
                x.Priority == WorkTaskPriority.Critical),
            completedToday = rows.Count(x =>
                x.CompletedAtUtc.HasValue &&
                x.CompletedAtUtc.Value.Date == today)
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        CreateWorkTaskRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { message = "Görev başlığı zorunludur." });

        var sequence = await db.WorkTasks.CountAsync(
            x => x.CompanyId == request.CompanyId, cancellationToken);

        var item = new WorkTask
        {
            CompanyId = request.CompanyId,
            ProjectId = request.ProjectId,
            TaskNumber = $"GRV-{DateTime.UtcNow:yyyy}-{sequence + 1:D5}",
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            Priority = request.Priority,
            Status = WorkTaskStatus.Open,
            AssignedToUserId = request.AssignedToUserId,
            AssignedByUserId = currentUser.UserId,
            StartDate = ToUtcDate(request.StartDate),
            DueDate = ToUtcDate(request.DueDate),
            SourceModule = request.SourceModule?.Trim(),
            SourceEntityId = request.SourceEntityId,
            SourceEventCode = request.SourceEventCode?.Trim(),
            Tags = request.Tags?.Trim()
        };

        db.WorkTasks.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(item));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateWorkTaskRequest request,
        CancellationToken cancellationToken)
    {
        var item = await db.WorkTasks.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "Görev bulunamadı." });

        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { message = "Görev başlığı zorunludur." });

        item.Title = request.Title.Trim();
        item.Description = request.Description?.Trim();
        item.Priority = request.Priority;
        item.AssignedToUserId = request.AssignedToUserId;
        item.StartDate = ToUtcDate(request.StartDate);
        item.DueDate = ToUtcDate(request.DueDate);
        item.Tags = request.Tags?.Trim();
        item.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(item));
    }

    [HttpPost("{id:guid}/start")]
    public async Task<IActionResult> Start(Guid id, CancellationToken cancellationToken)
    {
        var item = await db.WorkTasks.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "Görev bulunamadı." });

        item.Status = WorkTaskStatus.InProgress;
        item.StartedAtUtc = DateTime.UtcNow;
        item.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(item));
    }

    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> Complete(
        Guid id,
        CompleteWorkTaskRequest request,
        CancellationToken cancellationToken)
    {
        var item = await db.WorkTasks.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "Görev bulunamadı." });

        item.Status = WorkTaskStatus.Completed;
        item.CompletedAtUtc = DateTime.UtcNow;
        item.CompletedByUserId = currentUser.UserId;
        item.CompletionNote = request.CompletionNote?.Trim();
        item.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(item));
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(
        Guid id,
        CancelWorkTaskRequest request,
        CancellationToken cancellationToken)
    {
        var item = await db.WorkTasks.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "Görev bulunamadı." });

        item.Status = WorkTaskStatus.Cancelled;
        item.CancelledAtUtc = DateTime.UtcNow;
        item.CancelledByUserId = currentUser.UserId;
        item.CancellationReason = request.Reason?.Trim();
        item.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(item));
    }

    private static DateTime? ToUtcDate(DateTime? value) =>
        value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;

    private static object ToDto(WorkTask x) => new
    {
        x.Id,
        x.CompanyId,
        x.ProjectId,
        x.TaskNumber,
        x.Title,
        x.Description,
        Priority = (int)x.Priority,
        PriorityName = x.Priority.ToString(),
        Status = (int)x.Status,
        StatusName = x.Status.ToString(),
        x.AssignedToUserId,
        x.AssignedByUserId,
        x.StartDate,
        x.DueDate,
        x.StartedAtUtc,
        x.CompletedAtUtc,
        x.CompletionNote,
        x.SourceModule,
        x.SourceEntityId,
        x.SourceEventCode,
        x.Tags,
        IsOverdue = x.DueDate.HasValue &&
                    x.DueDate.Value < DateTime.UtcNow &&
                    x.Status != WorkTaskStatus.Completed &&
                    x.Status != WorkTaskStatus.Cancelled,
        x.CreatedAtUtc
    };
}

public sealed record CreateWorkTaskRequest(
    Guid CompanyId,
    Guid? ProjectId,
    string Title,
    string? Description,
    WorkTaskPriority Priority,
    Guid? AssignedToUserId,
    DateTime? StartDate,
    DateTime? DueDate,
    string? SourceModule,
    Guid? SourceEntityId,
    string? SourceEventCode,
    string? Tags);

public sealed record UpdateWorkTaskRequest(
    string Title,
    string? Description,
    WorkTaskPriority Priority,
    Guid? AssignedToUserId,
    DateTime? StartDate,
    DateTime? DueDate,
    string? Tags);

public sealed record CompleteWorkTaskRequest(string? CompletionNote);

public sealed record CancelWorkTaskRequest(string Reason);
