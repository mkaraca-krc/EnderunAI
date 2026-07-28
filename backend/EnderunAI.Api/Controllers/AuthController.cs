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
            .Include(item => item.Personnel)
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
                user.MustChangePassword,
                personnelId = user.PersonnelId,
                roles = visibleRoles,
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
            .Include(item => item.Personnel)
            .SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);

        if (user is null || !user.IsActive)
            return Unauthorized(new { message = "Kullanıcı hesabı pasif veya bulunamadı." });

        var roleNames = user.UserRoles
            .Select(userRole => userRole.Role.Name)
            .ToArray();
        var roles = roleNames
            .Where(PermissionCatalog.IsPresetRole)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var permissions = PermissionCatalog.Resolve(roleNames)
            .OrderBy(permission => permission)
            .ToArray();

        return Ok(new
        {
            id = user.Id,
            user.Username,
            user.FullName,
            user.MustChangePassword,
            personnel = user.Personnel is null
                ? null
                : new
                {
                    user.Personnel.Id,
                    user.Personnel.EmployeeNumber,
                    user.Personnel.FullName,
                    user.Personnel.CompanyId,
                    user.Personnel.BranchId
                },
            roles,
            permissions
        });
    }
}
