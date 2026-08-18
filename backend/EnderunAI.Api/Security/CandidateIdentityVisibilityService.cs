using EnderunAI.Api.Security.CurrentUser;

namespace EnderunAI.Api.Security;

/// <summary>
/// ADAY KİMLİK NUMARASI (TC) GÖRÜNÜRLÜĞÜ.
///
/// NEDEN AYRI BİR MASKE: aday havuzu uçları `personnel.view` ile
/// korunuyor ve o izin ŞANTİYE ŞEFİ ile FORMEN'de de var. Yani bugün
/// şantiye kapsamlı kullanıcılar dahil, personel görebilen HERKES bütün
/// adayların TC kimlik numarasını görüyordu — arama kutusu olmadan,
/// listede düz metin olarak.
///
/// TC bir KİŞİSEL VERİ ve aday henüz çalışan bile değil. Görülmesi için
/// bir iş gerekçesi olmalı: adayı personel kaydına çevirmek. O işlemin
/// izni `personnel.create`; bu yüzden maske ONU soruyor.
///
/// YENİ İZİN ANAHTARI AÇILMADI — bu programın tek-kaynak disiplini:
/// maskeyi mevcut ve anlamı örtüşen izne bağlamak, katalogda ikinci bir
/// gerçek doğurmaktan iyidir. `personnel.create` olan roller:
/// Admin, Genel Müdür, İK Sorumlusu — yani işe alımı sonuçlandıranlar.
///
/// FAIL-CLOSED: kullanıcı çözülemezse ya da yetkilendirme kaydı pasifse
/// TC GÖSTERİLMEZ. Maske kararsız kaldığında kişisel veriyi açmak,
/// maskeyi hiç koymamaktan kötü.
///
/// Desen `ISalaryVisibilityService` ve `IExtraPaymentVisibilityService`
/// ile aynı: kontrolcü bir bool sorar, projeksiyon alanı null'a düşer.
/// </summary>
public interface ICandidateIdentityVisibilityService
{
    Task<bool> CanViewIdentityNumberAsync(
        CancellationToken cancellationToken = default);
}

public sealed class CandidateIdentityVisibilityService(
    ICurrentUserService currentUser,
    IUserAuthorizationService authorizationService)
    : ICandidateIdentityVisibilityService
{
    public async Task<bool> CanViewIdentityNumberAsync(
        CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not Guid userId)
            return false;

        var snapshot = await authorizationService.GetAsync(userId, cancellationToken);

        if (snapshot is null || !snapshot.IsActive)
            return false;

        return snapshot.Permissions.Contains(
            PermissionCatalog.Keys.PersonnelCreate,
            StringComparer.OrdinalIgnoreCase);
    }
}
