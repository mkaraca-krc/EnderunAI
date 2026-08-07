namespace EnderunAI.Api.Models;

/// <summary>
/// Taşerondan istenen evrak türleri.
///
/// Ordinal değerler kalıcıdır; yalnızca sona ekleme yapılmalı.
/// </summary>
public enum SubcontractorDocumentType
{
    /// <summary>İmzalı taşeron sözleşmesi.</summary>
    Contract = 0,

    /// <summary>İmza sirküleri.</summary>
    SignatureCircular = 1,

    /// <summary>Vergi levhası.</summary>
    TaxCertificate = 2,

    /// <summary>
    /// SGK borcu yoktur yazısı. Kanunen kısa ömürlüdür ve
    /// ÜÇ AYDA BİR yenilenmesi gerekir; bitiş tarihi girilmemişse bile
    /// düzenlenme tarihinden üç ay sonrası son kullanma sayılır.
    /// </summary>
    SocialSecurityClearance = 3,

    /// <summary>Vergi borcu yoktur yazısı.</summary>
    TaxClearance = 4,

    /// <summary>İSG evrakları (eğitim, risk değerlendirmesi vb.).</summary>
    OccupationalSafety = 5,

    /// <summary>Ticaret sicil gazetesi.</summary>
    TradeRegistry = 6,

    /// <summary>Sigorta poliçesi.</summary>
    InsurancePolicy = 7,

    Other = 99
}

/// <summary>
/// Taşeron evrakı.
///
/// <c>ProjectDocument</c>'tan ayrı bir tablo: proje dokümanlarında
/// geçerlilik tarihi yok ve dosyalar proje klasörü mantığıyla
/// sürümleniyor. Taşeron evrakının asıl sorusu "hâlâ geçerli mi" —
/// süresi dolmuş SGK borcu yoktur yazısı, belge hiç yokmuş gibi risk
/// doğurur (asıl işverenin müteselsil sorumluluğu).
///
/// <see cref="IsgSiteDocument"/> ile aynı desen; geçerlilik hesabı da
/// aynı <see cref="Services.Isg.IsgValidityCalculator"/> ile yapılıyor
/// ki "İSG'de 30 gün, taşeronda 45 gün" gibi sessiz bir tutarsızlık
/// doğmasın.
/// </summary>
public sealed class SubcontractorDocument : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>
    /// Evrakın bağlı olduğu sözleşme. Şirket düzeyinde geçerli
    /// evraklar (imza sirküleri, vergi levhası) da sözleşmeye bağlanır:
    /// evrakı isteyen ve takip eden taraf sözleşmedir, ve aynı taşeronla
    /// iki ayrı işte farklı tarihlerde evrak istenebilir.
    /// </summary>
    public Guid SubcontractorContractId { get; set; }
    public SubcontractorContract SubcontractorContract { get; set; } = null!;

    public SubcontractorDocumentType DocumentType { get; set; }

    public string Title { get; set; } = string.Empty;

    public DateOnly IssueDate { get; set; }

    /// <summary>
    /// Geçerlilik bitişi. Boşsa süresiz sayılır — SGK borcu yoktur
    /// yazısında ise boş bırakılsa bile üç aylık kural uygulanır
    /// (bkz. <see cref="EffectiveValidUntil"/>).
    /// </summary>
    public DateOnly? ValidUntil { get; set; }

    // --- Dosya ---

    public string StoredFileName { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    public Guid? UploadedByUserId { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// Uygulanacak bitiş tarihi.
    ///
    /// SGK borcu yoktur yazısı kanunen üç ay geçerlidir; kullanıcı
    /// bitiş tarihi girmese bile bu kural uygulanır. Aksi halde belge
    /// "süresiz" görünür ve asıl işveren müteselsil sorumluluk altında
    /// kalır.
    /// </summary>
    public DateOnly? EffectiveValidUntil =>
        ValidUntil ?? (DocumentType == SubcontractorDocumentType.SocialSecurityClearance
            ? IssueDate.AddMonths(SocialSecurityClearanceMonths)
            : null);

    /// <summary>SGK borcu yoktur yazısının kanuni geçerlilik süresi (ay).</summary>
    public const int SocialSecurityClearanceMonths = 3;
}
