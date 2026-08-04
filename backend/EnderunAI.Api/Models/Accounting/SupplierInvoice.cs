namespace EnderunAI.Api.Models;

public enum SupplierInvoiceStatus
{
    Draft = 0,
    PendingApproval = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4
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

    /// <summary>Zorunlu — maliyet fişindeki 320/maliyet satırları proje boyutu ister.</summary>
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

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

    public ICollection<SupplierInvoiceItem> Items { get; set; } = new List<SupplierInvoiceItem>();
}

public sealed class SupplierInvoiceItem : BaseEntity
{
    public Guid SupplierInvoiceId { get; set; }
    public SupplierInvoice SupplierInvoice { get; set; } = null!;

    public int LineNumber { get; set; }
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
}
