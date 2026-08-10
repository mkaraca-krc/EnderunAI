namespace EnderunAI.Api.Models;

public enum ProgressPaymentStatus
{
    Draft = 0,
    PendingApproval = 1,
    Approved = 2,
    Posted = 3,
    Cancelled = 4
}

public sealed class ProgressPayment : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid? ProjectMeasurementId { get; set; }

    public string ProgressPaymentNumber { get; set; } = string.Empty;
    public int PeriodNumber { get; set; }

    public DateTime? PeriodStartDate { get; set; }
    public DateTime? PeriodEndDate { get; set; }
    public DateTime ProgressPaymentDate { get; set; }

    /// <summary>
    /// Beklenen tahsilat tarihi — OPSİYONEL EZME.
    /// 
    /// Doluysa projedeki vade gününün önüne geçer ve kalem KESİN
    /// sayılır: işverenle konuşulmuş bir tarih, formülle üretilmiş bir
    /// tahminden daha iyi bilgidir. Boşsa proje vadesinden hesaplanır
    /// ve TAHMİNİ kalır.
    /// </summary>
    public DateTime? ExpectedCollectionDate { get; set; }

    public ProgressPaymentStatus Status { get; set; }
        = ProgressPaymentStatus.Draft;

    public string CurrencyCode { get; set; } = "TRY";

    public decimal ContractAmount { get; set; }
    public decimal PreviousAmount { get; set; }
    public decimal CurrentAmount { get; set; }
    public decimal CumulativeAmount { get; set; }

    /// <summary>Kümülatif imalat tutarı (ihzarat hariç).</summary>
    public decimal CumulativeWorkAmount { get; set; }

    /// <summary>
    /// Kümülatif AÇIK ihzarat tutarı — mahsup edilenler düşülmüş hali.
    /// İhzarat imalata dönüştükçe bu düşer, imalat artar; toplam sabit
    /// kalır ve aynı iş iki kez tahsil edilemez.
    /// </summary>
    public decimal CumulativeAdvanceMaterialAmount { get; set; }

    public decimal PriceDifferenceAmount { get; set; }

    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }

    public int WithholdingNumerator { get; set; }
    public int WithholdingDenominator { get; set; }
    public decimal WithholdingAmount { get; set; }

    /// <summary>Gelir/kurumlar stopajı oranı (%). Opsiyonel; 0 ise uygulanmaz.</summary>
    public decimal IncomeTaxWithholdingRate { get; set; }

    /// <summary>Stopaj tutarı — matrahı KDV hariç hakediş tutarıdır.</summary>
    public decimal IncomeTaxWithholdingAmount { get; set; }

    public decimal TotalDeductionAmount { get; set; }
    public decimal GrossPayableAmount { get; set; }
    public decimal NetPayableAmount { get; set; }

    public string? Description { get; set; }
    public string? Notes { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? PostedAtUtc { get; set; }

    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }

    /// <summary>
    /// Kesinleştirmede (Post) otomatik üretilen gelir fişi:
    /// 120 Alıcılar (borç) / 600 Yurtiçi Satışlar + 391 Hesaplanan KDV
    /// (alacak), kesintiler kendi hesaplarına borç.
    /// </summary>
    public Guid? AccountingVoucherId { get; set; }
    public AccountingVoucher? AccountingVoucher { get; set; }

    public ICollection<ProgressPaymentSection> Sections { get; set; }
        = new List<ProgressPaymentSection>();

    public ICollection<ProgressPaymentItem> Items { get; set; }
        = new List<ProgressPaymentItem>();

    public ICollection<ProgressPaymentDeduction> Deductions { get; set; }
        = new List<ProgressPaymentDeduction>();

    /// <summary>Bu hakedişte açılan ihzarat kalemleri.</summary>
    public ICollection<ProgressPaymentAdvanceMaterial> AdvanceMaterials { get; set; }
        = new List<ProgressPaymentAdvanceMaterial>();

    /// <summary>Bu hakedişte yapılan ihzarat mahsupları.</summary>
    public ICollection<ProgressPaymentAdvanceMaterialOffset> AdvanceMaterialOffsets { get; set; }
        = new List<ProgressPaymentAdvanceMaterialOffset>();

    /// <summary>Ödeme dağılımı: nakit + vadeli çekler.</summary>
    public ICollection<ProgressPaymentPaymentPlan> PaymentPlans { get; set; }
        = new List<ProgressPaymentPaymentPlan>();
}

public sealed class ProgressPaymentItem : BaseEntity
{
    public Guid ProgressPaymentId { get; set; }
    public ProgressPayment ProgressPayment { get; set; } = null!;

    public Guid? EngineeringPositionId { get; set; }

    /// <summary>Pozun ait olduğu imalat bölümü; boşsa bölümsüz.</summary>
    public Guid? ProgressPaymentSectionId { get; set; }
    public ProgressPaymentSection? Section { get; set; }

