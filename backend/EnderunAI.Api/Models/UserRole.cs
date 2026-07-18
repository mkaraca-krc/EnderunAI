namespace EnderunAI.Api.Models;

public sealed class UserRole
{
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public Guid RoleId { get; set; }
    public AppRole Role { get; set; } = null!;
}
