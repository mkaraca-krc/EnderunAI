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
    ICurrentUserService currentUser,
    ISecurityAuditService securityAudit) : ControllerBase
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

    [HttpGet("scope-options")]
    public async Task<IActionResult> GetScopeOptions(
        CancellationToken cancellationToken)
    {
        var companies = await db.Companies
            .AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => item.Name)
            .Select(item => new { item.Id, item.Code, item.Name })
            .ToListAsync(cancellationToken);
        var branches = await db.Branches
            .AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => item.Name)
            .Select(item => new
            {
                item.Id,
                item.CompanyId,
                item.Code,
                item.Name
            })
            .ToListAsync(cancellationToken);
        var projects = await db.Projects
            .AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => item.Name)
            .Select(item => new
            {
                item.Id,
                item.CompanyId,
                item.BranchId,
                item.Code,
                item.Name
            })
            .ToListAsync(cancellationToken);

        return Ok(new { companies, branches, projects });
    }

    [HttpGet("roles")]
    public async Task<IActionResult> GetRoles(
        CancellationToken cancellationToken)
    {
        var roles = await db.Roles
            .AsNoTracking()
            .Where(role => !role.Name.StartsWith(PermissionCatalog.AllowPrefix) &&
                           !role.Name.StartsWith(PermissionCatalog.DenyPrefix))
            .OrderBy(role => role.Name)
            .Select(role => new
            {
                role.Id,
                role.Name,
                role.Description,
                permissions = role.RolePermissions
                    .Select(item => item.Permission.Key)
                    .OrderBy(item => item),
                userCount = role.UserRoles.Count
            })
            .ToListAsync(cancellationToken);

        return Ok(roles);
    }

    [HttpGet("audit-events")]
    public async Task<IActionResult> GetAuditEvents(
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 500);
        var events = await db.SecurityAuditEvents
            .AsNoTracking()
            .OrderByDescending(item => item.OccurredAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
        return Ok(events);
    }

    [HttpPut("roles/{id:guid}/permissions")]
    public async Task<IActionResult> UpdateRolePermissions(
        Guid id,
        UpdateManagedRolePermissionsRequest request,
        CancellationToken cancellationToken)
    {
        var role = await db.Roles
            .Include(item => item.RolePermissions)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (role is null)
            return NotFound(new { message = "Rol bulunamadı." });
        if (role.Name.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message = "Admin rolünün tam yetkisi değiştirilemez."
            });
        }

        var permissionKeys = PermissionCatalog
            .SanitizeOverrides(request.Permissions);
        if (permissionKeys.Length == 0 ||
            permissionKeys.Length != request.Permissions
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count())
        {
            return BadRequest(new
            {
                message = "Rol için en az bir geçerli yetki seçilmelidir."
            });
        }

        var permissions = await db.Permissions
            .Where(item => permissionKeys.Contains(item.Key))
            .ToListAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(
            cancellationToken);
        db.RolePermissions.RemoveRange(role.RolePermissions);
        db.RolePermissions.AddRange(permissions.Select(permission =>
            new RolePermission
            {
                RoleId = role.Id,
                PermissionId = permission.Id
            }));

        var affectedUsers = await db.Users
            .Where(user => user.UserRoles.Any(item => item.RoleId == role.Id))
            .ToListAsync(cancellationToken);
        foreach (var user in affectedUsers)
            user.SecurityStamp = Guid.NewGuid().ToString("N");

        await db.SaveChangesAsync(cancellationToken);
        await securityAudit.WriteAsync(
            "role.permissions.updated",
            nameof(AppRole),
            role.Id,
            new
            {
                role.Name,
                PermissionKeys = permissionKeys,
                AffectedUserIds = affectedUsers.Select(user => user.Id)
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Ok(new
        {
            message = "Rol yetkileri güncellendi; etkilenen oturumlar kapatıldı.",
            role.Id,
            role.Name,
            permissions = permissionKeys
        });
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers(CancellationToken cancellationToken)
    {
        var users = await db.Users
            .AsNoTracking()
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .ThenInclude(role => role.RolePermissions)
            .ThenInclude(rolePermission => rolePermission.Permission)
            .Include(user => user.PermissionOverrides)
            .ThenInclude(permissionOverride => permissionOverride.Permission)
            .Include(user => user.DataScopes)
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

        var (dataScopes, dataScopeError) = await NormalizeDataScopesAsync(
            request.RoleName,
            request.PersonnelId,
            request.DataScopes,
            cancellationToken);
        if (dataScopeError is not null)
            return BadRequest(new { message = dataScopeError });

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
        await SyncUserDataScopesAsync(
            user.Id,
            dataScopes,
            cancellationToken);

        await securityAudit.WriteAsync(
            "user.created",
            nameof(AppUser),
            user.Id,
            new
            {
                user.Username,
                user.PersonnelId,
                RoleName = request.RoleName,
                AllowedPermissions = request.AllowedPermissions,
                DeniedPermissions = request.DeniedPermissions,
                DataScopes = dataScopes
            },
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

        var (dataScopes, dataScopeError) = await NormalizeDataScopesAsync(
            request.RoleName,
            request.PersonnelId,
            request.DataScopes,
            cancellationToken);
        if (dataScopeError is not null)
            return BadRequest(new { message = dataScopeError });

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
        await SyncUserDataScopesAsync(
            id,
            dataScopes,
            cancellationToken);

        await securityAudit.WriteAsync(
            "user.updated",
            nameof(AppUser),
            user.Id,
            new
            {
                user.Username,
                user.PersonnelId,
                user.IsActive,
                RoleName = request.RoleName,
                AllowedPermissions = request.AllowedPermissions,
                DeniedPermissions = request.DeniedPermissions,
                DataScopes = dataScopes
            },
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
        await securityAudit.WriteAsync(
            "user.password.reset",
            nameof(AppUser),
            user.Id,
            new
            {
                user.Username,
                request.RequirePasswordChange
            },
            cancellationToken);

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
            allowedPermissions = user.PermissionOverrides
                .Where(item => item.Effect == PermissionOverrideEffect.Allow)
                .Select(item => item.Permission.Key)
                .OrderBy(name => name),
            deniedPermissions = user.PermissionOverrides
                .Where(item => item.Effect == PermissionOverrideEffect.Deny)
                .Select(item => item.Permission.Key)
                .OrderBy(name => name),
            effectivePermissions = ResolvePermissions(user)
                .OrderBy(item => item),
            dataScopes = user.DataScopes
                .Where(item => item.IsActive)
                .OrderBy(item => item.ScopeType)
                .Select(item => new
                {
                    item.ScopeType,
                    item.CompanyId,
                    item.BranchId,
                    item.ProjectId
                })
        };
    }

    private static IReadOnlyCollection<string> ResolvePermissions(AppUser user)
    {
        var permissions = user.UserRoles
            .SelectMany(item => item.Role.RolePermissions)
            .Select(item => item.Permission.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        permissions.UnionWith(
            user.PermissionOverrides
                .Where(item => item.Effect == PermissionOverrideEffect.Allow)
                .Select(item => item.Permission.Key));
        permissions.ExceptWith(
            user.PermissionOverrides
                .Where(item => item.Effect == PermissionOverrideEffect.Deny)
                .Select(item => item.Permission.Key));
        return permissions.OrderBy(item => item).ToArray();
    }

    private async Task<AppUser?> LoadUserAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return await db.Users
            .AsNoTracking()
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .ThenInclude(role => role.RolePermissions)
            .ThenInclude(rolePermission => rolePermission.Permission)
            .Include(user => user.PermissionOverrides)
            .ThenInclude(permissionOverride => permissionOverride.Permission)
            .Include(user => user.DataScopes)
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

        await db.UserRoles
            .Where(userRole => userRole.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        var normalizedRoleName = roleName.Trim();
        var role = await db.Roles.SingleOrDefaultAsync(
            item => item.Name.ToLower() == normalizedRoleName.ToLower(),
            cancellationToken);
        if (role is null)
        {
            role = new AppRole
            {
                Name = normalizedRoleName,
                Description = PermissionCatalog.RolePresets
                    .First(item => item.Name.Equals(
                        normalizedRoleName,
                        StringComparison.OrdinalIgnoreCase))
                    .Description
            };
            db.Roles.Add(role);
            await db.SaveChangesAsync(cancellationToken);
        }

        db.UserRoles.Add(new UserRole
        {
            UserId = userId,
            RoleId = role.Id
        });

        await db.UserPermissionOverrides
            .Where(item => item.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        var overrideEffects = allowed
            .Select(permission => new
            {
                Permission = permission,
                Effect = PermissionOverrideEffect.Allow
            })
            .Concat(denied.Select(permission => new
            {
                Permission = permission,
                Effect = PermissionOverrideEffect.Deny
            }))
            .ToDictionary(
                item => item.Permission,
                item => item.Effect,
                StringComparer.OrdinalIgnoreCase);
        var overrideKeys = overrideEffects.Keys.ToArray();
        var permissions = await db.Permissions
            .Where(item => overrideKeys.Contains(item.Key))
            .ToListAsync(cancellationToken);
        db.UserPermissionOverrides.AddRange(
            permissions.Select(permission => new UserPermissionOverride
            {
                UserId = userId,
                PermissionId = permission.Id,
                Effect = overrideEffects[permission.Key],
                UpdatedAtUtc = DateTime.UtcNow,
                UpdatedByUserId = currentUser.UserId
            }));

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SyncUserDataScopesAsync(
        Guid userId,
        IReadOnlyCollection<NormalizedDataScope> scopes,
        CancellationToken cancellationToken)
    {
        await db.UserDataScopes
            .Where(item => item.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        db.UserDataScopes.AddRange(scopes.Select(scope => new UserDataScope
        {
            UserId = userId,
            ScopeType = scope.ScopeType,
            CompanyId = scope.CompanyId,
            BranchId = scope.BranchId,
            ProjectId = scope.ProjectId,
            CreatedByUserId = currentUser.UserId
        }));
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<(IReadOnlyCollection<NormalizedDataScope> Scopes, string? Error)>
        NormalizeDataScopesAsync(
            string roleName,
            Guid? personnelId,
            IEnumerable<ManagedUserDataScopeRequest>? requests,
            CancellationToken cancellationToken)
    {
        var requested = (requests ?? []).ToArray();
        if (requested.Length == 0)
        {
            if (roleName.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                roleName.Equals("Genel Müdür", StringComparison.OrdinalIgnoreCase))
            {
                return ([new NormalizedDataScope(DataScopeType.All, null, null, null)], null);
            }

            if (personnelId.HasValue)
            {
                var personnel = await db.Personnel
                    .AsNoTracking()
                    .SingleAsync(item => item.Id == personnelId, cancellationToken);
                return personnel.BranchId.HasValue
                    ? ([new NormalizedDataScope(
                        DataScopeType.Branch,
                        personnel.CompanyId,
                        personnel.BranchId,
                        null)], null)
                    : ([new NormalizedDataScope(
                        DataScopeType.Company,
                        personnel.CompanyId,
                        null,
                        null)], null);
            }

            return (
                Array.Empty<NormalizedDataScope>(),
                "Operasyonel kullanıcı için en az bir şirket, şube veya proje kapsamı seçilmelidir.");
        }

        var normalized = new List<NormalizedDataScope>();
        foreach (var request in requested)
        {
            switch (request.ScopeType)
            {
                case DataScopeType.All:
                    if (!roleName.Equals("Admin", StringComparison.OrdinalIgnoreCase) &&
                        !roleName.Equals(
                            "Genel Müdür",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return (
                            Array.Empty<NormalizedDataScope>(),
                            "Tüm şirketler kapsamı yalnızca Admin veya Genel Müdür rolüne verilebilir.");
                    }

                    normalized.Add(new NormalizedDataScope(
                        DataScopeType.All,
                        null,
                        null,
                        null));
                    break;

                case DataScopeType.Company:
                    if (request.CompanyId is not Guid companyId ||
                        !await db.Companies.AsNoTracking().AnyAsync(
                            item => item.Id == companyId && item.IsActive,
                            cancellationToken))
                    {
                        return (
                            Array.Empty<NormalizedDataScope>(),
                            "Geçerli ve aktif bir şirket kapsamı seçilmelidir.");
                    }

                    normalized.Add(new NormalizedDataScope(
                        DataScopeType.Company,
                        companyId,
                        null,
                        null));
                    break;

                case DataScopeType.Branch:
                    if (request.BranchId is not Guid branchId)
                    {
                        return (
                            Array.Empty<NormalizedDataScope>(),
                            "Şube kapsamı için şube seçilmelidir.");
                    }

                    var branch = await db.Branches
                        .AsNoTracking()
                        .SingleOrDefaultAsync(
                            item => item.Id == branchId && item.IsActive,
                            cancellationToken);
                    if (branch is null)
                    {
                        return (
                            Array.Empty<NormalizedDataScope>(),
                            "Seçilen şube bulunamadı veya pasif.");
                    }

                    normalized.Add(new NormalizedDataScope(
                        DataScopeType.Branch,
                        branch.CompanyId,
                        branch.Id,
                        null));
                    break;

                case DataScopeType.Project:
                    if (request.ProjectId is not Guid projectId)
                    {
                        return (
                            Array.Empty<NormalizedDataScope>(),
                            "Proje kapsamı için proje seçilmelidir.");
                    }

                    var project = await db.Projects
                        .AsNoTracking()
                        .SingleOrDefaultAsync(
                            item => item.Id == projectId && item.IsActive,
                            cancellationToken);
                    if (project is null)
                    {
                        return (
                            Array.Empty<NormalizedDataScope>(),
                            "Seçilen proje bulunamadı veya pasif.");
                    }

                    normalized.Add(new NormalizedDataScope(
                        DataScopeType.Project,
                        project.CompanyId,
                        project.BranchId,
                        project.Id));
                    break;

                default:
                    return (
                        Array.Empty<NormalizedDataScope>(),
                        "Geçersiz veri kapsamı türü.");
            }
        }

        return (
            normalized
                .Distinct()
                .ToArray(),
            null);
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

    private sealed record NormalizedDataScope(
        DataScopeType ScopeType,
        Guid? CompanyId,
        Guid? BranchId,
        Guid? ProjectId);
}
