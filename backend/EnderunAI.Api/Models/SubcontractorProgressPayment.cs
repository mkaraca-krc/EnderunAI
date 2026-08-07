namespace EnderunAI.Api.Models;

public enum SubcontractorProgressPaymentStatus
{
    Draft = 0,
    Submitted = 1,
    Approved = 2,
    Paid = 3,
    Cancelled = 4
}

/// <summary>
/// Taşeron hakedişi — işveren hakedişimizin TERS YÖNÜ: burada biz
/// ödüyoruz.
///
/// İKİ KATMANLI: her satırda hem ÖNERİ (puantaj ve saha raporundan
/// türetilen) hem MUTABAKAT (taşeronla anlaşılan) ayrı sütunlarda
/// durur. Öneri hiçbir zaman sessizce mutabakat yerine geçmez —
/// aksi halde sahanın tahmini, imzalanmış bir rakammış gibi ödemeye
/// dönüşürdü. Hesap her zaman MUTABAKAT rakamıyla yapılır.
///
/// Kümülatif (minha) mantığı işveren hakedişiyle aynı: bu dönem
/// tutarı = kümülatif − önceki. Böylece geçmiş bir satır düzeltilse
/// bile toplam doğru kalır.
/// </summary>
public sealed class SubcontractorProgressPayment : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid SubcontractorContractId { get; set; }
    public SubcontractorContract SubcontractorContract { get; set; } = null!;

    public string ProgressPaymentNumber { get; set; } = string.Empty;

    /// <summary>Kaçıncı hakediş (1'den başlar).</summary>
    public int PeriodNumber { get; set; }

    public DateTime PeriodStartDate { get; set; }
    public DateTime PeriodEndDate { get; set; }
    public DateTime ProgressPaymentDate { get; set; }

    /// <summary>
    /// Dönemin yılı ve ayı. Bordro maliyeti ve puantaj sorguları bu
    /// ikisiyle yürüdüğü için ayrıca saklanıyor: tarih aralığından
    /// türetmek, ay ortasında başlayan dönemlerde iki aya birden
    /// denk gelirdi.
    /// </summary>
    public int Year { get; set; }
    public int Month { get; set; }

    public SubcontractorProgressPaymentStatus Status { get; set; }
        = SubcontractorProgressPaymentStatus.Draft;

    public string CurrencyCode { get; set; } = "TRY";

    // --- Yapılan iş ---

    public decimal ContractAmount { get; set; }

    /// <summary>Önceki hakedişlerin kümülatif iş tutarı.</summary>
    public decimal PreviousAmount { get; set; }

    /// <summary>Bu dönemde yapılan iş (kümülatif − önceki).</summary>
    public decimal CurrentAmount { get; set; }

    public decimal CumulativeAmount { get; set; }

    // --- Kesintiler ve ödeme ---

    public decimal TotalDeductionAmount { get; set; }

    /// <summary>Kesinti öncesi bu dönem tutarı.</summary>
    public decimal GrossPayableAmount { get; set; }

    /// <summary>Kesintiler düşüldükten sonra ödenecek tutar.</summary>
    public decimal NetPayableAmount { get; set; }

    public string? Notes { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }

    /// <summary>Birim fiyatlı sözleşmede iş kalemleri.</summary>
    public ICollection<SubcontractorProgressPaymentItem> Items { get; set; }
        = new List<SubcontractorProgressPaymentItem>();

    /// <summary>Götürü sözleşmede kısım bazlı ilerleme.</summary>
    public ICollection<SubcontractorProgressPaymentSection> Sections { get; set; }
        = new List<SubcontractorProgressPaymentSection>();

    public ICollection<SubcontractorProgressPaymentDeduction> Deductions { get; set; }
        = new List<SubcontractorProgressPaymentDeduction>();
}

/// <summary>
/// Birim fiyatlı taşeron hakedişinin iş kalemi.
///
/// Öneri ve mutabakat MİKTARLARI ayrı: saha "120 m çekildi" der,
/// taşeronla "115 m" diye anlaşılır. Ödeme 115'ten yapılır, ama 120
/// kayıtta kalır — aradaki farkın izlenebilmesi için.
/// </summary>
public sealed class SubcontractorProgressPaymentItem : BaseEntity
{
    public Guid SubcontractorProgressPaymentId { get; set; }
    public SubcontractorProgressPayment SubcontractorProgressPayment { get; set; }
        = null!;

