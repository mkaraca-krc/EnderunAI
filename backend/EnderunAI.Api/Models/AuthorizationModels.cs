namespace EnderunAI.Api.Models;

public enum PermissionOverrideEffect
{
    Allow = 1,
    Deny = 2
}

public sealed class AppPermission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public ICollection<RolePermission> RolePermissions { get; set; } =
        new List<RolePermission>();
    public ICollection<UserPermissionOverride> UserOverrides { get; set; } =
        new List<UserPermissionOverride>();
}

public sealed class RolePermission
{
    public Guid RoleId { get; set; }
    public AppRole Role { get; set; } = null!;
    public Guid PermissionId { get; set; }
    public AppPermission Permission { get; set; } = null!;
}

public sealed class UserPermissionOverride
{
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public Guid PermissionId { get; set; }
    public AppPermission Permission { get; set; } = null!;
    public PermissionOverrideEffect Effect { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? UpdatedByUserId { get; set; }
}
