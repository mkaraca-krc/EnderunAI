using System.Security.Cryptography;
using System.Text.RegularExpressions;
using EnderunAI.Api.Contracts;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[RequirePermission(PermissionCatalog.Keys.SystemUsersManage)]
[Route("api/user-management")]
public sealed class UserManagementController(
    AppDbContext db,
    PasswordService passwordService,
    ICurrentUserService currentUser) : ControllerBase
{
    private static readonly Regex UsernamePattern =
        new("^[a-zA-Z0-9._-]+$", RegexOptions.Compiled);

    [HttpGet("catalog")]
    public IActionResult GetCatalog()
    {
        return Ok(new
        {
            permissions = PermissionCatalog.Permissions,
            rolePresets = PermissionCatalog.RolePresets
        });
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers(CancellationToken cancellationToken)
    {
        var users = await db.Users
            .AsNoTracking()
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .Include(user => user.Personnel)
            .OrderByDescending(user => user.IsActive)
            .ThenBy(user => user.FullName)
            .ToListAsync(cancellationToken);

        return Ok(users.Select(ToUserResponse));
    }

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser(
        CreateManagedUserRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateUserInput(
            request.Username,
            request.FullName,
            request.RoleName,
            request.AllowedPermissions,
            request.DeniedPermissions);

        if (validation is not null)
            return BadRequest(new { message = validation });

        var personnelValidation = await ValidatePersonnelLinkAsync(
            request.PersonnelId,
            null,
            cancellationToken);
        if (personnelValidation is not null)
            return BadRequest(new { message = personnelValidation });

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
            SecurityStamp = Guid.NewGuid().ToString("N"),
            MustChangePassword = request.MustChangePassword,
            PasswordChangedAtUtc = DateTime.UtcNow,
            IsActive = request.IsActive,
            PersonnelId = request.PersonnelId
        };

        await using var transaction = await db.Database.BeginTransactionAsync(
            cancellationToken);
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        await SyncUserRolesAsync(
            user.Id,
            request.RoleName,
            request.AllowedPermissions,
            request.DeniedPermissions,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        var createdUser = await LoadUserAsync(user.Id, cancellationToken);
        return Ok(new
        {
            message = "Kullanıcı oluşturuldu.",
            temporaryPassword,
            user = ToUserResponse(createdUser!)
        });
    }

    [HttpPut("users/{id:guid}")]
    public async Task<IActionResult> UpdateUser(
        Guid id,
        UpdateManagedUserRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateUserInput(
            request.Username,
            request.FullName,
            request.RoleName,
            request.AllowedPermissions,
            request.DeniedPermissions);

        if (validation is not null)
            return BadRequest(new { message = validation });

        var user = await db.Users.SingleOrDefaultAsync(
            item => item.Id == id,
            cancellationToken);

        if (user is null)
            return NotFound(new { message = "Kullanıcı bulunamadı." });

        var personnelValidation = await ValidatePersonnelLinkAsync(
            request.PersonnelId,
            id,
            cancellationToken);
        if (personnelValidation is not null)
            return BadRequest(new { message = personnelValidation });

        var currentUserId = currentUser.UserId;
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
        user.PersonnelId = request.PersonnelId;
        user.MustChangePassword = request.MustChangePassword;
        user.SecurityStamp = Guid.NewGuid().ToString("N");

        await using var transaction = await db.Database.BeginTransactionAsync(
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await SyncUserRolesAsync(
            id,
            request.RoleName,
            request.AllowedPermissions,
            request.DeniedPermissions,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        var updatedUser = await LoadUserAsync(id, cancellationToken);
        return Ok(new
        {
            message = "Kullanıcı ve yetkileri güncellendi. Yeni yetkiler sonraki girişte etkinleşir.",
            user = ToUserResponse(updatedUser!)
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
        user.PasswordChangedAtUtc = DateTime.UtcNow;
        user.MustChangePassword = request.RequirePasswordChange;
        user.SecurityStamp = Guid.NewGuid().ToString("N");

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Şifre sıfırlandı.",
            temporaryPassword
        });
    }

    private static object ToUserResponse(AppUser user)
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
            roleName = PermissionCatalog.GetPrimaryRole(roleNames) ?? "Rol tanımsız",
            allowedPermissions = roleNames
                .Where(name => name.StartsWith(
                    PermissionCatalog.AllowPrefix,
                    StringComparison.OrdinalIgnoreCase))
                .Select(name => name[PermissionCatalog.AllowPrefix.Length..])
                .OrderBy(name => name),
            deniedPermissions = roleNames
                .Where(name => name.StartsWith(
                    PermissionCatalog.DenyPrefix,
                    StringComparison.OrdinalIgnoreCase))
                .Select(name => name[PermissionCatalog.DenyPrefix.Length..])
                .OrderBy(name => name),
            effectivePermissions = PermissionCatalog.Resolve(roleNames).OrderBy(name => name)
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
            .Include(user => user.Personnel)
            .SingleOrDefaultAsync(user => user.Id == id, cancellationToken);
    }

    private async Task<string?> ValidatePersonnelLinkAsync(
        Guid? personnelId,
        Guid? currentUserId,
        CancellationToken cancellationToken)
    {
        if (personnelId is null)
            return null;

        var personnelExists = await db.Personnel
            .AsNoTracking()
            .AnyAsync(
                personnel => personnel.Id == personnelId && personnel.IsActive,
                cancellationToken);
        if (!personnelExists)
            return "Seçilen aktif personel kaydı bulunamadı.";

        var linkedToAnotherUser = await db.Users
            .AsNoTracking()
            .AnyAsync(
                user =>
                    user.PersonnelId == personnelId &&
                    user.Id != currentUserId,
                cancellationToken);

        return linkedToAnotherUser
            ? "Bu personel kaydı başka bir kullanıcıyla eşleştirilmiş."
            : null;
    }

    private async Task SyncUserRolesAsync(
        Guid userId,
        string roleName,
        IEnumerable<string> allowedPermissions,
        IEnumerable<string> deniedPermissions,
        CancellationToken cancellationToken)
    {
        var allowed = PermissionCatalog.SanitizeOverrides(allowedPermissions);
        var denied = PermissionCatalog.SanitizeOverrides(deniedPermissions)
            .Except(allowed, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var desiredRoleNames = new[] { roleName.Trim() }
            .Concat(allowed.Select(permission =>
                $"{PermissionCatalog.AllowPrefix}{permission}"))
            .Concat(denied.Select(permission =>
                $"{PermissionCatalog.DenyPrefix}{permission}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        await db.UserRoles
            .Where(userRole => userRole.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        var roles = await db.Roles
            .Where(role => desiredRoleNames.Contains(role.Name))
            .ToListAsync(cancellationToken);

        var knownRoleNames = roles
            .Select(role => role.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var missingRoleName in desiredRoleNames.Where(
                     name => !knownRoleNames.Contains(name)))
        {
            var role = new AppRole
            {
                Name = missingRoleName,
                Description = missingRoleName.StartsWith(
                    PermissionCatalog.AllowPrefix,
                    StringComparison.OrdinalIgnoreCase)
                    ? "Kullanıcıya özel ek izin"
                    : missingRoleName.StartsWith(
                        PermissionCatalog.DenyPrefix,
                        StringComparison.OrdinalIgnoreCase)
                        ? "Kullanıcıya özel kısıtlama"
                        : PermissionCatalog.RolePresets
                            .First(item => item.Name.Equals(
                                missingRoleName,
                                StringComparison.OrdinalIgnoreCase))
                            .Description
            };

            db.Roles.Add(role);
            roles.Add(role);
        }

        await db.SaveChangesAsync(cancellationToken);

        db.UserRoles.AddRange(roles.Select(role => new UserRole
        {
            UserId = userId,
            RoleId = role.Id
        }));

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

    private static string? ValidateUserInput(
        string username,
        string fullName,
        string roleName,
        IEnumerable<string> allowedPermissions,
        IEnumerable<string> deniedPermissions)
    {
        if (string.IsNullOrWhiteSpace(username) ||
            username.Trim().Length < 3 ||
            !UsernamePattern.IsMatch(username.Trim()))
        {
            return "Kullanıcı adı en az 3 karakter olmalı; yalnızca harf, rakam, nokta, alt çizgi ve tire içermelidir.";
        }

        if (string.IsNullOrWhiteSpace(fullName))
            return "Ad soyad zorunludur.";

        if (!PermissionCatalog.IsPresetRole(roleName.Trim()))
            return "Geçerli bir görev rolü seçilmelidir.";

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
