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

    /// <summary>
    /// JETON/1 — İZİN VE ROL JETONDAN DEĞİL, KANONİK ÇÖZÜCÜDEN.
    ///
    /// Eskiden `Roles` jetondaki rol taleplerini, `Permissions` jetondaki
    /// izin taleplerini okuyordu. Jeton 12 saat yaşıyor; izni alınan
    /// kullanıcı bu süre boyunca eski izniyle karar aldırıyordu (ölçüldü:
    /// kapanmış iptal yetkisi alındıktan sonra aynı jetonla ödenmiş çek
    /// geri alındı). Artık kaynak, `PermissionAuthorizationMiddleware`'in
    /// bu istek için çözdüğü anlık görüntü (<see cref="IstekYetkisi"/>).
    ///
    /// ═══ KAPALI DÜŞER (fail-closed) ═══
    ///
    /// Aşağıdakilerin HERHANGİ BİRİNDE izin ve rol BOŞ döner; jetona geri
    /// düşülmez — "emin değilim, izin vereyim" yok:
    ///   · istek bağlamı yok (arka plan işi, başlangıç kodu)
    ///   · bu istekte anlık görüntü yok (ara katman koşmadı ya da çözemedi)
    ///   · anlık görüntü başka bir kullanıcıya ait
    ///   · hesap pasif
    ///   · WebSocket bağlantısı — bağlantının ömrü bir isteğin ömrü
    ///     değildir; açılıştaki görüntü saatlerce eskiyebilir
    ///
    /// Arka plan işleri bu servisi KURUYOR (denetim kesicisi `UserId`
    /// okuyor, ölçüldü) ama izin üyesini çağırmıyor; bu yüzden anlık
    /// görüntü kurucuda değil, yalnız sorulduğunda okunur.
    /// </summary>
    private UserAuthorizationSnapshot? Yetki
    {
        get
        {
            var baglam = httpContextAccessor.HttpContext;
            if (baglam is null || baglam.WebSockets.IsWebSocketRequest)
                return null;

            var yetki = IstekYetkisi.Al(baglam);
            if (yetki is null || !yetki.IsActive || UserId is not Guid kimlik || yetki.UserId != kimlik)
                return null;

            return yetki;
        }
    }

    public IReadOnlyCollection<string> Roles =>
        Yetki?.RoleNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];

    public IReadOnlyCollection<string> Permissions =>
        Yetki?.Permissions ?? [];

    public bool IsInRole(string role) =>
        !string.IsNullOrWhiteSpace(role) &&
        Roles.Contains(role, StringComparer.OrdinalIgnoreCase);

    public bool HasPermission(string permission) =>
        !string.IsNullOrWhiteSpace(permission) &&
        Permissions.Contains(
            permission,
            StringComparer.OrdinalIgnoreCase);
}
