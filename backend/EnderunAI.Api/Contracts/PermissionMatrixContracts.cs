namespace EnderunAI.Api.Contracts;

public sealed class TogglePermissionGrantRequest
{
    public Guid RoleId { get; set; }
    public string PermissionKey { get; set; } = string.Empty;
    public bool Granted { get; set; }
}

public sealed class UpdateRoleScopePolicyRequest
{
    public int DataScopePolicy { get; set; }
}

public sealed class CreateRoleRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CopyFromRoleName { get; set; }
}
