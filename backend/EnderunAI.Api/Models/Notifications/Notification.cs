namespace EnderunAI.Api.Models.Notifications;

/// <summary>
/// Bildirimin aciliyeti. Sıralama buna göre; kullanıcı önce yanmakta
/// olanı görmeli. Hızır brifingindeki <c>BriefingSeverity</c> ile
/// AYNI kademeler — iki ayrı ölçek olsaydı aynı olay iki yerde farklı
/// renkte görünürdü.
/// </summary>
public enum NotificationSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

public enum NotificationStatus
{
    /// <summary>Açık — kullanıcı henüz görmedi.</summary>
    Open = 0,

    /// <summary>Okundu ama iş duruyor.</summary>
    Read = 1,

    /// <summary>
    /// Ertelendi. <see cref="SnoozedUntil"/> gelene kadar listede
    /// görünmez; kullanıcı bugün yapamayacağı işi susturabilmeli,
    /// yoksa bildirim merkezi görmezden gelinen bir yığına döner.
    /// </summary>
    Snoozed = 2,

    /// <summary>
    /// Kullanıcı kapattı. Kaynak hâlâ duruyor olabilir — "gördüm,
    /// bilerek bırakıyorum" demek.
    /// </summary>
    Dismissed = 3,

    /// <summary>
    /// KAYNAK KALKTI: çek ödendi, belge yenilendi, talep onaylandı.
    /// Tarama bunu kendiliğinden kapatır; kullanıcının kapatmasına
    /// gerek yok. Kapanmasaydı bildirim merkezi çözülmüş işlerle
    /// dolar ve güvenilirliğini yitirirdi.
    /// </summary>
    Closed = 90
}

/// <summary>
/// Merkezî bildirim / hatırlatma.
///
/// TEKİLLEŞTİRME: <see cref="Type"/> + <see cref="SourceId"/> +
/// <see cref="PeriodKey"/> üçlüsü şirket içinde TEKİL. Günlük tarama
/// aynı satırı GÜNCELLER, yenisini açmaz. Her turda yeni satır
/// açılsaydı bir haftalık vade uyarısı yedi kayıt üretir ve
/// "okundu" bilgisi her gece kaybolurdu.
///
/// KANAL-AGNOSTİK: bu kayıt bildirimin KENDİSİ, uygulama içi
/// gösterimi değil. E-posta kanalı sonra eklendiğinde aynı satır
/// okunur; motor yeniden yazılmaz.
///
/// YETKİ: satır kullanıcıya değil, ŞİRKETE ait. Kim görebilir
/// <see cref="RequiredPermission"/> ile belirlenir ve okuma anında
/// süzülür. Kullanıcı başına satır üretilseydi tek bir çek vadesi
/// için onlarca kopya doğar, biri okununca diğerleri eskirdi.
/// </summary>
public sealed class Notification : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>Kaynak türü anahtarı — "cheque.due", "isg.expiring".</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Tetikleyen kaydın kimliği. Kayda bağlı olmayan bildirimlerde
    /// (ör. "yıl parametreleri doğrulanmadı") boş kalır.
    /// </summary>
    public Guid? SourceId { get; set; }

    /// <summary>
    /// Dönem ayracı — aynı kaynağın farklı dönemleri ayrı bildirim
    /// olsun diye. Kredi taksitinde "2026-09", kart ekstresinde son
    /// ödeme günü. Boş yerine "-" yazılır: benzersiz indeks NULL'ları
    /// birbirinden ayrı sayar ve tekilleştirme sessizce delinirdi.
    /// </summary>
    public string PeriodKey { get; set; } = "-";

    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Açıklama — TUTAR İÇERMEZ. Herkese gösterilebilir olan metin.
    /// </summary>
    public string? Detail { get; set; }

    /// <summary>
    /// Tutar içeren açıklama. Yalnızca
    /// <see cref="AmountPermission"/> iznine sahip kullanıcıya
    /// gösterilir; olmayan kullanıcı <see cref="Detail"/> görür.
    ///
    /// İki ayrı alan tutuluyor çünkü tek metinden tutarı çalışma
    /// anında ayıklamak kırılgan: bir gün biçim değişir, maske
    /// sessizce delinir.
    /// </summary>
    public string? AmountDetail { get; set; }

    /// <summary>Tutarlı metni görmek için gereken izin.</summary>
    public string? AmountPermission { get; set; }

    public NotificationSeverity Severity { get; set; }

    /// <summary>İlgili ekranın yolu; bildirim oradan link verir.</summary>
    public string? TargetPath { get; set; }

    /// <summary>
    /// Bildirimi görebilmek için gereken izin. Boşsa herkese açık.
    /// Finans bildirimi finansa, İK bildirimi İK'ya bu alanla gider.
    /// </summary>
    public string? RequiredPermission { get; set; }

    /// <summary>Varsa ilgili vade — sıralama ve "kaç gün kaldı" için.</summary>
    public DateTime? DueDate { get; set; }

    public NotificationStatus Status { get; set; } = NotificationStatus.Open;

    public DateTime? SnoozedUntil { get; set; }

    /// <summary>
    /// İlk üretildiği ve son görüldüğü an. Son görülme her taramada
    /// tazelenir; kaynak kalkınca tarama bunu güncellemeyi bırakır ve
    /// kapatma bu farktan anlaşılır.
    /// </summary>
    public DateTime FirstSeenAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// KİŞİSEL BİLDİRİM: doluysa bu satır TEK BİR KULLANICIYA ait ve
    /// okunma durumu <see cref="NotificationRecipient"/> üzerinden
    /// izlenir.
    ///
    /// Boşsa satır ŞİRKETE aittir ve görünürlük
    /// <see cref="RequiredPermission"/> ile belirlenir — mevcut dört
    /// tarama kaynağının davranışı bu ve değişmiyor.
    ///
    /// İKİ MODEL GEÇİCİ OLARAK YAN YANA: bundan sonra eklenecek her
    /// yeni bildirim KİŞİSEL doğar. Şirket satırı yalnız o dört
    /// kaynak için duruyor.
    /// </summary>
    public Guid? TargetUserId { get; set; }

    /// <summary>
    /// Şirket satırlarının okunma damgası. KİŞİSEL satırlarda
    /// kullanılmaz — orada okuma durumu alıcı tablosunda.
    /// </summary>
    public DateTime? ReadAtUtc { get; set; }
    public DateTime? DismissedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
}
