using EnderunAI.Api.Security.CurrentUser;

namespace EnderunAI.Api.Security;

/// <summary>
/// Sağlık raporunun TIBBİ DETAYI için gizlilik kapısı.
///
/// <see cref="IExtraPaymentVisibilityService"/> ile aynı desen. Ayrımın
/// sebebi: İSG modülünü kullanan herkesin (ör. sahadaki Teknik
/// Koordinatör) raporun süresini görmesi gerekir — yenileme takibi
/// bunsuz yapılamaz — ama teşhis, kısıtlama notu ve taranmış rapor
/// dosyası kişinin sağlık verisidir ve yalnızca
/// <see cref="PermissionCatalog.Keys.IsgHealthView"/> iznine sahip
/// kullanıcıya döner.
///
/// Gizleme arayüzde değil, projeksiyon seviyesinde yapılır: yetkisiz
/// kullanıcıya tıbbi alanlar null gelir, hiç sorgudan çıkmaz.
/// </summary>
public interface IIsgHealthVisibilityService
{
    Task<bool> CanViewHealthDetailAsync(CancellationToken cancellationToken = default);
}

public sealed class IsgHealthVisibilityService(
    ICurrentUserService currentUser,
    IUserAuthorizationService authorizationService) : IIsgHealthVisibilityService
{
    public async Task<bool> CanViewHealthDetailAsync(
        CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not Guid userId)
            return false;

        var snapshot = await authorizationService.GetAsync(userId, cancellationToken);

        if (snapshot is null || !snapshot.IsActive)
            return false;

        return snapshot.Permissions.Contains(
            PermissionCatalog.Keys.IsgHealthView, StringComparer.OrdinalIgnoreCase);
    }
}
