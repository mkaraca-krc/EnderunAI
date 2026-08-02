namespace EnderunAI.Api.Models;

public sealed class Permission
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Key { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } =
        new List<RolePermission>();
}

public sealed class RolePermission
{
    public Guid RoleId { get; set; }
    public AppRole Role { get; set; } = null!;

    public Guid PermissionId { get; set; }
    public Permission Permission { get; set; } = null!;
}

public enum PermissionOverrideEffect
{
    Allow = 1,
    Deny = 2
}

public sealed class UserPermissionOverride
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public Guid PermissionId { get; set; }
    public Permission Permission { get; set; } = null!;

    public PermissionOverrideEffect Effect { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedByUserId { get; set; }
}
