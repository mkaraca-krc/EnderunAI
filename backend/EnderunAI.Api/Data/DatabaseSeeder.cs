using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Data;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(
        AppDbContext db,
        PasswordService passwordService,
        IConfiguration configuration)
    {
        await SeedAuthorizationCatalogAsync(db);

        var adminRole = await db.Roles.SingleAsync(role => role.Name == "Admin");

        var username = Environment.GetEnvironmentVariable("SEED_ADMIN_USERNAME");
        var password = Environment.GetEnvironmentVariable("SEED_ADMIN_PASSWORD");
        var fullName =
            Environment.GetEnvironmentVariable("SEED_ADMIN_FULLNAME") ??
            "Mehmet Karacabey";

        if (string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        username = username.Trim().ToLowerInvariant();
        var user = await db.Users
            .Include(item => item.UserRoles)
            .SingleOrDefaultAsync(item => item.Username == username);

        if (user is null)
        {
            var result = passwordService.Hash(password);
            user = new AppUser
            {
                Username = username,
                FullName = fullName,
                PasswordHash = result.Hash,
                PasswordSalt = result.Salt,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                PasswordChangedAtUtc = DateTime.UtcNow,
                IsActive = true
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        if (!user.UserRoles.Any(userRole => userRole.RoleId == adminRole.Id))
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = adminRole.Id
            });
            await db.SaveChangesAsync();
        }
    }

    private static async Task SeedAuthorizationCatalogAsync(AppDbContext db)
    {
        var existingPermissions = await db.Permissions.ToListAsync();
        foreach (var definition in PermissionCatalog.Permissions)
        {
            var permission = existingPermissions.FirstOrDefault(item =>
                item.Key.Equals(
                    definition.Key,
                    StringComparison.OrdinalIgnoreCase));
            if (permission is null)
            {
                permission = new AppPermission { Key = definition.Key };
                existingPermissions.Add(permission);
                db.Permissions.Add(permission);
            }

            permission.Module = definition.Module;
            permission.Name = definition.Name;
            permission.Description = definition.Description;
        }

        await db.SaveChangesAsync();

        var existingRoles = await db.Roles
            .Include(role => role.RolePermissions)
            .ToListAsync();
        foreach (var preset in PermissionCatalog.RolePresets)
        {
            var role = existingRoles.FirstOrDefault(item =>
                item.Name.Equals(
                    preset.Name,
                    StringComparison.OrdinalIgnoreCase));
            if (role is null)
            {
                role = new AppRole
                {
                    Name = preset.Name,
                    Description = preset.Description
                };
                existingRoles.Add(role);
                db.Roles.Add(role);
                await db.SaveChangesAsync();
            }

            role.Description = preset.Description;
            var desiredPermissionIds = existingPermissions
                .Where(permission => preset.Permissions.Contains(
                    permission.Key,
                    StringComparer.OrdinalIgnoreCase))
                .Select(permission => permission.Id)
                .ToHashSet();
            var currentPermissionIds = role.RolePermissions
                .Select(item => item.PermissionId)
                .ToHashSet();

            if (role.RolePermissions.Count == 0)
            {
                foreach (var permissionId in desiredPermissionIds.Except(
                             currentPermissionIds))
                {
                    db.RolePermissions.Add(new RolePermission
                    {
                        RoleId = role.Id,
                        PermissionId = permissionId
                    });
                }
            }
        }

        await db.SaveChangesAsync();
        await MigrateLegacyOverridesAsync(db, existingPermissions);
    }

    private static async Task MigrateLegacyOverridesAsync(
        AppDbContext db,
        IReadOnlyCollection<AppPermission> permissions)
    {
        var legacyRoles = await db.UserRoles
            .Include(item => item.Role)
            .Where(item =>
                item.Role.Name.StartsWith(PermissionCatalog.AllowPrefix) ||
                item.Role.Name.StartsWith(PermissionCatalog.DenyPrefix))
            .ToListAsync();
        if (legacyRoles.Count == 0)
            return;

        var existingOverrides = await db.UserPermissionOverrides
            .ToListAsync();
        foreach (var userRole in legacyRoles)
        {
            var isAllow = userRole.Role.Name.StartsWith(
                PermissionCatalog.AllowPrefix,
                StringComparison.OrdinalIgnoreCase);
            var prefix = isAllow
                ? PermissionCatalog.AllowPrefix
                : PermissionCatalog.DenyPrefix;
            var permissionKey = userRole.Role.Name[prefix.Length..];
            var permission = permissions.FirstOrDefault(item =>
                item.Key.Equals(
                    permissionKey,
                    StringComparison.OrdinalIgnoreCase));
            if (permission is null)
                continue;

            var permissionOverride = existingOverrides.FirstOrDefault(item =>
                item.UserId == userRole.UserId &&
                item.PermissionId == permission.Id);
            if (permissionOverride is null)
            {
                permissionOverride = new UserPermissionOverride
                {
                    UserId = userRole.UserId,
                    PermissionId = permission.Id
                };
                existingOverrides.Add(permissionOverride);
                db.UserPermissionOverrides.Add(permissionOverride);
            }

            permissionOverride.Effect = isAllow
                ? PermissionOverrideEffect.Allow
                : PermissionOverrideEffect.Deny;
            permissionOverride.UpdatedAtUtc = DateTime.UtcNow;
        }

        db.UserRoles.RemoveRange(legacyRoles);
        await db.SaveChangesAsync();
    }
}
