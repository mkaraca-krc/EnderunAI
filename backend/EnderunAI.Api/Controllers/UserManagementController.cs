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
    [RequirePermission(PermissionCatalog.Keys.UserManagementView)]
    public async Task<IActionResult> GetCatalog(CancellationToken cancellationToken)
    {
        var roles = await db.Roles
            .AsNoTracking()
            .OrderBy(role => role.Name)
            .Select(role => new
            {
                role.Name,
                role.Description,
                DataScopePolicy = (int)role.DataScopePolicy
            })
            .ToListAsync(cancellationToken);

        var sites = await db.ProjectSites
            .AsNoTracking()
            .Where(site => site.IsActive)
            .OrderBy(site => site.Project.Code)
            .ThenBy(site => site.Code)
            .Select(site => new
            {
                site.Id,
                site.Code,
                site.Name,
                ProjectCode = site.Project.Code,
                ProjectName = site.Project.Name
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            permissions = PermissionCatalog.Permissions,
            roles,
            sites
        });
    }

    [HttpGet("users")]
    [RequirePermission(PermissionCatalog.Keys.UserManagementView)]
    public async Task<IActionResult> GetUsers(CancellationToken cancellationToken)
    {
        var users = await db.Users
            .AsNoTracking()
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .OrderByDescending(user => user.IsActive)
            .ThenBy(user => user.FullName)
            .ToListAsync(cancellationToken);

        var userIds = users.Select(u => u.Id).ToArray();

        var overridesByUser = await db.UserPermissionOverrides
            .AsNoTracking()
            .Where(item => userIds.Contains(item.UserId))
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

        var siteAssignments = await db.UserDataScopes
            .AsNoTracking()
            .Where(item =>
                userIds.Contains(item.UserId) &&
                item.ScopeType == DataScopeType.Site &&
                item.ProjectSiteId.HasValue)
            .Select(item => new
            {
                item.UserId,
                item.ProjectSiteId,
                SiteCode = item.ProjectSite!.Code,
                SiteName = item.ProjectSite!.Name
            })
            .ToListAsync(cancellationToken);

        var sitesLookup = siteAssignments
            .GroupBy(item => item.UserId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(x => new { x.ProjectSiteId, x.SiteCode, x.SiteName }).ToArray());

        var responses = new List<object>();
        foreach (var user in users)
        {
            var authorization = await userAuthorizationService.GetAsync(
                user.Id,
                cancellationToken);

            responses.Add(ToUserResponse(
                user,
                authorization?.Permissions ?? [],
                overridesLookup.GetValueOrDefault(user.Id, []),
                sitesLookup.GetValueOrDefault(user.Id, [])));
        }

        return Ok(responses);
    }

    [HttpPost("users")]
    [RequirePermission(PermissionCatalog.Keys.UserManagementCreate)]
    public async Task<IActionResult> CreateUser(
        CreateManagedUserRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateUserInputAsync(
            request.Username,
            request.FullName,
            request.RoleNames,
            request.ProjectSiteIds,
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
            IsActive = request.IsActive,
            WorkHoursExempt = request.WorkHoursExempt
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        await SyncUserRolesAsync(
            user.Id,
            request.RoleNames,
            request.ProjectSiteIds,
            request.AllowedPermissions,
            request.DeniedPermissions,
            cancellationToken);

        var createdUser = await LoadUserAsync(user.Id, cancellationToken);
        var authorization = await userAuthorizationService.GetAsync(user.Id, cancellationToken);
        var overrides = await LoadOverridesAsync(user.Id, cancellationToken);
        var sites = await LoadSiteAssignmentsAsync(user.Id, cancellationToken);

        return Ok(new
        {
            message = "Kullanıcı oluşturuldu.",
            temporaryPassword,
            user = ToUserResponse(createdUser!, authorization?.Permissions ?? [], overrides, sites)
        });
    }

    [HttpPut("users/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.UserManagementEdit)]
    public async Task<IActionResult> UpdateUser(
        Guid id,
        UpdateManagedUserRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateUserInputAsync(
            request.Username,
            request.FullName,
            request.RoleNames,
            request.ProjectSiteIds,
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
        var keepsAdmin = request.RoleNames.Contains("Admin", StringComparer.OrdinalIgnoreCase);
        if (currentUserId == id && (!request.IsActive || !keepsAdmin))
        {
            return BadRequest(new
            {
                message = "Kendi aktif Admin yetkinizi kaldıramazsınız."
            });
        }

        if (await RemovingLastAdminAsync(
                id,
                keepsAdmin,
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
        user.WorkHoursExempt = request.WorkHoursExempt;

        await db.SaveChangesAsync(cancellationToken);
        await SyncUserRolesAsync(
            id,
            request.RoleNames,
            request.ProjectSiteIds,
            request.AllowedPermissions,
            request.DeniedPermissions,
            cancellationToken);

        var updatedUser = await LoadUserAsync(id, cancellationToken);
        var authorization = await userAuthorizationService.GetAsync(id, cancellationToken);
        var overrides = await LoadOverridesAsync(id, cancellationToken);
        var sites = await LoadSiteAssignmentsAsync(id, cancellationToken);

        return Ok(new
        {
            message = "Kullanıcı ve yetkileri güncellendi. Değişiklikler bir sonraki istekte etkindir.",
            user = ToUserResponse(updatedUser!, authorization?.Permissions ?? [], overrides, sites)
        });
    }

    [HttpPost("users/{id:guid}/reset-password")]
    [RequirePermission(PermissionCatalog.Keys.UserManagementEdit)]
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
        IReadOnlyCollection<OverrideRow> overrides,
        IReadOnlyCollection<dynamic> siteAssignments)
    {
        var roleNames = user.UserRoles
            .Select(userRole => userRole.Role.Name)
            .OrderBy(name => name)
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
            user.WorkHoursExempt,
            roleNames,
            roleName = roleNames.FirstOrDefault() ?? "Rol tanımsız",
            projectSiteIds = siteAssignments.Select(x => (Guid)x.ProjectSiteId).ToArray(),
            projectSites = siteAssignments.Select(x => new
            {
                id = (Guid)x.ProjectSiteId,
                code = (string)x.SiteCode,
                name = (string)x.SiteName
            }),
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

    private async Task<List<dynamic>> LoadSiteAssignmentsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var items = await db.UserDataScopes
            .AsNoTracking()
            .Where(item =>
                item.UserId == userId &&
                item.ScopeType == DataScopeType.Site &&
                item.ProjectSiteId.HasValue)
            .Select(item => new
            {
                ProjectSiteId = item.ProjectSiteId!.Value,
                SiteCode = item.ProjectSite!.Code,
                SiteName = item.ProjectSite!.Name
            })
            .ToListAsync(cancellationToken);

        return items.Cast<dynamic>().ToList();
    }

    private async Task SyncUserRolesAsync(
        Guid userId,
        IReadOnlyCollection<string> roleNames,
        IReadOnlyCollection<Guid> projectSiteIds,
        IEnumerable<string> allowedPermissions,
        IEnumerable<string> deniedPermissions,
        CancellationToken cancellationToken)
    {
        var trimmedRoleNames = roleNames
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var roles = await db.Roles
            .Where(role => trimmedRoleNames.Contains(role.Name))
            .ToListAsync(cancellationToken);

        await db.UserRoles
            .Where(userRole => userRole.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        db.UserRoles.AddRange(roles.Select(role => new UserRole
        {
            UserId = userId,
            RoleId = role.Id
        }));

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

        // Veri kapsamı: seçilen rollerden biri SiteOnly ise kullanıcı
        // sadece seçilen şantiyeleri görür; aksi halde kısıtsız (AllScope).
        // ÖNEMLİ: SiteOnly bir rol seçilip hiç şantiye atanmazsa fail-closed
        // davranılır — hiç UserDataScope satırı eklenmez, yani kullanıcı
        // hiçbir veriyi göremez (CurrentDataScopeSnapshot boş SiteIds/
        // ProjectIds ile HasGlobalAccess=false döner). Önceden bu durumda
        // yanlışlıkla AllScope (kısıtsız erişim) veriliyordu.
        await db.UserDataScopes
            .Where(item => item.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        var requiresSiteScope = roles.Any(
            role => role.DataScopePolicy == RoleDataScopePolicy.SiteOnly);

        if (requiresSiteScope)
        {
            foreach (var siteId in projectSiteIds.Distinct())
            {
                db.UserDataScopes.Add(new UserDataScope
                {
                    UserId = userId,
                    ScopeType = DataScopeType.Site,
                    ProjectSiteId = siteId
                });
            }
        }
        else
        {
            db.UserDataScopes.Add(new UserDataScope
            {
                UserId = userId,
                ScopeType = DataScopeType.All
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> RemovingLastAdminAsync(
        Guid userId,
        bool keepsAdmin,
        bool requestedActive,
        CancellationToken cancellationToken)
    {
        var currentlyAdmin = await db.UserRoles.AnyAsync(
            userRole =>
                userRole.UserId == userId &&
                userRole.Role.Name == "Admin",
            cancellationToken);

        if (!currentlyAdmin || (requestedActive && keepsAdmin))
            return false;

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
        IReadOnlyCollection<string> roleNames,
        IReadOnlyCollection<Guid> projectSiteIds,
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

        if (roleNames.Count == 0)
            return "En az bir rol seçilmelidir.";

        var trimmedRoleNames = roleNames
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var matchedRoles = await db.Roles
            .Where(role => trimmedRoleNames.Contains(role.Name))
            .Select(role => new { role.Name, role.DataScopePolicy })
            .ToListAsync(cancellationToken);

        if (matchedRoles.Count != trimmedRoleNames.Length)
            return "Geçerli görev rolleri seçilmelidir.";

        if (matchedRoles.Any(role => role.DataScopePolicy == RoleDataScopePolicy.SiteOnly) &&
            projectSiteIds.Count == 0)
        {
            return "Bu rol(ler) için en az bir şantiye ataması zorunludur.";
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
