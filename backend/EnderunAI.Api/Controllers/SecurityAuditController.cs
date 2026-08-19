using EnderunAI.Api.Contracts.Core;
using EnderunAI.Api.Data;
using EnderunAI.Api.Security;
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
    [RequirePermission(PermissionCatalog.Keys.AuditLogView)]
    public async Task<IActionResult> GetEvents(
        [FromQuery] string? entityType,
        [FromQuery] Guid? entityId,
        [FromQuery] int take,
        [FromQuery] int? page,
        CancellationToken cancellationToken)
    {
        var limit = take > 0 && take <= 200 ? take : 50;

        var query = db.SecurityAuditEvents.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(entityType))
            query = query.Where(x => x.EntityType == entityType);
        if (entityId.HasValue)
            query = query.Where(x => x.EntityId == entityId.Value);


        // TOPLAM TAVANDAN ÖNCE SAYILIR — arayüz kırpıldığını bilsin.
        var total = await query.CountAsync(cancellationToken);

        // Denetim kütüğü yalnız BÜYÜR; canlıda 1.580 olay var ve
        // eskiye bakmanın tek yolu sayfalama.
        var currentPage = page is > 0 ? page.Value : 1;

        var items = await query
            .OrderByDescending(x => x.OccurredAtUtc)
            .Skip((currentPage - 1) * limit)
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

        return Ok(PagedResult<object>.FromPage(items, total, limit, currentPage));
    }
}
