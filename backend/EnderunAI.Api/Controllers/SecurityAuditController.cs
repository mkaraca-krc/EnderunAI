using EnderunAI.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin,Genel Müdür")]
[Route("api/security-audit")]
public sealed class SecurityAuditController(AppDbContext db) : ControllerBase
{
    [HttpGet("events")]
    public async Task<IActionResult> GetEvents(
        [FromQuery] string? entityType,
        [FromQuery] Guid? entityId,
        [FromQuery] int take,
        CancellationToken cancellationToken)
    {
        var limit = take > 0 && take <= 200 ? take : 50;

        var query = db.SecurityAuditEvents.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(entityType))
            query = query.Where(x => x.EntityType == entityType);
        if (entityId.HasValue)
            query = query.Where(x => x.EntityId == entityId.Value);

        var items = await query
            .OrderByDescending(x => x.OccurredAtUtc)
            .Take(limit)
            .Select(x => new
            {
                x.Id,
                x.ActorUserId,
                x.ActorUsername,
                x.Action,
                x.EntityType,
                x.EntityId,
                x.DetailsJson,
                x.IpAddress,
                x.OccurredAtUtc
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }
}
