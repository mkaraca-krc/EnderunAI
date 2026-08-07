namespace EnderunAI.Api.Models;

/// <summary>Taşeron hesabındaki hareket türü.</summary>
public enum SubcontractorLedgerKind
{
    /// <summary>Hakediş ödemesi.</summary>
    Payment = 0,

    /// <summary>Avans — sonraki hakedişlerden mahsup edilir.</summary>
    Advance = 1
}

/// <summary>
/// Taşerona yapılan RESMÎ (faturalı) ödeme ve verilen resmî avans.
///
/// Bu tablo <c>subcontractor.view</c> ile okunur; muhasebeye ve proje
/// maliyetine girer.
/// </summary>
public sealed class SubcontractorLedgerEntry : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid SubcontractorContractId { get; set; }
    public SubcontractorContract SubcontractorContract { get; set; } = null!;

    /// <summary>
    /// Ödemenin karşılığı olan hakediş. Avansta boştur — avans
    /// hakedişten önce verilir.
    /// </summary>
    public Guid? SubcontractorProgressPaymentId { get; set; }
    public SubcontractorProgressPayment? SubcontractorProgressPayment { get; set; }

    public SubcontractorLedgerKind Kind { get; set; }

    public DateTime EntryDate { get; set; }

    /// <summary>KDV hariç tutar.</summary>
    public decimal Amount { get; set; }

    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }

    /// <summary>
    /// Yapım işleri KDV tevkifatı — alıcı sıfatıyla bizim beyan edip
    /// ödeyeceğimiz kısım. Oran sözleşmeden gelir; her faturada elle
    /// girilseydi aynı taşeronun iki faturası farklı oranla
    /// muhasebeleşir ve KDV beyanı tutmazdı.
    /// </summary>
    public decimal WithholdingAmount { get; set; }

    /// <summary>Taşerona fiilen ödenecek tutar (tevkifat düşülmüş).</summary>
    public decimal PayableAmount { get; set; }

    public string CurrencyCode { get; set; } = "TRY";

    /// <summary>Varsa bağlı tedarikçi faturası.</summary>
    public Guid? SupplierInvoiceId { get; set; }

    public string? Description { get; set; }
}

/// <summary>
/// Taşerona ELDEN yapılan ödeme ve verilen elden avans.
///
/// AYRI TABLO OLMASI BİLİNÇLİ — <see cref="PersonnelExtraPayment"/> ile
/// aynı gerekçe. Bu tutarlar resmî tabloya kolon olarak eklenseydi,
/// taşeron ekranlarını okuyan mevcut uçların projeksiyonlarına farkında
/// olmadan sızardı. Ayrı tablo, <c>extra_payment.view</c> izni olmayan
/// bir kullanıcının sorgusunun buraya HİÇ uğramaması demek.
///
/// Resmî muhasebeye HİÇBİR fiş yazmaz. Proje maliyetine de ayrı bir
/// satır olarak yazılmaz; maliyet ekranında okuma anında ve yetki
/// kontrolüyle eklenir.
/// </summary>
public sealed class SubcontractorCashLedgerEntry : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid SubcontractorContractId { get; set; }
    public SubcontractorContract SubcontractorContract { get; set; } = null!;

    public Guid? SubcontractorProgressPaymentId { get; set; }
    public SubcontractorProgressPayment? SubcontractorProgressPayment { get; set; }

    public SubcontractorLedgerKind Kind { get; set; }

    public DateTime EntryDate { get; set; }

    public decimal Amount { get; set; }

    public string CurrencyCode { get; set; } = "TRY";

    public string? Description { get; set; }
}
