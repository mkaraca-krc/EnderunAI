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

    [Required, MinLength(1)]
    public string[] RoleNames { get; set; } = [];

    [MinLength(10)]
    public string? Password { get; set; }

    public bool IsActive { get; set; } = true;
    public string[] AllowedPermissions { get; set; } = [];
    public string[] DeniedPermissions { get; set; } = [];

    /// <summary>
    /// Seçilen rollerden biri SiteOnly kapsam politikasına sahipse
    /// (ör. Şantiye Şefi, Formen) bu liste zorunludur.
    /// </summary>
    public Guid[] ProjectSiteIds { get; set; } = [];
}

public sealed class UpdateManagedUserRequest
{
    [Required, MinLength(3), MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    [EmailAddress, MaxLength(200)]
    public string? Email { get; set; }

    [Required, MinLength(1)]
    public string[] RoleNames { get; set; } = [];

    public bool IsActive { get; set; } = true;
    public string[] AllowedPermissions { get; set; } = [];
    public string[] DeniedPermissions { get; set; } = [];
    public Guid[] ProjectSiteIds { get; set; } = [];
}

public sealed class ResetManagedUserPasswordRequest
{
    [MinLength(10)]
    public string? NewPassword { get; set; }
}
