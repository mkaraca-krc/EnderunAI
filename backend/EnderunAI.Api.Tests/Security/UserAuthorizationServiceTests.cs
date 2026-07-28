using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Tests.Security;

public sealed class UserAuthorizationServiceTests
{
    [Fact]
    public async Task DenyOverrideWinsOverRolePermission()
    {
        await using var db = CreateContext();
        var permission = new AppPermission
        {
            Key = PermissionCatalog.Keys.ProjectsManage,
            Module = "Proje",
            Name = "Proje yönetimi",
            Description = "Test"
        };
        var role = new AppRole { Name = "Proje Müdürü" };
        var user = CreateUser();
        db.AddRange(permission, role, user);
        db.RolePermissions.Add(new RolePermission
        {
            Role = role,
            Permission = permission
        });
        db.UserRoles.Add(new UserRole
        {
            User = user,
            Role = role
        });
        db.UserPermissionOverrides.Add(new UserPermissionOverride
        {
            User = user,
            Permission = permission,
            Effect = PermissionOverrideEffect.Deny
        });
        await db.SaveChangesAsync();

        var snapshot = await new UserAuthorizationService(db).GetAsync(user.Id);

        Assert.NotNull(snapshot);
        Assert.DoesNotContain(
            PermissionCatalog.Keys.ProjectsManage,
            snapshot!.Permissions);
    }

    [Fact]
    public async Task AllowOverrideAddsPermissionOutsideRole()
    {
        await using var db = CreateContext();
        var permission = new AppPermission
        {
            Key = PermissionCatalog.Keys.FinanceView,
            Module = "Finans",
            Name = "Finans görüntüleme",
            Description = "Test"
        };
        var role = new AppRole { Name = "Tekniker" };
        var user = CreateUser();
        db.AddRange(permission, role, user);
        db.UserRoles.Add(new UserRole { User = user, Role = role });
        db.UserPermissionOverrides.Add(new UserPermissionOverride
        {
            User = user,
            Permission = permission,
            Effect = PermissionOverrideEffect.Allow
        });
        await db.SaveChangesAsync();

        var snapshot = await new UserAuthorizationService(db).GetAsync(user.Id);

        Assert.NotNull(snapshot);
        Assert.Contains(PermissionCatalog.Keys.FinanceView, snapshot!.Permissions);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static AppUser CreateUser() => new()
    {
        Username = Guid.NewGuid().ToString("N"),
        FullName = "Test User",
        PasswordHash = "hash",
        PasswordSalt = "salt",
        SecurityStamp = Guid.NewGuid().ToString("N")
    };
}
