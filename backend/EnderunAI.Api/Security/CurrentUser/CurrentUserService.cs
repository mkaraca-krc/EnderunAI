using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace EnderunAI.Api.Security.CurrentUser;

public sealed class CurrentUserService(
    IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    private ClaimsPrincipal? Principal =>
        httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated =>
        Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId
    {
        get
        {
            var value =
                Principal?.FindFirstValue(ClaimTypes.NameIdentifier) ??
                Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
                Principal?.FindFirstValue("sub");

            return Guid.TryParse(value, out var userId)
                ? userId
                : null;
        }
    }

    public string? Username =>
        Principal?.FindFirstValue(JwtRegisteredClaimNames.UniqueName) ??
        Principal?.FindFirstValue(ClaimTypes.Name) ??
        Principal?.FindFirstValue("unique_name");

    public string? FullName =>
        Principal?.FindFirstValue("full_name") ??
        Principal?.FindFirstValue(ClaimTypes.GivenName) ??
        Username;

    public IReadOnlyCollection<string> Roles =>
        GetDistinctClaimValues(
            ClaimTypes.Role,
            "role",
            "roles");

    public IReadOnlyCollection<string> Permissions =>
        GetDistinctClaimValues(
            "permissions",
            "permission");

    public bool IsInRole(string role) =>
        !string.IsNullOrWhiteSpace(role) &&
        Roles.Contains(role, StringComparer.OrdinalIgnoreCase);

    public bool HasPermission(string permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
            return false;

        return IsInRole("Admin") ||
               Permissions.Contains(
                   permission,
                   StringComparer.OrdinalIgnoreCase) ||
               PermissionCatalog.Resolve(Roles).Contains(permission);
    }

    private IReadOnlyCollection<string> GetDistinctClaimValues(
        params string[] claimTypes)
    {
        if (Principal is null)
            return Array.Empty<string>();

        return claimTypes
            .SelectMany(Principal.FindAll)
            .Select(claim => claim.Value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
