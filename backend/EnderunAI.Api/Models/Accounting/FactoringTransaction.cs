namespace EnderunAI.Api.Models;

/// <summary>
/// Çek kırdırma (faktoring) işlemi. Bir işlem tek çeki kapsar; her çekin
/// kendi vadesi ve dolayısıyla kendi komisyon oranı olduğu için toplu
/// kırdırmalar da çek başına birer kayıt olarak tutulur.
///
/// Kesinti matematiği (hepsi ayrı kalem):
///   Komisyon  = Nominal × KomisyonOranı
///   BSMV      = Komisyon × BSMV Oranı (yasal %5)
///   Masraf    = sabit tutar (dosya/işlem masrafı)
///   Toplam Kesinti = Komisyon + BSMV + Masraf
///   Net       = Nominal − Toplam Kesinti
///
/// Fiş: 102 Bankalar (net, borç) + 780 Finansman Giderleri (kesinti,
/// borç) / 101 Alınan Çekler (nominal, alacak) — dengeli.
/// </summary>
public sealed class FactoringTransaction : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>Sistem içi numara (FAK-2026-000001).</summary>
    public string InternalNumber { get; set; } = string.Empty;

    public Guid ChequeId { get; set; }
    public Cheque Cheque { get; set; } = null!;

    /// <summary>Faktoring şirketinin cari kartı (opsiyonel).</summary>
    public Guid? FactoringCurrentAccountId { get; set; }
    public CurrentAccount? FactoringCurrentAccount { get; set; }

    /// <summary>Net paranın girdiği kasa/banka hesabı.</summary>
    public Guid CashAccountId { get; set; }
    public CashAccount CashAccount { get; set; } = null!;

    /// <summary>Finansman giderinin yükleneceği proje (opsiyonel).</summary>
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public DateTime TransactionDate { get; set; }

    public string CurrencyCode { get; set; } = "TRY";

    /// <summary>Çekin nominal tutarı (işlem anındaki kopyası).</summary>
    public decimal ChequeAmount { get; set; }

    public decimal CommissionRate { get; set; }
    public decimal CommissionAmount { get; set; }

    /// <summary>Banka ve Sigorta Muameleleri Vergisi oranı (varsayılan %5).</summary>
    public decimal BsmvRate { get; set; } = 5m;
    public decimal BsmvAmount { get; set; }

    /// <summary>Dosya/işlem masrafı.</summary>
    public decimal ExpenseAmount { get; set; }

    public decimal TotalDeductionAmount { get; set; }
    public decimal NetAmount { get; set; }

    public string? Description { get; set; }

    /// <summary>Üretilen (Posted) muhasebe fişi.</summary>
    public Guid? AccountingVoucherId { get; set; }
    public AccountingVoucher? AccountingVoucher { get; set; }

    /// <summary>Net tutarın kasa/banka hareketi.</summary>
    public Guid? CashTransactionId { get; set; }
    public CashTransaction? CashTransaction { get; set; }
}
