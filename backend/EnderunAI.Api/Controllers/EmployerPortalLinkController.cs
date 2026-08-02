using System.Security.Cryptography;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security.CurrentUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/employer-portal-link")]
public sealed class EmployerPortalLinkController(
    AppDbContext db,
    ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid projectId, CancellationToken cancellationToken)
    {
        var link = await db.EmployerPortalLinks.AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.Token,
                x.IsActive,
                x.CreatedAtUtc,
                x.RevokedAtUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        return Ok(link);
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid projectId, CancellationToken cancellationToken)
    {
        var projectExists = await db.Projects.AsNoTracking()
            .AnyAsync(x => x.Id == projectId, cancellationToken);
        if (!projectExists)
            return NotFound(new { message = "Proje bulunamadı." });

        var existingActive = await db.EmployerPortalLinks
            .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.IsActive, cancellationToken);

        if (existingActive is not null)
        {
            existingActive.IsActive = false;
            existingActive.RevokedAtUtc = DateTime.UtcNow;
            existingActive.RevokedByUserId = currentUser.UserId;
        }

        var link = new EmployerPortalLink
        {
            ProjectId = projectId,
            Token = GenerateToken(),
            CreatedByUserId = currentUser.UserId
        };

        db.EmployerPortalLinks.Add(link);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "İşveren portalı linki oluşturuldu.",
            link.Id,
            link.Token
        });
    }

    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke(Guid projectId, CancellationToken cancellationToken)
    {
        var link = await db.EmployerPortalLinks
            .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.IsActive, cancellationToken);

        if (link is null)
            return NotFound(new { message = "Aktif bir işveren portalı linki bulunamadı." });

        link.IsActive = false;
        link.RevokedAtUtc = DateTime.UtcNow;
        link.RevokedByUserId = currentUser.UserId;
        link.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "İşveren portalı linki iptal edildi." });
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
