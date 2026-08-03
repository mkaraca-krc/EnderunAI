using EnderunAI.Api.Security;

namespace EnderunAI.Api.Services.Hizir;

/// <summary>
/// Bir aracın çalıştırılacağı kullanıcı bağlamı. Her araç sorgusunu
/// bu bağlamla sınırlar; modele "şunu gösterme" denmez, veri zaten
/// dönmez.
/// </summary>
public sealed record HizirToolContext(
    Guid UserId,
    string FullName,
    /// <summary>Kullanıcının seçtiği hitap ("Bey"/"Hanım") — yoksa null.</summary>
    string? Honorific,
    IReadOnlyCollection<string> RoleNames,
    IReadOnlyCollection<string> Permissions,
    CurrentDataScopeSnapshot Scope)
{
    public bool Has(string permission) =>
        Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Aracın çalışması sonucu. Reddedilen çağrılar da sonuç üretir —
/// model neden veri alamadığını bilmeli ki kullanıcıya doğru sebebi
/// söylesin ve veri uydurmasın.
/// </summary>
public sealed record HizirToolOutcome(
    string Content,
    bool Denied = false,
    bool IsError = false);

/// <summary>
/// Aracın güvenlik kademesi.
///
/// Tehlikeli işlemler (para/ödeme, çek durumu, kasa hareketi, silme,
/// bordro-muhasebe kesinleştirme, yetki/rol/kullanıcı yönetimi) için
/// üçüncü bir kademe YOKTUR: bu işlemler araç olarak hiç tanımlanmaz,
/// dolayısıyla modelin onlara giden bir kod yolu bulunmaz.
/// Bkz. HizirForbiddenActionTests — yanlışlıkla eklenmelerini engelleyen
/// koruma testi.
/// </summary>
public enum HizirToolTier
{
    /// <summary>Doğrudan çalışır: okuma, taslak, kendine hatırlatma.</summary>
    Safe = 0,

    /// <summary>
    /// Kullanıcı onayı olmadan yürütülmez. Aracın çalıştırıcısı iş
    /// servisini çağırmaz; yalnızca bekleyen eylem kaydı üretir.
    /// </summary>
    RequiresApproval = 1
}

/// <summary>
/// Bir Hızır aracı. RequiredPermission null ise araç herkese açıktır
/// (ör. kullanım kılavuzu araması).
/// </summary>
public sealed record HizirTool(
    string Name,
    string Description,
    object InputSchema,
    string? RequiredPermission,
    Func<HizirToolContext, IReadOnlyDictionary<string, object?>, CancellationToken,
        Task<HizirToolOutcome>> ExecuteAsync,
    HizirToolTier Tier = HizirToolTier.Safe);
