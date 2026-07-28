namespace EnderunAI.Api.Security.CurrentUser;

public interface ICurrentUserService
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    string? Username { get; }

    string? FullName { get; }

    string? SecurityStamp { get; }

    IReadOnlyCollection<string> Roles { get; }

    IReadOnlyCollection<string> Permissions { get; }

    bool IsInRole(string role);

    bool HasPermission(string permission);
}
