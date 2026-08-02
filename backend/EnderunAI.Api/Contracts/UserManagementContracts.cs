using System.ComponentModel.DataAnnotations;

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
    public string[] AllowedPermissions { get; set; } = [];
    public string[] DeniedPermissions { get; set; } = [];
}

public sealed class ResetManagedUserPasswordRequest
{
    [MinLength(10)]
    public string? NewPassword { get; set; }
}
