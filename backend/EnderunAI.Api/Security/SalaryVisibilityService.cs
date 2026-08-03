using EnderunAI.Api.Security.CurrentUser;
namespace EnderunAI.Api.Security;

/// <summary>
/// Ücret gizliliği. Maaş, bordro ve işçilik maliyeti rakamları yalnızca
/// <see cref="PermissionCatalog.Keys.SalaryView"/> iznine sahip
/// kullanıcılara döner.
///
/// Gizleme arayüzde değil, projeksiyon seviyesinde yapılır: yetkisiz
/// kullanıcıya tutar alanları null gelir. Yalnızca arayüzde gizlemek,
/// veriyi API'den okuyan herkese açık bırakırdı.
/// </summary>
public interface ISalaryVisibilityService
{
    Task<bool> CanViewSalaryAsync(CancellationToken cancellationToken = default);
}

public sealed class SalaryVisibilityService(
    ICurrentUserService currentUser,
    IUserAuthorizationService authorizationService) : ISalaryVisibilityService
{
    public async Task<bool> CanViewSalaryAsync(
        CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not Guid userId)
            return false;

        var snapshot = await authorizationService.GetAsync(userId, cancellationToken);

        if (snapshot is null || !snapshot.IsActive)
            return false;

        return snapshot.Permissions.Contains(
            PermissionCatalog.Keys.SalaryView, StringComparer.OrdinalIgnoreCase);
    }
}
