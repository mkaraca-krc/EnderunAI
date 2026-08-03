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

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    AppDbContext db,
    PasswordService passwordService,
    TokenService tokenService,
    ILoginAttemptService loginAttemptService,
    IUserAuthorizationService userAuthorizationService,
    IWorkHourAccessService workHourAccessService) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var ipAddress = ResolveClientIp();

        if (loginAttemptService.IsLocked(ipAddress, out var remaining))
        {
            var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
            return StatusCode(429, new
            {
                message = $"Çok fazla başarısız giriş denemesi. Lütfen {minutes} dakika sonra tekrar deneyin."
            });
        }

        var username = request.Username.Trim().ToLowerInvariant();
        var user = await db.Users
            .Include(item => item.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .SingleOrDefaultAsync(
                item => item.Username.ToLower() == username,
                cancellationToken);

        if (user is null ||
            !user.IsActive ||
            !passwordService.Verify(
                request.Password,
                user.PasswordHash,
                user.PasswordSalt))
        {
            loginAttemptService.RecordFailure(ipAddress);
            return Unauthorized(new { message = "Kullanıcı adı veya şifre hatalı." });
        }

        var workHourEvaluation = await workHourAccessService.EvaluateAsync(user.Id, cancellationToken);
        if (!workHourEvaluation.IsAllowed)
        {
            db.SecurityAuditEvents.Add(new SecurityAuditEvent
            {
                ActorUserId = user.Id,
                ActorUsername = user.Username,
                Action = "LoginRejectedOutsideWorkHours",
                EntityType = "WorkHourAccess",
                DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    summary = $"{user.Username} mesai saatleri dışında giriş denedi."
                }),
                IpAddress = ipAddress,
                UserAgent = Request.Headers.UserAgent.ToString(),
                OccurredAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);

            return StatusCode(403, new
            {
                message = workHourEvaluation.Reason,
                outsideWorkHours = true
            });
        }

        loginAttemptService.RecordSuccess(ipAddress);

        user.LastLoginAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var roleNames = user.UserRoles
            .Select(userRole => userRole.Role.Name)
            .ToArray();
        var authorization = await userAuthorizationService.GetAsync(
            user.Id,
            cancellationToken);
        var permissions = (authorization?.Permissions ?? [])
            .OrderBy(permission => permission)
            .ToArray();

        return Ok(new
        {
            token = tokenService.Create(user, roleNames, permissions),
            expiresInSeconds = 43200,
            user = new
            {
                user.Id,
                user.Username,
                user.FullName,
                user.Email,
                roles = roleNames,
                permissions
            }
        });
    }

    [AllowAnonymous]
    [HttpPost("access-requests")]
    public async Task<IActionResult> SubmitAccessRequest(
        SubmitAccessRequestRequest request,
        CancellationToken cancellationToken)
    {
        var ipAddress = ResolveClientIp();

        if (loginAttemptService.IsLocked(ipAddress, out var remaining))
        {
            var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
            return StatusCode(429, new
            {
                message = $"Çok fazla başarısız giriş denemesi. Lütfen {minutes} dakika sonra tekrar deneyin."
            });
        }

        var username = request.Username.Trim().ToLowerInvariant();
        var user = await db.Users
            .SingleOrDefaultAsync(item => item.Username.ToLower() == username, cancellationToken);

        if (user is null ||
            !user.IsActive ||
            !passwordService.Verify(request.Password, user.PasswordHash, user.PasswordSalt))
        {
            loginAttemptService.RecordFailure(ipAddress);
            return Unauthorized(new { message = "Kullanıcı adı veya şifre hatalı." });
        }

        loginAttemptService.RecordSuccess(ipAddress);

        var reason = request.Reason.Trim();
        if (string.IsNullOrWhiteSpace(reason))
            return BadRequest(new { message = "Gerekçe zorunludur." });

        var existingPending = await db.AccessRequests
            .Where(item => item.UserId == user.Id && item.Status == AccessRequestStatus.Pending)
            .SingleOrDefaultAsync(cancellationToken);

        if (existingPending is not null)
        {
            return Ok(new
            {
                message = "Zaten bekleyen bir erişim talebiniz var, onay bekleniyor.",
                existingPending.Id
            });
        }

        var accessRequest = new AccessRequest
        {
            UserId = user.Id,
            Reason = reason,
            Status = AccessRequestStatus.Pending
        };
        db.AccessRequests.Add(accessRequest);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Erişim talebiniz gönderildi, onay bekleniyor.",
            accessRequest.Id
        });
    }

    [Authorize]
    [HttpGet("work-hours-status")]
    public async Task<IActionResult> WorkHoursStatus(CancellationToken cancellationToken)
    {
        var idValue =
            User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            User.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
            User.FindFirstValue("sub");
        if (!Guid.TryParse(idValue, out var userId))
            return Unauthorized(new { message = "Oturum kullanıcısı doğrulanamadı." });

        var evaluation = await workHourAccessService.EvaluateAsync(userId, cancellationToken);
        var minutesRemaining = evaluation.WindowEndsAtUtc is null
            ? (int?)null
            : Math.Max(0, (int)Math.Ceiling((evaluation.WindowEndsAtUtc.Value - DateTime.UtcNow).TotalMinutes));

        return Ok(new
        {
            isAllowed = evaluation.IsAllowed,
            isExempt = evaluation.IsExempt,
            windowEndsAtUtc = evaluation.WindowEndsAtUtc,
            minutesRemaining
        });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var idValue =
            User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            User.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
            User.FindFirstValue("sub");
        if (!Guid.TryParse(idValue, out var userId))
            return Unauthorized(new { message = "Oturum kullanıcısı doğrulanamadı." });

        var user = await db.Users
            .AsNoTracking()
            .Include(item => item.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);

        if (user is null || !user.IsActive)
            return Unauthorized(new { message = "Kullanıcı hesabı pasif veya bulunamadı." });

        var roles = user.UserRoles
            .Select(userRole => userRole.Role.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var authorization = await userAuthorizationService.GetAsync(
            user.Id,
            cancellationToken);
        var permissions = (authorization?.Permissions ?? [])
            .OrderBy(permission => permission)
            .ToArray();

        return Ok(new
        {
            id = user.Id,
            user.Username,
            user.FullName,
            user.Honorific,
            roles,
            permissions
        });
    }

    private string ResolveClientIp()
    {
        var forwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            var first = forwardedFor.Split(',')[0].Trim();
            if (!string.IsNullOrWhiteSpace(first))
                return first;
        }

        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
