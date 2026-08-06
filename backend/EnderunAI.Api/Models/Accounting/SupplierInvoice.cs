namespace EnderunAI.Api.Models;

public enum SupplierInvoiceStatus
{
    Draft = 0,
    PendingApproval = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4
}

/// <summary>
/// Faturanın ne için kesildiği. Muhasebe fişi ve stok etkisi buna göre
/// değişir; ikisi tek akışta birleştirilemez çünkü alışta stok hesabı
/// ve depo, giderde gider hesabı ve masraf merkezi gerekir.
/// </summary>
public enum SupplierInvoiceType
{
    /// <summary>
    /// Alış: stok kartına bağlı malzeme girişi. Varsayılan — tip alanı
    /// eklenmeden önce girilmiş faturalar bugünkü davranışını korusun.
    /// </summary>
    Stock = 0,

    /// <summary>Gider: elektrik, kira, müşavirlik gibi; stoğa girmez.</summary>
    Expense = 1
}

public enum SupplierInvoiceMatchStatus
{
    /// <summary>Sipariş/mal kabul bağlantısı yok — 3 yönlü kontrol uygulanmadı.</summary>
    NotApplicable = 0,
    /// <summary>Sipariş = mal kabul = fatura, tolerans içinde.</summary>
    Matched = 1,
    /// <summary>Tolerans dışı fark — GM onayı gerekir.</summary>
    ToleranceExceeded = 2
}

/// <summary>
/// Tedarikçi (alış) faturası. Onaylandığında otomatik, dengeli ve
/// doğrudan Posted bir muhasebe fişi üretir: 320 Satıcılar (alacak),
/// 191 İndirilecek KDV + maliyet hesabı (borç). SourceModule =
/// "SupplierInvoice" olarak fişe işlenir; ayrıca projeye
/// ProjectCostTransaction düşer.
/// </summary>
public sealed class SupplierInvoice : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid SupplierCurrentAccountId { get; set; }
    public CurrentAccount SupplierCurrentAccount { get; set; } = null!;

    public SupplierInvoiceType InvoiceType { get; set; } = SupplierInvoiceType.Stock;

    /// <summary>
    /// OPSİYONEL. Ofis elektriği, kira, müşavirlik gibi giderlerin
    /// gerçekten projesi yoktur; zorunlu tutulsaydı kullanıcı bunları
    /// rastgele bir projeye yazmak zorunda kalır ve o projenin maliyeti
    /// olduğundan yüksek görünürdü. Proje boşken proje maliyet kaydı da
    /// oluşmaz.
    /// </summary>
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    /// <summary>
    /// Faturanın varsayılan masraf merkezi kodu. Merkez giderinde
    /// merkez ofis şubesinin kodu, şantiye giderinde proje kodu.
    /// Kalem kendi kodunu taşıyorsa o geçerlidir.
    /// </summary>
    public string? CostCenterCode { get; set; }

    /// <summary>
    /// ALIŞ faturasında varsayılan depo — kalemlerin çoğu aynı depoya
    /// girer, istisna kalem kendi deposunu taşır.
    /// </summary>
    public Guid? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public Guid? PurchaseOrderId { get; set; }
    public PurchaseOrder.PurchaseOrder? PurchaseOrder { get; set; }

    public Guid? GoodsReceiptId { get; set; }
    public GoodsReceipt.GoodsReceipt? GoodsReceipt { get; set; }

    /// <summary>Sistem içi sıra numarası (SFT-2026-000001).</summary>
    public string InternalNumber { get; set; } = string.Empty;

    /// <summary>Tedarikçinin kendi fatura numarası.</summary>
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public DateTime? DueDate { get; set; }

    public string CurrencyCode { get; set; } = "TRY";
    public decimal ExchangeRate { get; set; } = 1m;

    public decimal Subtotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }

    /// <summary>
    /// KDV tevkifatı — alıcı olarak bizim beyan edip ödeyeceğimiz kısım.
    /// Tevkifatlı faturada ödenecek tutar bu kadar azalır.
    /// </summary>
    public decimal WithholdingAmount { get; set; }

    // --- E-fatura içe aktarma izi ---

    /// <summary>
    /// İçe aktarılan XML'in saklandığı yol. Denetim izi ve orijinal
    /// belgeye erişim için; elle girilen faturada boş.
    /// </summary>
    public string? SourceXmlPath { get; set; }

    /// <summary>Faturayı hangi katman okudu (standart / AI).</summary>
    public EInvoiceParseSource? ParseSource { get; set; }

    /// <summary>
    /// AI ile okunduysa veya tutarlar tutmuyorsa true. Bu faturalar
    /// gözden geçirilmeden onaylanmamalı; arayüz uyarı gösterir.
    /// </summary>
    public bool RequiresManualReview { get; set; }

    public string? Description { get; set; }

    public SupplierInvoiceStatus Status { get; set; } = SupplierInvoiceStatus.Draft;

    public SupplierInvoiceMatchStatus MatchStatus { get; set; } = SupplierInvoiceMatchStatus.NotApplicable;
    /// <summary>Fatura ara toplamı ile sipariş/mal kabul beklenen tutarı arasındaki fark (TRY).</summary>
    public decimal MatchDifferenceAmount { get; set; }
    public string? MatchNote { get; set; }

    /// <summary>Tolerans dışı fark veya GM tutar eşiği aşımı nedeniyle yalnız Admin/GM onaylayabilir.</summary>
    public bool RequiresGmApproval { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? RejectedByUserId { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
    public string? RejectionReason { get; set; }

    /// <summary>Onayda üretilen (Posted) muhasebe fişi.</summary>
    public Guid? AccountingVoucherId { get; set; }
    public AccountingVoucher? AccountingVoucher { get; set; }

    /// <summary>
    /// İADE FATURASI. Tedarikçiye mal iadesinde biz keseriz; muhasebe
    /// fişi orijinalin aynası olur (320 borç / stok-gider + 191 alacak)
    /// ve stok depodan çıkar.
    ///
    /// Ayrı bir tablo yerine aynı tabloda tutuluyor: cari bakiyesi,
    /// liste, onay akışı ve raporlar tek kaynaktan okusun.
    /// </summary>
    public bool IsReturn { get; set; }

    /// <summary>İade faturasında iade edilen orijinal fatura.</summary>
    public Guid? OriginalInvoiceId { get; set; }
    public SupplierInvoice? OriginalInvoice { get; set; }

    /// <summary>
    /// Kesinleşmiş fatura iptal edildiğinde üretilen ters fiş. Orijinal
    /// fiş silinmez; iz kalması için ikisi de defterde durur.
    /// </summary>
    public Guid? ReversalVoucherId { get; set; }
    public AccountingVoucher? ReversalVoucher { get; set; }

    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }

    public ICollection<SupplierInvoiceItem> Items { get; set; } = new List<SupplierInvoiceItem>();
}

