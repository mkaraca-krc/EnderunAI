namespace EnderunAI.Api.Models;

/// <summary>
/// ROLDEN KALDIRILAN İZNİN KAYDI — TOHUMLAYICI ONU GERİ KOYMASIN DİYE.
///
/// ═══ ÖLÇÜLEN KUSUR (SEED/1, 2026-09-07) ═══
///
/// `SeedRolePermissionsAsync` her açılışta koşulsuz çalışıp
/// `RoleCatalog`taki eksik çiftleri geri ekliyordu. Sonuç: matristen
/// kaldırılan izin, bir sonraki yayında geri geliyordu.
///
/// Denetim kaydından ölçüldü:
///   18:57  mehmet, 6 rolden `dashboard.view` kaldırdı
///   20:33  sistem, aynı 6 çifti geri ekledi (yeniden başlatma)
/// Bugüne kadar yapılan 6 kaldırmanın 6'sı da geri gelmişti; kalıcı
/// olmuş tek bir kaldırma yoktu.
///
/// Deneysel ölçüm: katalog 595 çift üretiyor, canlıda 597 vardı —
/// yani 597'nin 595'i geri gelirdi.
///
/// ═══ NEDEN "KALDIRMA KAYDI", NEDEN "TOHUMLAYICIYI KAPAT" DEĞİL ═══
///
/// İzin kataloğu 6 haftada 35'ten 143'e çıktı, 60 günde 20 commit
/// dokundu — kabaca 2-3 günde bir yeni izin. Tohumlayıcı kapatılsaydı
/// her yeni izin 15 role kadar ELLE tıklanacaktı ve unutulan tıklama
/// sessiz kalacaktı.
///
/// Bu kayıt ikisini birden veriyor: yeni izinler dağıtılmaya devam
/// ediyor, KALDIRILMIŞ olanlar geri gelmiyor.
///
/// ═══ NEDEN AYRI TABLO, NEDEN RolePermission'DA BAYRAK DEĞİL ═══
///
/// Kaldırılan çiftin `RolePermission` satırı ARTIK YOK. Bayrak
/// koyacak bir satır yok; kaldırma ancak AYRI bir kayıtla
/// hatırlanabilir.
/// </summary>
public sealed class RolePermissionRevocation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RoleId { get; set; }
    public AppRole Role { get; set; } = null!;

    public Guid PermissionId { get; set; }
    public Permission Permission { get; set; } = null!;

    public DateTime RevokedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Kaldıran kişi. `null` olabilir: geçmişte yazılmış ya da
    /// oturumu çözülemeyen bir kayıt, kaydın kendisini geçersiz
    /// kılmamalı — kaldırma bilgisi aktörden daha değerli.
    /// </summary>
    public Guid? RevokedByUserId { get; set; }
}
