namespace EnderunAI.Api.Models;

public enum ProjectSiteDailyReportStatus
{
    Draft = 0,
    Approved = 1
}

public sealed class ProjectSiteDailyReport : BaseEntity
{
    public Guid ProjectSiteId { get; set; }
    public ProjectSite ProjectSite { get; set; } = null!;

    public DateTime ReportDate { get; set; }
    public string? WeatherCondition { get; set; }

    public int EngineerCount { get; set; }
    public int ForemanCount { get; set; }
    public int CraftsmanCount { get; set; }
    public int WorkerCount { get; set; }
    public int OtherCount { get; set; }

    public string? Notes { get; set; }

    public ProjectSiteDailyReportStatus Status { get; set; } =
        ProjectSiteDailyReportStatus.Draft;

    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }

    public ICollection<ProjectSiteDailyReportWorkItem> WorkItems { get; set; }
        = new List<ProjectSiteDailyReportWorkItem>();

    public ICollection<ProjectSiteDailyReportPhoto> Photos { get; set; }
        = new List<ProjectSiteDailyReportPhoto>();
}

public sealed class ProjectSiteDailyReportWorkItem : BaseEntity
{
    public Guid DailyReportId { get; set; }
    public ProjectSiteDailyReport DailyReport { get; set; } = null!;

    /// <summary>
    /// Sözleşme icmalindeki kalem. OPSİYONEL: icmalde olmayan iş de
    /// günlük rapora yazılabilmeli, aksi halde saha o günü hiç
    /// kaydedemezdi. Seçilirse onaylı miktarlar bu kalemin iç
    /// gerçekleşmesine birikir.
    /// </summary>
    public Guid? ProjectBoqItemId { get; set; }
    public ProjectBoqItem? ProjectBoqItem { get; set; }

    public string Description { get; set; } = string.Empty;
    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }
}

public sealed class ProjectSiteDailyReportPhoto : BaseEntity
{
    public Guid DailyReportId { get; set; }
    public ProjectSiteDailyReport DailyReport { get; set; } = null!;

    public string StoredFileName { get; set; } = string.Empty;
    public string OriginalName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string? Caption { get; set; }

    public bool IsVisibleToEmployer { get; set; } = false;
}

