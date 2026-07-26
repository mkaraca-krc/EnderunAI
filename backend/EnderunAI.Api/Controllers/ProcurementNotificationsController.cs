using System.Security.Claims;
using EnderunAI.Api.Data;
using EnderunAI.Api.Services.Procurement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Route("api/procurement-notifications")]
[Authorize]
public sealed class ProcurementNotificationsController(
    ProcurementNotificationDbContext db,
    IProcurementNotificationService generator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> List(
        [FromQuery] bool unreadOnly = false,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        var userId = GetUserId();
        var roles = User.FindAll(ClaimTypes.Role)
            .Select(x => x.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        take = Math.Clamp(take, 1, 200);
        var query = db.Notifications.AsNoTracking()
            .Where(x => x.DismissedAtUtc == null &&
                        ((x.UserId.HasValue && x.UserId == userId) ||
                         (x.RoleName != null && roles.Contains(x.RoleName))));

        if (unreadOnly)
            query = query.Where(x => x.ReadAtUtc == null);

        var result = await query
            .OrderByDescending(x => x.Severity)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

        return Ok(result);
    }

    [HttpGet("summary")]
    public async Task<ActionResult> Summary(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var roles = User.FindAll(ClaimTypes.Role).Select(x => x.Value).ToArray();
        var visible = db.Notifications.AsNoTracking()
            .Where(x => x.DismissedAtUtc == null &&
                        ((x.UserId.HasValue && x.UserId == userId) ||
                         (x.RoleName != null && roles.Contains(x.RoleName))));

        var result = await visible.GroupBy(_ => 1).Select(g => new
        {
            Total = g.Count(),
            Unread = g.Count(x => x.ReadAtUtc == null),
            Critical = g.Count(x => x.ReadAtUtc == null && x.Severity == Models.ProcurementNotificationSeverity.Critical),
            Warning = g.Count(x => x.ReadAtUtc == null && x.Severity == Models.ProcurementNotificationSeverity.Warning)
        }).FirstOrDefaultAsync(cancellationToken);

        return Ok(result ?? new { Total = 0, Unread = 0, Critical = 0, Warning = 0 });
    }

    [HttpPost("{id:guid}/read")]
    public async Task<ActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        var notification = await FindVisibleAsync(id, cancellationToken);
        if (notification is null)
            return NotFound();

        notification.ReadAtUtc ??= DateTime.UtcNow;
        notification.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<ActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var roles = User.FindAll(ClaimTypes.Role).Select(x => x.Value).ToArray();
        var notifications = await db.Notifications
            .Where(x => x.ReadAtUtc == null && x.DismissedAtUtc == null &&
                        ((x.UserId.HasValue && x.UserId == userId) ||
                         (x.RoleName != null && roles.Contains(x.RoleName))))
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var item in notifications)
        {
            item.ReadAtUtc = now;
            item.UpdatedAtUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { Updated = notifications.Count });
    }

    [HttpPost("{id:guid}/dismiss")]
    public async Task<ActionResult> Dismiss(Guid id, CancellationToken cancellationToken)
    {
        var notification = await FindVisibleAsync(id, cancellationToken);
        if (notification is null)
            return NotFound();

        notification.DismissedAtUtc = DateTime.UtcNow;
        notification.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("generate")]
    public async Task<ActionResult> Generate(CancellationToken cancellationToken)
    {
        var count = await generator.GenerateApprovalNotificationsAsync(cancellationToken);
        return Ok(new { Created = count });
    }

    private async Task<Models.ProcurementNotification?> FindVisibleAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var roles = User.FindAll(ClaimTypes.Role).Select(x => x.Value).ToArray();
        return await db.Notifications.FirstOrDefaultAsync(x => x.Id == id &&
            ((x.UserId.HasValue && x.UserId == userId) ||
             (x.RoleName != null && roles.Contains(x.RoleName))), cancellationToken);
    }

    private Guid? GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
