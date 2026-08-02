using System.Net.Mail;
using System.Security.Cryptography;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Services.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/employer-portal-link")]
public sealed class EmployerPortalLinkController(
    AppDbContext db,
    ICurrentUserService currentUser,
    IEmailService emailService) : ControllerBase
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
                x.RevokedAtUtc,
                x.EmployerName,
                x.EmployerEmail
            })
            .FirstOrDefaultAsync(cancellationToken);

        return Ok(new
        {
            link,
            emailConfigured = emailService.IsConfigured
        });
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

    [HttpPost("send-email")]
    public async Task<IActionResult> SendEmail(
        Guid projectId,
        SendPortalEmailRequest request,
        CancellationToken cancellationToken)
    {
        if (!emailService.IsConfigured)
        {
            return BadRequest(new { message = "E-posta yapılandırılmamış." });
        }

        if (string.IsNullOrWhiteSpace(request.EmployerEmail) ||
            !MailAddress.TryCreate(request.EmployerEmail, out _))
        {
            return BadRequest(new { message = "Geçerli bir e-posta adresi girin." });
        }

        var project = await db.Projects.AsNoTracking()
            .Where(x => x.Id == projectId)
            .Select(x => new { x.Name })
            .SingleOrDefaultAsync(cancellationToken);

        if (project is null)
            return NotFound(new { message = "Proje bulunamadı." });

        var link = await db.EmployerPortalLinks
            .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.IsActive, cancellationToken);

        if (link is null)
            return NotFound(new { message = "Aktif bir işveren portalı linki bulunamadı." });

        if (string.IsNullOrWhiteSpace(request.PortalUrl) ||
            !request.PortalUrl.TrimEnd('/').EndsWith($"/portal/{link.Token}", StringComparison.Ordinal))
        {
            return BadRequest(new { message = "Portal linki geçersiz." });
        }

        link.EmployerName = request.EmployerName?.Trim();
        link.EmployerEmail = request.EmployerEmail.Trim();
        link.UpdatedAtUtc = DateTime.UtcNow;

        var log = new EmployerPortalEmailLog
        {
            EmployerPortalLinkId = link.Id,
            ProjectId = projectId,
            RecipientEmail = link.EmployerEmail,
            RecipientName = link.EmployerName,
            SentAtUtc = DateTime.UtcNow,
            CreatedByUserId = currentUser.UserId
        };

        try
        {
            var subject = $"{project.Name} - Saha Takip Portalı";
            var html = EmployerPortalEmailTemplate.Build(project.Name, request.PortalUrl, link.EmployerName);

            await emailService.SendAsync(
                link.EmployerEmail,
                link.EmployerName,
                subject,
                html,
                cancellationToken);

            log.IsSuccess = true;
        }
        catch (Exception exception)
        {
            log.IsSuccess = false;
            log.ErrorMessage = "E-posta gönderilemedi: sunucuya bağlanılamadı veya kimlik doğrulanamadı.";

            db.EmployerPortalEmailLogs.Add(log);
            await db.SaveChangesAsync(cancellationToken);

            Console.Error.WriteLine($"[EmployerPortalEmail] Gönderim hatası: {exception}");

            return StatusCode(502, new
            {
                message = "E-posta gönderilemedi: sunucuya bağlanılamadı veya kimlik doğrulanamadı."
            });
        }

        db.EmployerPortalEmailLogs.Add(log);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "E-posta gönderildi." });
    }

    [HttpGet("email-log")]
    public async Task<IActionResult> GetEmailLog(Guid projectId, CancellationToken cancellationToken)
    {
        var items = await db.EmployerPortalEmailLogs.AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.SentAtUtc)
            .Take(20)
            .Select(x => new
            {
                x.Id,
                x.RecipientEmail,
                x.RecipientName,
                x.SentAtUtc,
                x.IsSuccess,
                x.ErrorMessage
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
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

public sealed record SendPortalEmailRequest(
    string? EmployerName,
    string EmployerEmail,
    string PortalUrl);
