namespace EnderunAI.Api.Models;

public enum SalesInvoiceStatus
{
    Draft = 0,

    /// <summary>Kesinleşti — gelir fişi üretildi, artık değiştirilemez.</summary>
    Posted = 1,

    Cancelled = 2
}

/// <summary>
/// Hakediş DIŞI satış faturası.
///
/// Hakediş de bir gelir belgesidir ama sözleşmeye, pursantaja ve
/// kesintilere bağlıdır. Malzeme satışı gibi tek seferlik satışların
/// oraya sıkıştırılması yanlış olurdu; bu yüzden ayrı bir belge.
/// Muhasebe mantığı aynı (120 / 600 / 391) ama fişi ayrı bir metot
/// üretir — iki akış birbirine karışmamalı.
/// </summary>
public sealed class SalesInvoice : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid CustomerCurrentAccountId { get; set; }
    public CurrentAccount CustomerCurrentAccount { get; set; } = null!;

    /// <summary>Satış bir projeye aitse maliyet/gelir oraya bağlanır.</summary>
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    /// <summary>Sistem içi numara (SAT-2026-000001).</summary>
    public string InternalNumber { get; set; } = string.Empty;

    /// <summary>
    /// GİB/entegratör fatura numarası. İçe aktarmada XML'deki numara
    /// buraya yazılır; elle kesilende sonradan doldurulabilir.
    /// İki numara birbirine karışmasın diye ayrı tutuluyor.
    /// </summary>
    public string? OfficialInvoiceNumber { get; set; }

    public DateTime InvoiceDate { get; set; }
    public DateTime? DueDate { get; set; }

    public string CurrencyCode { get; set; } = "TRY";
    public decimal ExchangeRate { get; set; } = 1m;

    public decimal Subtotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }

    /// <summary>
    /// KDV tevkifatı — alıcının beyan edeceği kısım. Bizim beyan
    /// ettiğimiz KDV bu kadar azalır ve tahsil edilecek tutar düşer.
    /// </summary>
    public decimal WithholdingAmount { get; set; }

    /// <summary>Tahsil edilecek: GrandTotal − WithholdingAmount.</summary>
    public decimal NetReceivableAmount { get; set; }

    public string? Description { get; set; }
    public string? Notes { get; set; }

    public SalesInvoiceStatus Status { get; set; } = SalesInvoiceStatus.Draft;

    public DateTime? PostedAtUtc { get; set; }
    public Guid? PostedByUserId { get; set; }

    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }

    /// <summary>Kesinleştirmede üretilen gelir fişi.</summary>
    public Guid? AccountingVoucherId { get; set; }
    public AccountingVoucher? AccountingVoucher { get; set; }

    /// <summary>
    /// İADE FATURASI (müşteriden mal iadesi). Kesinleştiğinde fiş
    /// 610 Satıştan İadeler + 391 borç / 120 alacak olarak aynalanır —
    /// gelir hesabı 600 borçlandırılmaz, iadeler kendi hesabında
    /// toplanır ki brüt satış rakamı bozulmasın.
    /// </summary>
    public bool IsReturn { get; set; }

    public Guid? OriginalInvoiceId { get; set; }
    public SalesInvoice? OriginalInvoice { get; set; }

    /// <summary>Kesinleşmiş faturanın iptalinde üretilen ters fiş.</summary>
    public Guid? ReversalVoucherId { get; set; }
    public AccountingVoucher? ReversalVoucher { get; set; }

    // --- E-fatura içe aktarma izi ---

    public string? SourceXmlPath { get; set; }
    public EInvoiceParseSource? ParseSource { get; set; }
    public bool RequiresManualReview { get; set; }

    public ICollection<SalesInvoiceItem> Items { get; set; }
        = new List<SalesInvoiceItem>();
}

public sealed class SalesInvoiceItem : BaseEntity
{
    public Guid SalesInvoiceId { get; set; }
    public SalesInvoice SalesInvoice { get; set; } = null!;

    public int LineNumber { get; set; }

    public string Description { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal VatRate { get; set; }

    /// <summary>Miktar × birim fiyat (KDV hariç).</summary>
    public decimal LineSubtotal { get; set; }
    public decimal VatAmount { get; set; }

    /// <summary>LineSubtotal + VatAmount.</summary>
    public decimal LineTotal { get; set; }

    /// <summary>İade kaleminin iade ettiği orijinal kalem.</summary>
    public Guid? OriginalItemId { get; set; }
    public SalesInvoiceItem? OriginalItem { get; set; }
}
