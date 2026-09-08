namespace EnderunAI.Api.Models;

/// <summary>
/// KATALOGDA OLMAYAN AMA ELLE VERİLMİŞ İZNİN KAYDI (KATALOG/1).
///
/// ═══ NEDEN VAR ═══
///
/// `RolePermissionRevocation`ın SİMETRİĞİ. O kayıt "katalog veriyor
/// ama kullanıcı kaldırdı" durumunu hatırlıyordu; bu kayıt "katalog
/// vermiyor ama kullanıcı verdi" durumunu hatırlıyor.
///
/// İkisi olmadan uzlaştırma tek yönlü kalır. SEED/1(b) ile ekleme
/// yönü kapandı: tohumlayıcı kaldırılmış çifti geri koymuyor. Ama
/// SİLME yönü açıktı — katalogdan çıkarılan bir izin veritabanında
/// süresiz kalıyordu.
///
/// ═══ ÖLÇÜLEN KUSUR (AC1, 2026-09-08) ═══
///
///   2026-08-02 `26ad404b` — RoleCatalog `projects.delete`i
///     Teknik Ofis ve Teknik Koordinatör'e veriyor; tohumlayıcı yazıyor.
///   2026-08-06 `4e55a24a` — katalog iki satırı da KALDIRIYOR.
///   Bugün — iki satır hâlâ veritabanında. Tohumlayıcı yalnız ekler.
///
/// Bu iki rol, katalog "olmasın" derken proje silme/arşivleme
/// yapabiliyordu. Fiilî kapı bugün kapalıydı ama onu kapatan şey
/// TESADÜFTÜ: tek aktif taşıyıcının o izni kişisel olarak kısıtlıydı,
/// öteki rolü taşıyan aktif kullanıcı yoktu.
///
/// ═══ NEDEN AYRI TABLO ═══
///
/// `RolePermission` satırının kendisi "nereden geldi" bilgisini
/// taşımıyor: katalogdan mı, elden mi, ayırt edilemiyor. Uzlaştırıcı
/// bu ayrımı yapamazsa ya elle verilmiş izinleri siler ya da hiçbir
/// şey silemez. Kayıt, o ayrımın tek kaynağı.
/// </summary>
public sealed class RoleManualPermissionGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RoleId { get; set; }
    public AppRole Role { get; set; } = null!;

    public Guid PermissionId { get; set; }
    public Permission Permission { get; set; } = null!;

    public DateTime GrantedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Veren kişi. `null` olabilir — kaldırma kaydındaki gerekçenin
    /// aynısı: kaydın kendisi aktöründen daha değerli.
    /// </summary>
    public Guid? GrantedByUserId { get; set; }
}
