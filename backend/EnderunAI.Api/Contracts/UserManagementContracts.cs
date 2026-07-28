using System.ComponentModel.DataAnnotations;
using EnderunAI.Api.Models;

namespace EnderunAI.Api.Contracts;

public sealed class CreateManagedUserRequest
{
    [Required, MinLength(3), MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    [EmailAddress, MaxLength(200)]
    public string? Email { get; set; }

    [Required, MaxLength(80)]
    public string RoleName { get; set; } = string.Empty;

    [MinLength(10)]
    public string? Password { get; set; }

    public bool IsActive { get; set; } = true;
    public Guid? PersonnelId { get; set; }
    public bool MustChangePassword { get; set; } = true;
    public ManagedUserDataScopeRequest[] DataScopes { get; set; } = [];
    public string[] AllowedPermissions { get; set; } = [];
    public string[] DeniedPermissions { get; set; } = [];
}

public sealed class UpdateManagedUserRequest
{
    [Required, MinLength(3), MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    [EmailAddress, MaxLength(200)]
    public string? Email { get; set; }

    [Required, MaxLength(80)]
    public string RoleName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public Guid? PersonnelId { get; set; }
    public bool MustChangePassword { get; set; }
    public ManagedUserDataScopeRequest[] DataScopes { get; set; } = [];
    public string[] AllowedPermissions { get; set; } = [];
    public string[] DeniedPermissions { get; set; } = [];
}

public sealed class ResetManagedUserPasswordRequest
{
    [MinLength(10)]
    public string? NewPassword { get; set; }

    public bool RequirePasswordChange { get; set; } = true;
}

public sealed class ManagedUserDataScopeRequest
{
    public DataScopeType ScopeType { get; set; }
    public Guid? CompanyId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? ProjectId { get; set; }
}

public sealed class UpdateManagedRolePermissionsRequest
{
    [MinLength(1)]
    public string[] Permissions { get; set; } = [];
}
