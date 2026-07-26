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
    TokenService tokenService) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
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
            return Unauthorized(new { message = "Kullanıcı adı veya şifre hatalı." });
        }

        user.LastLoginAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var roleNames = user.UserRoles
            .Select(userRole => userRole.Role.Name)
            .ToArray();
        var visibleRoles = roleNames
            .Where(PermissionCatalog.IsPresetRole)
            .ToArray();
        var permissions = PermissionCatalog.Resolve(roleNames)
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
                roles = visibleRoles,
                permissions
            }
        });
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        var roles = User.FindAll(ClaimTypes.Role)
            .Select(claim => claim.Value)
            .Where(PermissionCatalog.IsPresetRole)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var permissions = User.FindAll("permissions")
            .Select(claim => claim.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(permission => permission)
            .ToArray();

        return Ok(new
        {
            id = User.FindFirstValue(ClaimTypes.NameIdentifier),
            username = User.Identity?.Name,
            fullName = User.FindFirstValue("full_name"),
            roles,
            permissions
        });
    }
}
