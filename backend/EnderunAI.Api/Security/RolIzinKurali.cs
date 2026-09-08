using EnderunAI.Api.Models;

namespace EnderunAI.Api.Security;

/// <summary>
/// ROL İZNİ BULUNMALI MI — TEK KURAL, TEK YER (SEED/1 SB1 + KATALOG/1).
///
/// ═══ DOĞRULUK TABLOSU ═══
///
///   katalogda VAR  + kaldırma kaydı YOK   → BULUNSUN
///   katalogda VAR  + kaldırma kaydı VAR   → BULUNMASIN
///   katalogda YOK  + elle ekleme YOK      → BULUNMASIN
///   katalogda YOK  + elle ekleme VAR      → BULUNSUN
///
/// Tek cümlede: KATALOGDA VARSA kaldırma kaydı karar verir, YOKSA
/// elle ekleme kaydı karar verir.
///
/// ═══ NEDEN İKİ YÖN ═══
///
/// SEED/1(b) yalnız EKLEME yönünü kapatmıştı: tohumlayıcı, kullanıcının
/// kaldırdığı çifti geri koymuyor. SİLME yönü açık kalmıştı — katalogdan
/// ÇIKARILAN bir izin veritabanında süresiz kalıyordu.
///
/// ÖLÇÜLDÜ (AC1, 2026-09-08): `projects.delete` 2026-08-02'de iki role
/// verildi, 2026-08-06'da katalogdan kaldırıldı, ve bugün hâlâ o iki
/// rolde duruyor. Tohumlayıcı yalnız ekler; hiç silmez.
///
/// ═══ NEDEN "ZATEN VAR MI" ARTIK SORULMUYOR ═══
///
/// Eski `TohumlanmaliMi(katalogdaVar, zatenVar, kaldirilmis)` bir
/// EYLEM soruyordu: "ekleyeyim mi". Eylem sorusu tek yönlüdür ve
/// silme yönünü ifade edemez.
///
/// Bu kural bir DURUM söylüyor: "bu çift bulunmalı mı". Uzlaştırıcı
/// istenen durumu mevcut durumla karşılaştırıp farkı kapatıyor;
/// eklemek de silmek de aynı cümleden çıkıyor. İki ayrı kural
/// yazılsaydı biri düzeltilip öteki unutulurdu (Kural 79).
///
/// ═══ NEDEN BU KADAR KÜÇÜK BİR ŞEY İÇİN AYRI SINIF ═══
///
/// Küçük olduğu için değil, SESSİZ olduğu için. Yanlış yazılırsa
/// kimse hata görmez: izin geri gelir ya da sessizce kaybolur, ekran
/// yine çalışır. Görünmeyen bir kuralın tek savunması tek yerde
/// durmasıdır.
/// </summary>
public static class RolIzinKurali
{
    /// <summary>
    /// Bu rol+izin çifti veritabanında BULUNMALI MI.
    /// </summary>
    /// <param name="katalogdaVar">`RoleCatalog` bu çifti tanımlıyor mu.</param>
    /// <param name="kaldirilmis">Kullanıcı bu çifti matristen kaldırmış mı.</param>
    /// <param name="elleEklendi">Kullanıcı katalog dışı bu çifti elle vermiş mi.</param>
    public static bool BulunmaliMi(
        bool katalogdaVar, bool kaldirilmis, bool elleEklendi) =>
        katalogdaVar ? !kaldirilmis : elleEklendi;

    /// <summary>
    /// Kaldırma kaydı kümesi için anahtar. Tek biçim, tek yer.
    /// </summary>
    public static (Guid RoleId, Guid PermissionId) Anahtar(
        RolePermissionRevocation kayit) => (kayit.RoleId, kayit.PermissionId);

    /// <summary>
    /// Elle ekleme kaydı için aynı anahtar biçimi.
    /// </summary>
    public static (Guid RoleId, Guid PermissionId) Anahtar(
        RoleManualPermissionGrant kayit) => (kayit.RoleId, kayit.PermissionId);
}