public sealed class EmployerPortalLink : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    /*
     * TOKEN ARTIK TABLODA TUTULMUYOR — PAROLAYLA AYNI MANTIK.
     *
     * Eskiden `Token` alanı anahtarın kendisini düz metin tutuyordu.
     * Bir sırrı saklamanın en güvenli yolu onu hiç saklamamaktır:
     * token yalnız ÜRETİLDİĞİ AN bellekte var olur, bağlantı adresine
     * yazılır ve bir daha hiçbir yerde durmaz. Tabloda özeti kalır.
     *
     * SONUÇLARI (bilinçli kabul edildi):
     *   - "Linki kopyala" YALNIZ oluşturma anında çalışır. Adres
     *     kaybedilirse geri getirilemez; yeni bağlantı üretilir.
     *   - Denetim kaydına ya da bir log satırına token sızsa bile
     *     tabloyla eşleşmez; sızıntı tek başına işe yaramaz.
     *   - Karartma mekanizmasına yeni kayıtlar için gerek kalmadı.
     *
     * `Token` alanı SİLİNMEDİ: 2026-08-23 öncesi doğmuş, iptal edilip
     * karartılmış 7 satır orada duruyor ve izlenebilirlikleri o
     * alandan okunuyor. Yeni satırlarda boş kalır.
     */
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Tokenın SHA-256 özeti (hex). Arama bununla yapılır.
    ///
    /// BENZERSİZ: iki bağlantı aynı özeti taşıyamaz — token
    /// çakışması zaten imkânsıza yakın ama kısıt veritabanında
    /// duruyor, uygulamanın iyi niyetine bırakılmıyor.
    ///
    /// NULL OLABİLİR: eski 7 satırın tokenı gitti, özeti üretilemez.
    /// Onlar "eski kayıt, özet yok" olarak duruyor ve hiçbir istekle
    /// eşleşmiyor — zaten hepsi iptal edilmiş.
    /// </summary>
    public string? TokenHash { get; set; }

    /// <summary>
    /// Tokenın ilk 8 karakteri. SIR DEĞİLDİR: 256 bitlik anahtarın
    /// 48 biti tek başına kullanılamaz.
    ///
    /// NEDEN AYRI ALAN: denetim kaydında "hangi bağlantı denendi"
    /// sorusunu cevaplamak ve ekranda bağlantıyı tanıtmak için.
    /// Özet bunu yapamaz — özet tanınabilir değildir.
    /// </summary>
    public string? TokenPrefix { get; set; }

    /// <summary>
    /// İPTAL EDİLEN BAĞLANTININ TOKENI KARARTILIR.
    ///
    /// ARTIK GEREKSİZ — ama kaldırılmadı. Yeni bağlantılarda `Token`
    /// zaten boş doğuyor (yalnız özeti saklanıyor), yani karartacak
    /// bir şey yok. Bu metot 2026-08-23 öncesi doğmuş satırlar için
    /// duruyor: onların karartılmış değerleri tabloda ve bir gün
    /// benzer bir veri düzeltmesi gerekirse kural burada yazılı.
    ///
    /// NEDEN: iptal edilmiş bir token zaten çalışmıyor, yani zararsız.
    /// Ama tabloda düz metin durmasının da bir faydası yok — yalnızca
    /// yanmış bir sır olarak bekliyor. Veritabanı yedeği, bir rapor
    /// ekranı ya da bir hata ayıklama sorgusu onu yeniden dolaşıma
    /// sokabilir.
    ///
    /// İZLENEBİLİRLİK KORUNUYOR: ilk 8 karakter duruyor, tıpkı
    /// PortalTokenRejected olayındaki gibi. Hangi bağlantıdan söz
    /// edildiği anlaşılır, ama anahtar kullanılamaz.
    ///
    /// KİMLİK EKLENİYOR: `Token` üzerinde benzersiz indeks var. İki
    /// bağlantının ilk 8 karakteri aynı olsaydı karartma sonrası
    /// çakışır ve kayıt yazılamazdı; kimliğin ilk parçası bunu
    /// imkânsız kılıyor.
    /// </summary>
    public void Karart()
    {
        if (string.IsNullOrEmpty(Token) || Token.Contains("***"))
            return;

        var onek = Token[..Math.Min(8, Token.Length)];
        Token = $"{onek}***-{Id.ToString("N")[..8]}";
    }

    public string? EmployerName { get; set; }
    public string? EmployerEmail { get; set; }

    public DateTime? RevokedAtUtc { get; set; }
    public Guid? RevokedByUserId { get; set; }

    /// <summary>
    /// SON GEÇERLİLİK — ZORUNLU, VARSAYILAN 6 AY.
    ///
    /// Bağlantı e-postayla paylaşılıyor ve kimlik doğrulaması yok:
    /// süresiz bırakıldığında elle iptal edilene kadar kalıcı bir
    /// kapı oluyor. E-posta kutusu yıllar sonra başkasının eline
    /// geçse bile bağlantı çalışmaya devam ederdi.
    ///
    /// Süresi geçen bağlantı 404 dönüyor, 401 DEĞİL: 401 "böyle bir
    /// bağlantı var ama artık geçerli değil" bilgisini verirdi ve
    /// geçerli token aramaya çalışan birine "bu token bir zamanlar
    /// vardı" ipucu olurdu.
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>
    /// Son açılma zamanı ve toplam açılma sayısı — yönetim ekranında
    /// "bu bağlantı kullanılıyor mu" sorusunun cevabı. Kullanılmayan
    /// bir bağlantıyı iptal etmek, kullanılanı uzatmak için gerekli.
    /// </summary>
    public DateTime? LastAccessedAtUtc { get; set; }

    public int AccessCount { get; set; }

    /// <summary>
    /// Uzatma izi: kaç kez, en son ne zaman, en son kim uzattı.
    /// Denetim kaydı ayrıca security_audit_events'e yazılıyor; bu
    /// alanlar ekranda göstermek için kayıt üzerinde duruyor.
    /// </summary>
    public DateTime? LastExtendedAtUtc { get; set; }
    public Guid? LastExtendedByUserId { get; set; }
    public int ExtensionCount { get; set; }
}

public sealed class EmployerPortalEmailLog : BaseEntity
{
    public Guid EmployerPortalLinkId { get; set; }
    public EmployerPortalLink EmployerPortalLink { get; set; } = null!;

    public Guid ProjectId { get; set; }

    public string RecipientEmail { get; set; } = string.Empty;
    public string? RecipientName { get; set; }

    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}