    /// <summary>Kalemin bağlı olduğu icmal kısmı; maliyet buraya yazılır.</summary>
    public Guid? ProjectHakedisSectionId { get; set; }
    public ProjectHakedisSection? ProjectHakedisSection { get; set; }

    /// <summary>Sözleşme icmalindeki satır; varsa kâr karşılaştırması yapılır.</summary>
    public Guid? ProjectBoqItemId { get; set; }
    public ProjectBoqItem? ProjectBoqItem { get; set; }

    public int LineNumber { get; set; }

    public string PositionCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;

    public decimal ContractQuantity { get; set; }

    /// <summary>Önceki hakedişlerin kümülatif mutabakat miktarı.</summary>
    public decimal PreviousQuantity { get; set; }

    /// <summary>
    /// Puantaj ve saha raporundan türetilen ÖNERİ miktarı. Bilgi
    /// amaçlıdır; hesaba GİRMEZ.
    /// </summary>
    public decimal SuggestedQuantity { get; set; }

    /// <summary>
    /// Taşeronla mutabık kalınan kümülatif miktar. Hesap bununla
    /// yapılır.
    /// </summary>
    public decimal AgreedQuantity { get; set; }

    /// <summary>Bu dönem miktarı (mutabakat − önceki).</summary>
    public decimal CurrentQuantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal PreviousAmount { get; set; }
    public decimal CurrentAmount { get; set; }
    public decimal CumulativeAmount { get; set; }

    public string? Notes { get; set; }
}

/// <summary>
/// Götürü taşeron hakedişinde bir icmal kısmının ilerlemesi.
///
/// İlerleme kısım bazında girilir ve hakediş bunların AĞIRLIKLI
/// toplamıdır: tek bir genel yüzde, kısım bazında kâr karşılaştırmasını
/// imkânsız kılardı.
/// </summary>
public sealed class SubcontractorProgressPaymentSection : BaseEntity
{
    public Guid SubcontractorProgressPaymentId { get; set; }
    public SubcontractorProgressPayment SubcontractorProgressPayment { get; set; }
        = null!;

    public Guid ProjectHakedisSectionId { get; set; }
    public ProjectHakedisSection ProjectHakedisSection { get; set; } = null!;

    public int Order { get; set; }

    /// <summary>Sözleşmedeki kısım bedeli — ağırlık budur.</summary>
    public decimal SectionAmount { get; set; }

    /// <summary>Önceki hakedişlerdeki kümülatif ilerleme yüzdesi.</summary>
    public decimal PreviousProgressRate { get; set; }

    /// <summary>Sahadan gelen ÖNERİ ilerleme yüzdesi; hesaba girmez.</summary>
    public decimal SuggestedProgressRate { get; set; }

    /// <summary>Mutabık kalınan kümülatif ilerleme yüzdesi.</summary>
    public decimal AgreedProgressRate { get; set; }

    public decimal PreviousAmount { get; set; }
    public decimal CurrentAmount { get; set; }
    public decimal CumulativeAmount { get; set; }

    public string? Notes { get; set; }
}

/// <summary>
/// Taşeron hakedişinin kesinti kalemi.
///
/// Hangi kalemlerin açılacağını SÖZLEŞMENİN KAPSAM TİKLERİ belirler;
/// tutar ise yansıtma motorundan ya da bordrodan ÖNERİ olarak gelir ve
/// elle düzeltilebilir. Düzeltilen kalem
/// (<see cref="IsManualAmount"/>) bir daha öneriyle ezilmez.
/// </summary>
public sealed class SubcontractorProgressPaymentDeduction : BaseEntity
{
    public Guid SubcontractorProgressPaymentId { get; set; }
    public SubcontractorProgressPayment SubcontractorProgressPayment { get; set; }
        = null!;

    public int LineNumber { get; set; }

    /// <summary><see cref="HakedisDeductionType"/> ordinali.</summary>
    public int DeductionType { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Rate { get; set; }
    public decimal CumulativeBaseAmount { get; set; }
    public decimal PreviousAmount { get; set; }
    public decimal CumulativeAmount { get; set; }
    public decimal Amount { get; set; }

    public bool IsManualAmount { get; set; }

    /// <summary>
    /// Önerinin nasıl hesaplandığı ("İşveren İSG kesintisi 10.000,00 ×
    /// (12 taşeron işçisi / 40 şantiye işçisi)"). Kullanıcı rakamı
    /// görmeden onaylamak zorunda kalmasın diye saklanıyor.
    /// </summary>
    public string? SuggestionBasis { get; set; }
}
