using EnderunAI.Api.Models;

namespace EnderunAI.Api.Security;

/// <summary>
/// ROL İZNİ TOHUMLANIR MI — TEK KURAL, TEK YER (SEED/1 SB1).
///
/// ═══ KURAL ═══
///
///     ekle EĞER (katalogda var) VE (kaldırma kaydı yok)
///
/// ═══ NEDEN AYRI BİR SINIF ═══
///
/// Kural bugün tek yerden çağrılıyor (`DatabaseSeeder`). Yarın ikinci
/// bir çağıran çıkarsa — bir göç, bir yönetim ucu, bir toplu içe
/// aktarma — kuralı ORADA yeniden yazmak zorunda kalmasın diye.
///
/// Kural 79: bir kusur birden çok okuyucuda yaşıyorsa düzeltme
/// okuyucuda değil kaynakta yapılır. Burası o kaynak.
///
/// ═══ NEDEN BU KADAR KÜÇÜK BİR ŞEY İÇİN ═══
///
/// Küçük olduğu için değil, SESSİZ olduğu için. Bu kural yanlış
/// yazılırsa kimse hata görmez: izin geri gelir, ekran çalışır,
/// yalnız kısıtlama kaybolur. Görünmeyen bir kuralın tek savunması
/// tek yerde durmasıdır.
/// </summary>
public static class RolIzinKurali
{
    /// <summary>
    /// Bu rol+izin çifti tohumlanmalı mı.
    /// </summary>
    /// <param name="katalogdaVar">`RoleCatalog` bu çifti tanımlıyor mu.</param>
    /// <param name="zatenVar">Veritabanında hâlihazırda duruyor mu.</param>
    /// <param name="kaldirilmis">Kullanıcı bu çifti matristen kaldırmış mı.</param>
    public static bool TohumlanmaliMi(
        bool katalogdaVar, bool zatenVar, bool kaldirilmis) =>
        katalogdaVar && !zatenVar && !kaldirilmis;

    /// <summary>
    /// Kaldırma kaydı kümesi için anahtar. Tek biçim, tek yer.
    /// </summary>
    public static (Guid RoleId, Guid PermissionId) Anahtar(
        RolePermissionRevocation kayit) => (kayit.RoleId, kayit.PermissionId);
}