    /// <summary>
    /// Satırın geldiği sözleşme icmali kalemi. Opsiyonel: icmalsiz
    /// projede ve icmal kurulmadan önce açılmış hakedişlerde boştur;
    /// o durumda eşleşme poz koduyla yapılır.
    /// </summary>
    public Guid? ProjectBoqItemId { get; set; }
    public ProjectBoqItem? ProjectBoqItem { get; set; }

    /// <summary>
    /// Bu dönemde SAHANIN yaptığı miktar — hakediş kesinleşirken o anki
    /// onaylı günlük raporlardan yazılıp DONDURULUR.
    ///
    /// <see cref="CurrentQuantity"/> işverenle mutabık kalınan resmî
    /// rakamdır ve bundan farklı olabilir; ikisi bilerek ayrı tutulur.
    /// Dondurulmasının nedeni: hakediş kesinleştikten sonra onaylanan
    /// geç bir günlük rapor, muhasebeleşmiş belgenin fark raporunu
    /// geriye dönük değiştirmemeli.
    /// </summary>
    public decimal FieldQuantity { get; set; }

    /// <summary>Bu döneme kadar sahada biriken toplam (dondurulmuş).</summary>
    public decimal CumulativeFieldQuantity { get; set; }

    public int LineNumber { get; set; }

    public string PositionCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;

    public decimal ContractQuantity { get; set; }
    public decimal PreviousQuantity { get; set; }
    public decimal CurrentQuantity { get; set; }
    public decimal CumulativeQuantity { get; set; }

    // --- Birim fiyat bileşenleri ---
    // NATURA icmalindeki malzeme / montaj / GG&K kolonları bu ayrımdan
    // çıkar. UnitPrice üçünün toplamıdır ve türetilmiş değerdir; mevcut
    // muhasebe ve raporlama kodu onun üzerinden çalışmaya devam eder.

    public decimal MaterialUnitPrice { get; set; }
    public decimal LaborUnitPrice { get; set; }
    public decimal OverheadUnitPrice { get; set; }

    public decimal UnitPrice { get; set; }

    /// <summary>Bu dönem malzeme tutarı (miktar × malzeme birim fiyatı).</summary>
    public decimal MaterialAmount { get; set; }

    /// <summary>Bu dönem montaj/işçilik tutarı.</summary>
    public decimal LaborAmount { get; set; }

    /// <summary>Bu dönem genel gider ve kâr tutarı.</summary>
    public decimal OverheadAmount { get; set; }

    public decimal PreviousAmount { get; set; }
    public decimal CurrentAmount { get; set; }
    public decimal CumulativeAmount { get; set; }

    public decimal CompletionRate { get; set; }

    public string? MeasurementReference { get; set; }
    public string? Notes { get; set; }
}

public sealed class ProgressPaymentDeduction : BaseEntity
{
    public Guid ProgressPaymentId { get; set; }
    public ProgressPayment ProgressPayment { get; set; } = null!;

    public int LineNumber { get; set; }

    /// <summary>
    /// Kesinti türü. int olarak saklanır (mevcut kayıtlar bozulmasın);
    /// anlamı <see cref="HakedisDeductionType"/> içinde.
    /// </summary>
    public int DeductionType { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Rate { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal Amount { get; set; }

    // --- Kümülatif takip ---
    // Kesinti "bu dönem tutarı × oran" ile değil, "kümülatif taban ×
    // oran − önceki kesintiler" ile bulunur. Oran dönemler arasında
    // değişse bile toplam doğru kalır.

    /// <summary>Kesintinin uygulandığı kümülatif taban.</summary>
    public decimal CumulativeBaseAmount { get; set; }

    /// <summary>Önceki hakedişlerde bu türden kesilen toplam.</summary>
    public decimal PreviousAmount { get; set; }

    /// <summary>Bu hakediş dahil kümülatif kesinti toplamı.</summary>
    public decimal CumulativeAmount { get; set; }

    /// <summary>
    /// Alt kalemler (yemek: kahvaltı/öğlen/akşam/kumanya, konaklama:
    /// yatılı/evci). Doluysa kesinti tutarı oranla değil bunlardan
    /// hesaplanır.
    /// </summary>
    public ICollection<ProgressPaymentDeductionLine> Lines { get; set; }
        = new List<ProgressPaymentDeductionLine>();

    public bool IsManualAmount { get; set; }

    /// <summary>
    /// Bu kesintinin borç yazılacağı muhasebe hesabı (ör. teminat için
    /// 126, stopaj için 193). Boşsa şirket finans ayarındaki varsayılan
    /// kesinti hesabı kullanılır.
    /// </summary>
    public Guid? AccountingAccountId { get; set; }
    public AccountingAccount? AccountingAccount { get; set; }

    public string? Notes { get; set; }
}
