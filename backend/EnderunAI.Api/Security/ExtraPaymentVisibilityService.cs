using EnderunAI.Api.Security.CurrentUser;

namespace EnderunAI.Api.Security;

/// <summary>
/// Ek ödeme (elden) gizliliği. Elden ödenen tutarlar ve elden tazminat
/// farkı yalnızca <see cref="PermissionCatalog.Keys.ExtraPaymentView"/>
/// iznine sahip kullanıcılara döner.
///
/// <see cref="ISalaryVisibilityService"/> ile aynı desen. İzin anahtarı
/// ayrı tutuluyor ki ileride tek bir role kapatmak gerekirse kaldıraç
/// elde kalsın; ama bugün maaşı gören her role (Admin, Genel Müdür,
/// Finans Sorumlusu, Ön Muhasebe, İK Sorumlusu) veriliyor — maaş
/// kartında resmi net, elden ödeme ve toplam ele geçen birlikte
/// gösterildiği için.
///
/// Maaşı GÖRMEYEN roller ek ödemeyi de göremez: Şantiye Şefi, Formen,
/// Teknik Ofis, Teknik Koordinatör ve Sekreterya hiçbir koşulda
/// göremez.
///
/// Gizleme arayüzde değil, projeksiyon seviyesinde yapılır: yetkisiz
/// kullanıcıya gerçek/fark alanları null gelir.
/// </summary>
public interface IExtraPaymentVisibilityService
{
    Task<bool> CanViewExtraPaymentAsync(CancellationToken cancellationToken = default);
}

public sealed class ExtraPaymentVisibilityService(
    ICurrentUserService currentUser,
    IUserAuthorizationService authorizationService) : IExtraPaymentVisibilityService
{
    public async Task<bool> CanViewExtraPaymentAsync(
        CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not Guid userId)
            return false;

        var snapshot = await authorizationService.GetAsync(userId, cancellationToken);

        if (snapshot is null || !snapshot.IsActive)
            return false;

        return snapshot.Permissions.Contains(
            PermissionCatalog.Keys.ExtraPaymentView, StringComparer.OrdinalIgnoreCase);
    }
}