public sealed class SupplierInvoiceItem : BaseEntity
{
    public Guid SupplierInvoiceId { get; set; }
    public SupplierInvoice SupplierInvoice { get; set; } = null!;

    public int LineNumber { get; set; }

    /// <summary>
    /// ALIŞ faturasında kalemin stok kartı. Serbest metin yerine karttan
    /// seçilir ki stok, maliyet ve satın alma zinciri aynı kalemi
    /// göstersin.
    /// </summary>
    public Guid? InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }

    /// <summary>Kalemin gireceği depo; boşsa faturanın deposu.</summary>
    public Guid? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    /// <summary>
    /// GİDER faturasında kalemin yazılacağı gider hesabı (770/740 alt
    /// kırılımları). Tek bir varsayılan hesap yerine kalem bazında
    /// seçilir: bir faturada hem akaryakıt hem otoyol gideri olabilir.
    /// </summary>
    public Guid? ExpenseAccountId { get; set; }
    public AccountingAccount? ExpenseAccount { get; set; }

    /// <summary>Kalemin masraf merkezi; boşsa faturanın kodu.</summary>
    public string? CostCenterCode { get; set; }

    /// <summary>
    /// Kalemin gittiği icmal satırı (poz). OPSİYONEL — doldurulursa
    /// maliyet o poza ölçülmüş olarak yazılır, boşsa proje/kısım
    /// düzeyinde kalır.
    /// </summary>
    public Guid? ProjectBoqItemId { get; set; }
    public ProjectBoqItem? ProjectBoqItem { get; set; }

    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }

    public decimal VatRate { get; set; }
    /// <summary>Quantity × UnitPrice (KDV hariç).</summary>
    public decimal LineSubtotal { get; set; }
    public decimal VatAmount { get; set; }
    /// <summary>LineSubtotal + VatAmount.</summary>
    public decimal LineTotal { get; set; }

    public Guid? PurchaseOrderItemId { get; set; }
    public PurchaseOrder.PurchaseOrderItem? PurchaseOrderItem { get; set; }

    /// <summary>
    /// İade kalemi hangi orijinal kalemi iade ediyor. Kısmi iadede
    /// "bu kalemden ne kadarı iade edildi" bu bağdan hesaplanır; yoksa
    /// aynı mal iki kez iade edilebilirdi.
    /// </summary>
    public Guid? OriginalItemId { get; set; }
    public SupplierInvoiceItem? OriginalItem { get; set; }
}
