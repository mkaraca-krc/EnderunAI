using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Security;

public sealed record UserAuthorizationSnapshot(
    Guid UserId,
    bool IsActive,
    string SecurityStamp,
    IReadOnlyCollection<string> RoleNames,
    IReadOnlyCollection<string> Permissions,
    IReadOnlyCollection<UserDataScopeGrant> DataScopes);

public sealed record UserDataScopeGrant(
    DataScopeType ScopeType,
    Guid? CompanyId,
    Guid? BranchId,
    Guid? ProjectId);

public interface IUserAuthorizationService
{
    Task<UserAuthorizationSnapshot?> GetAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}

public sealed class UserAuthorizationService(
    AppDbContext db) : IUserAuthorizationService
{
    public async Task<UserAuthorizationSnapshot?> GetAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await db.Users
            .AsNoTracking()
            .Include(item => item.UserRoles)
            .ThenInclude(item => item.Role)
            .ThenInclude(item => item.RolePermissions)
            .ThenInclude(item => item.Permission)
            .Include(item => item.PermissionOverrides)
            .ThenInclude(item => item.Permission)
            .Include(item => item.DataScopes)
            .SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);

        if (user is null)
            return null;

        var roleNames = user.UserRoles
            .Select(item => item.Role.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
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

        return new UserAuthorizationSnapshot(
            user.Id,
            user.IsActive,
            user.SecurityStamp,
            roleNames,
            permissions.OrderBy(item => item).ToArray(),
            user.DataScopes
                .Where(item => item.IsActive)
                .Select(item => new UserDataScopeGrant(
                    item.ScopeType,
                    item.CompanyId,
                    item.BranchId,
                    item.ProjectId))
                .ToArray());
    }
}
