using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EnderunAI.Api.Contracts;
using EnderunAI.Api.Data;
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
    IUserAuthorizationService userAuthorizationService) : ControllerBase
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
