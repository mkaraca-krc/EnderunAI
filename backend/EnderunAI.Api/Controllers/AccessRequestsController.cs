using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EnderunAI.Api.Contracts;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Mesai saati dışı erişim taleplerinin GM/Admin onay ekranı. Onaylanan
/// talep, varsayılan (ya da seçilen) süreli bir TemporaryAccessGrant
/// üretir; grant süresi dolunca WorkHourAccessMiddleware oturumu otomatik
/// keser.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin,Genel Müdür")]
[Route("api/access-requests")]
public sealed class AccessRequestsController(AppDbContext db) : ControllerBase
{
    private const int DefaultGrantDurationMinutes = 120;

    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.UserManagementView)]
    public async Task<IActionResult> Get(
        [FromQuery] bool includeDecided,
        CancellationToken cancellationToken)
    {
        var query = db.AccessRequests
            .AsNoTracking()
            .Include(item => item.User)
            .AsQueryable();

        if (!includeDecided)
            query = query.Where(item => item.Status == AccessRequestStatus.Pending);

        var items = await query
            .OrderByDescending(item => item.CreatedAtUtc)
            .Take(100)
            .Select(item => new
            {
                item.Id,
                item.UserId,
                Username = item.User.Username,
                FullName = item.User.FullName,
                item.Reason,
                Status = (int)item.Status,
                item.CreatedAtUtc,
                item.DecidedAtUtc,
                item.GrantedDurationMinutes,
                item.RejectionReason
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpPost("{id:guid}/approve")]
    [RequirePermission(PermissionCatalog.Keys.UserManagementEdit)]
    public async Task<IActionResult> Approve(
        Guid id,
        ApproveAccessRequestRequest request,
        CancellationToken cancellationToken)
    {
        var accessRequest = await db.AccessRequests
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (accessRequest is null)
            return NotFound(new { message = "Erişim talebi bulunamadı." });

        if (accessRequest.Status != AccessRequestStatus.Pending)
            return BadRequest(new { message = "Bu talep zaten karara bağlanmış." });

        var durationMinutes = request.DurationMinutes is > 0
            ? request.DurationMinutes.Value
            : DefaultGrantDurationMinutes;

        var currentUserId = GetCurrentUserId();
        var now = DateTime.UtcNow;

        accessRequest.Status = AccessRequestStatus.Approved;
        accessRequest.DecidedByUserId = currentUserId;
        accessRequest.DecidedAtUtc = now;
        accessRequest.GrantedDurationMinutes = durationMinutes;

        db.TemporaryAccessGrants.Add(new TemporaryAccessGrant
        {
            UserId = accessRequest.UserId,
            SourceAccessRequestId = accessRequest.Id,
            GrantedByUserId = currentUserId ?? accessRequest.UserId,
            ExpiresAtUtc = now.AddMinutes(durationMinutes)
        });

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = $"Erişim talebi onaylandı, {durationMinutes} dakika süreyle erişim tanındı."
        });
    }

    [HttpPost("{id:guid}/reject")]
    [RequirePermission(PermissionCatalog.Keys.UserManagementEdit)]
    public async Task<IActionResult> Reject(
        Guid id,
        RejectAccessRequestRequest request,
        CancellationToken cancellationToken)
    {
        var accessRequest = await db.AccessRequests
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (accessRequest is null)
            return NotFound(new { message = "Erişim talebi bulunamadı." });

        if (accessRequest.Status != AccessRequestStatus.Pending)
            return BadRequest(new { message = "Bu talep zaten karara bağlanmış." });

        accessRequest.Status = AccessRequestStatus.Rejected;
        accessRequest.DecidedByUserId = GetCurrentUserId();
        accessRequest.DecidedAtUtc = DateTime.UtcNow;
        accessRequest.RejectionReason = string.IsNullOrWhiteSpace(request.RejectionReason)
            ? null
            : request.RejectionReason.Trim();

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Erişim talebi reddedildi." });
    }

    private Guid? GetCurrentUserId()
    {
        var value =
            User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        return Guid.TryParse(value, out var id) ? id : null;
    }
}
