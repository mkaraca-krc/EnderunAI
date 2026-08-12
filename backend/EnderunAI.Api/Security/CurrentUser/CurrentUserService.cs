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

            return Guid.TryParse(value, out var id)
                ? id
                : null;
        }
    }

    public string? Username =>
        Principal?.FindFirstValue(ClaimTypes.Name) ??
        Principal?.FindFirstValue("username") ??
        Principal?.Identity?.Name;

    public string? FullName =>
        Principal?.FindFirstValue("full_name") ??
        Principal?.FindFirstValue("fullName") ??
        Principal?.FindFirstValue(ClaimTypes.GivenName);

    public IReadOnlyCollection<string> Roles =>
        Principal?
            .FindAll(ClaimTypes.Role)
            .Select(claim => claim.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
        ?? [];

    /// <summary>
    /// Token'daki izinler.
    ///
    /// TAM YETKİ BAYRAĞI: kataloğun tamamına sahip kullanıcıda
    /// <see cref="TokenService"/> izinleri tek tek yazmıyor, tek bir
    /// <c>all_permissions</c> claim'i koyuyor — 129 anahtar token'ı
    /// 4096 baytlık çerez sınırının üstüne çıkarıyor ve tarayıcı
    /// çerezi sessizce atıyordu. Bayrak burada da açılmazsa tam
    /// yetkili kullanıcı izinsiz görünür: elden tutar maskesi gibi
    /// <c>HasPermission</c>'a dayanan her yer sessizce kapanırdı.
    /// </summary>
    public IReadOnlyCollection<string> Permissions =>
        HasAllPermissions
            ? PermissionCatalog.Permissions
                .Select(definition => definition.Key)
                .ToArray()
            : Principal?
                .FindAll("permissions")
                .Select(claim => claim.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
              ?? [];

    private bool HasAllPermissions =>
        string.Equals(
            Principal?.FindFirstValue("all_permissions"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    public bool IsInRole(string role) =>
        !string.IsNullOrWhiteSpace(role) &&
        Principal?.IsInRole(role) == true;

    public bool HasPermission(string permission) =>
        !string.IsNullOrWhiteSpace(permission) &&
        Permissions.Contains(
            permission,
            StringComparer.OrdinalIgnoreCase);
}
