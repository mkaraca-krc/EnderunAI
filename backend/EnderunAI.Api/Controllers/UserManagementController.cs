using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using EnderunAI.Api.Contracts;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin,Genel Müdür")]
[Route("api/user-management")]
public sealed class UserManagementController(
    AppDbContext db,
    PasswordService passwordService,
    IUserAuthorizationService userAuthorizationService) : ControllerBase
{
    private static readonly Regex UsernamePattern =
        new("^[a-zA-Z0-9._-]+$", RegexOptions.Compiled);

    private sealed record OverrideRow(
        Guid UserId,
        string PermissionKey,
        PermissionOverrideEffect Effect);

    [HttpGet("catalog")]
    public async Task<IActionResult> GetCatalog(CancellationToken cancellationToken)
    {
        var roles = await db.Roles
            .AsNoTracking()
            .OrderBy(role => role.Name)
            .Select(role => new { role.Name, role.Description })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            permissions = PermissionCatalog.Permissions,
            roles
        });
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers(CancellationToken cancellationToken)
    {
        var users = await db.Users
            .AsNoTracking()
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .OrderByDescending(user => user.IsActive)
            .ThenBy(user => user.FullName)
            .ToListAsync(cancellationToken);

        var overridesByUser = await db.UserPermissionOverrides
            .AsNoTracking()
            .Where(item => users.Select(u => u.Id).Contains(item.UserId))
            .Select(item => new OverrideRow(
                item.UserId,
                item.Permission.Key,
                item.Effect))
            .ToListAsync(cancellationToken);

        var overridesLookup = overridesByUser
            .GroupBy(item => item.UserId)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray());

        var responses = new List<object>();
        foreach (var user in users)
        {
            var authorization = await userAuthorizationService.GetAsync(
                user.Id,
                cancellationToken);

            responses.Add(ToUserResponse(
                user,
                authorization?.Permissions ?? [],
                overridesLookup.GetValueOrDefault(user.Id, [])));
        }

        return Ok(responses);
    }

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser(
        CreateManagedUserRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateUserInputAsync(
            request.Username,
            request.FullName,
            request.RoleName,
            request.AllowedPermissions,
            request.DeniedPermissions,
            cancellationToken);

        if (validation is not null)
            return BadRequest(new { message = validation });

        var username = NormalizeUsername(request.Username);
        if (await db.Users.AnyAsync(
                user => user.Username.ToLower() == username,
                cancellationToken))
        {
            return Conflict(new { message = "Bu kullanıcı adı zaten kullanılıyor." });
        }

        var temporaryPassword = string.IsNullOrWhiteSpace(request.Password)
            ? GenerateTemporaryPassword()
            : request.Password!;

        if (temporaryPassword.Length < 10)
            return BadRequest(new { message = "Şifre en az 10 karakter olmalıdır." });

        var password = passwordService.Hash(temporaryPassword);
        var user = new AppUser
        {
            Username = username,
            FullName = request.FullName.Trim(),
            Email = NormalizeOptional(request.Email),
            PasswordHash = password.Hash,
            PasswordSalt = password.Salt,
            IsActive = request.IsActive
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        await SyncUserRoleAsync(
            user.Id,
            request.RoleName,
            request.AllowedPermissions,
            request.DeniedPermissions,
            cancellationToken);

        var createdUser = await LoadUserAsync(user.Id, cancellationToken);
        var authorization = await userAuthorizationService.GetAsync(user.Id, cancellationToken);
        var overrides = await LoadOverridesAsync(user.Id, cancellationToken);

        return Ok(new
        {
            message = "Kullanıcı oluşturuldu.",
            temporaryPassword,
            user = ToUserResponse(createdUser!, authorization?.Permissions ?? [], overrides)
        });
    }

    [HttpPut("users/{id:guid}")]
    public async Task<IActionResult> UpdateUser(
        Guid id,
        UpdateManagedUserRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateUserInputAsync(
            request.Username,
            request.FullName,
            request.RoleName,
            request.AllowedPermissions,
            request.DeniedPermissions,
            cancellationToken);

        if (validation is not null)
            return BadRequest(new { message = validation });

        var user = await db.Users.SingleOrDefaultAsync(
            item => item.Id == id,
            cancellationToken);

        if (user is null)
            return NotFound(new { message = "Kullanıcı bulunamadı." });

        var currentUserId = GetCurrentUserId();
        if (currentUserId == id &&
            (!request.IsActive ||
             !string.Equals(request.RoleName, "Admin", StringComparison.OrdinalIgnoreCase)))
        {
            return BadRequest(new
            {
                message = "Kendi aktif Admin yetkinizi kaldıramazsınız."
            });
        }

        if (await RemovingLastAdminAsync(
                id,
                request.RoleName,
                request.IsActive,
                cancellationToken))
        {
            return BadRequest(new
            {
                message = "Sistemde en az bir aktif Admin kullanıcısı bulunmalıdır."
            });
        }

        var username = NormalizeUsername(request.Username);
        if (await db.Users.AnyAsync(
                item => item.Id != id && item.Username.ToLower() == username,
                cancellationToken))
        {
            return Conflict(new { message = "Bu kullanıcı adı zaten kullanılıyor." });
        }

        user.Username = username;
        user.FullName = request.FullName.Trim();
        user.Email = NormalizeOptional(request.Email);
        user.IsActive = request.IsActive;

        await db.SaveChangesAsync(cancellationToken);
        await SyncUserRoleAsync(
            id,
            request.RoleName,
            request.AllowedPermissions,
            request.DeniedPermissions,
            cancellationToken);

        var updatedUser = await LoadUserAsync(id, cancellationToken);
        var authorization = await userAuthorizationService.GetAsync(id, cancellationToken);
        var overrides = await LoadOverridesAsync(id, cancellationToken);

        return Ok(new
        {
            message = "Kullanıcı ve yetkileri güncellendi. Değişiklikler bir sonraki istekte etkindir.",
            user = ToUserResponse(updatedUser!, authorization?.Permissions ?? [], overrides)
        });
    }

    [HttpPost("users/{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(
        Guid id,
        ResetManagedUserPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(
            item => item.Id == id,
            cancellationToken);

        if (user is null)
            return NotFound(new { message = "Kullanıcı bulunamadı." });

        var temporaryPassword = string.IsNullOrWhiteSpace(request.NewPassword)
            ? GenerateTemporaryPassword()
            : request.NewPassword!;

        if (temporaryPassword.Length < 10)
            return BadRequest(new { message = "Şifre en az 10 karakter olmalıdır." });

        var password = passwordService.Hash(temporaryPassword);
        user.PasswordHash = password.Hash;
        user.PasswordSalt = password.Salt;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Şifre sıfırlandı.",
            temporaryPassword
        });
    }

    private static object ToUserResponse(
        AppUser user,
        IReadOnlyCollection<string> effectivePermissions,
        IReadOnlyCollection<OverrideRow> overrides)
    {
        var roleNames = user.UserRoles
            .Select(userRole => userRole.Role.Name)
            .ToArray();

        return new
        {
            user.Id,
            user.Username,
            user.FullName,
            user.Email,
            user.IsActive,
            user.CreatedAtUtc,
            user.LastLoginAtUtc,
            roleName = roleNames.FirstOrDefault() ?? "Rol tanımsız",
            allowedPermissions = overrides
                .Where(item => item.Effect == PermissionOverrideEffect.Allow)
                .Select(item => item.PermissionKey)
                .OrderBy(name => name),
            deniedPermissions = overrides
                .Where(item => item.Effect == PermissionOverrideEffect.Deny)
                .Select(item => item.PermissionKey)
                .OrderBy(name => name),
            effectivePermissions = effectivePermissions.OrderBy(name => name)
        };
    }

    private async Task<AppUser?> LoadUserAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return await db.Users
            .AsNoTracking()
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .SingleOrDefaultAsync(user => user.Id == id, cancellationToken);
    }

    private async Task<List<OverrideRow>> LoadOverridesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await db.UserPermissionOverrides
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .Select(item => new OverrideRow(
                item.UserId,
                item.Permission.Key,
                item.Effect))
            .ToListAsync(cancellationToken);
    }

    private async Task SyncUserRoleAsync(
        Guid userId,
        string roleName,
        IEnumerable<string> allowedPermissions,
        IEnumerable<string> deniedPermissions,
        CancellationToken cancellationToken)
    {
        var trimmedRoleName = roleName.Trim();
        var role = await db.Roles.SingleAsync(
            item => item.Name == trimmedRoleName,
            cancellationToken);

        await db.UserRoles
            .Where(userRole => userRole.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        db.UserRoles.Add(new UserRole
        {
            UserId = userId,
            RoleId = role.Id
        });

        await db.UserPermissionOverrides
            .Where(item => item.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        var allowed = PermissionCatalog.SanitizeOverrides(allowedPermissions);
        var denied = PermissionCatalog.SanitizeOverrides(deniedPermissions)
            .Except(allowed, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var overrideKeys = allowed
            .Select(key => (Key: key, Effect: PermissionOverrideEffect.Allow))
            .Concat(denied.Select(key => (Key: key, Effect: PermissionOverrideEffect.Deny)))
            .ToArray();

        if (overrideKeys.Length > 0)
        {
            var permissionIds = await db.Permissions
                .Where(item => overrideKeys.Select(x => x.Key).Contains(item.Key))
                .ToDictionaryAsync(item => item.Key, item => item.Id, cancellationToken);

            foreach (var (key, effect) in overrideKeys)
            {
                if (!permissionIds.TryGetValue(key, out var permissionId))
                    continue;

                db.UserPermissionOverrides.Add(new UserPermissionOverride
                {
                    UserId = userId,
                    PermissionId = permissionId,
                    Effect = effect,
                    CreatedByUserId = GetCurrentUserId()
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> RemovingLastAdminAsync(
        Guid userId,
        string requestedRole,
        bool requestedActive,
        CancellationToken cancellationToken)
    {
        var currentlyAdmin = await db.UserRoles.AnyAsync(
            userRole =>
                userRole.UserId == userId &&
                userRole.Role.Name == "Admin",
            cancellationToken);

        if (!currentlyAdmin ||
            (requestedActive &&
             string.Equals(requestedRole, "Admin", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return !await db.Users.AnyAsync(
            user =>
                user.Id != userId &&
                user.IsActive &&
                user.UserRoles.Any(userRole => userRole.Role.Name == "Admin"),
            cancellationToken);
    }

    private Guid? GetCurrentUserId()
    {
        var value =
            User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        return Guid.TryParse(value, out var id) ? id : null;
    }

    private async Task<string?> ValidateUserInputAsync(
        string username,
        string fullName,
        string roleName,
        IEnumerable<string> allowedPermissions,
        IEnumerable<string> deniedPermissions,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username) ||
            username.Trim().Length < 3 ||
            !UsernamePattern.IsMatch(username.Trim()))
        {
            return "Kullanıcı adı en az 3 karakter olmalı; yalnızca harf, rakam, nokta, alt çizgi ve tire içermelidir.";
        }

        if (string.IsNullOrWhiteSpace(fullName))
            return "Ad soyad zorunludur.";

        if (!await db.Roles.AnyAsync(
                role => role.Name == roleName.Trim(),
                cancellationToken))
        {
            return "Geçerli bir görev rolü seçilmelidir.";
        }

        var invalidPermissions = allowedPermissions
            .Concat(deniedPermissions)
            .Where(permission => !PermissionCatalog.IsKnownPermission(permission))
            .Distinct()
            .ToArray();

        return invalidPermissions.Length > 0
            ? "Bilinmeyen bir yetki seçildi."
            : null;
    }

    private static string NormalizeUsername(string value) =>
        value.Trim().ToLowerInvariant();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string GenerateTemporaryPassword()
    {
        const string alphabet =
            "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%";
        var bytes = RandomNumberGenerator.GetBytes(16);
        return new string(bytes.Select(value => alphabet[value % alphabet.Length]).ToArray());
    }
}
