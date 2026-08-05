namespace EnderunAI.Api.Models;

public enum ChequeDirection
{
    /// <summary>Alınan çek — işverenden/müşteriden tahsilat aracı (101).</summary>
    Received = 0,
    /// <summary>Verilen çek — tedarikçiye kendi vade yükümlülüğümüz (103).</summary>
    Issued = 1
}

/// <summary>
/// Çek durumları. 0-4 alınan çek akışı, 10-12 verilen çek akışı için
/// kullanılır; iki akış birbirine karışmaz (geçiş matrisi
/// ChequeService.AllowedTransitions içinde tanımlı).
/// </summary>
public enum ChequeStatus
{
    /// <summary>Portföyde — elimizde duruyor (101.01).</summary>
    Portfolio = 0,
    /// <summary>Bankaya tahsile/teminata verildi (101.02).</summary>
    AtBank = 1,
    /// <summary>Faktoring şirketine kırdırıldı (101.03).</summary>
    AtFactoring = 2,
    /// <summary>Tahsil edildi — para kasaya/bankaya girdi.</summary>
    Collected = 3,
    /// <summary>Karşılıksız çıktı — alacak cariye geri döndü.</summary>
    Bounced = 4,

    /// <summary>Tedarikçiye verildi, vadesi bekleniyor (103).</summary>
    Issued = 10,
    /// <summary>Vadesinde bankadan ödendi.</summary>
    Paid = 11,
    /// <summary>Tedarikçiden geri alındı / iptal edildi.</summary>
    Returned = 12
}

/// <summary>
/// Çift yönlü çek defteri. Alınan çekler işverenden gelen tahsilat
/// aracı (101 Alınan Çekler), verilen çekler tedarikçiye karşı vade
/// yükümlülüğümüz (103 Verilen Çekler). Her durum geçişi bir
/// ChequeMovement satırı ve — muhasebe etkisi varsa — dengeli, doğrudan
/// Posted bir fiş üretir.
/// </summary>
public sealed class Cheque : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public ChequeDirection Direction { get; set; }
    public ChequeStatus Status { get; set; } = ChequeStatus.Portfolio;

    /// <summary>Sistem içi takip numarası (ACK-2026-000001 / VCK-2026-000001).</summary>
    public string InternalNumber { get; set; } = string.Empty;

    /// <summary>Çekin üzerindeki seri/çek numarası.</summary>
    public string ChequeNumber { get; set; } = string.Empty;

    /// <summary>Çekin ait olduğu banka (keşide bankası).</summary>
    public string BankName { get; set; } = string.Empty;
    public string? BankBranch { get; set; }

    /// <summary>Keşideci — alınan çekte çeki yazan taraf, verilen çekte biziz.</summary>
    public string? Drawer { get; set; }

    /// <summary>Alınan çekte çeki veren cari, verilen çekte çeki alan cari.</summary>
    public Guid? CurrentAccountId { get; set; }
    public CurrentAccount? CurrentAccount { get; set; }

    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    /// <summary>
    /// Masraf merkezi kodu — Merkez ofis (şube kodu) ya da şantiye.
    /// Ofis kirası gibi projesi olmayan çekler için var: boş bırakılırsa
    /// fişte proje kodu, o da yoksa şirket kodu kullanılır.
    /// </summary>
    public string? CostCenterCode { get; set; }

    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "TRY";

    /// <summary>Keşide tarihi.</summary>
    public DateTime IssueDate { get; set; }
    /// <summary>Vade — nakit akışı bu tarihe göre hesaplanır.</summary>
    public DateTime DueDate { get; set; }

    /// <summary>Bu çek hangi hakedişin tahsilatı (alınan çek).</summary>
    public Guid? ProgressPaymentId { get; set; }
    public ProgressPayment? ProgressPayment { get; set; }

    /// <summary>Bu çek hangi tedarikçi faturasının ödemesi (verilen çek).</summary>
    public Guid? SupplierInvoiceId { get; set; }
    public SupplierInvoice? SupplierInvoice { get; set; }

    /// <summary>Bankaya/faktoringe verildiğinde ilgili banka hesabı.</summary>
    public Guid? CashAccountId { get; set; }
    public CashAccount? CashAccount { get; set; }

    public string? Description { get; set; }

    public ICollection<ChequeMovement> Movements { get; set; }
        = new List<ChequeMovement>();

    /// <summary>
    /// Proje/masraf merkezi dağılımı. Boşsa çek tek parça işlenir ve
    /// yukarıdaki ProjectId/CostCenterCode geçerlidir.
    /// </summary>
    public ICollection<ChequeAllocation> Allocations { get; set; }
        = new List<ChequeAllocation>();
}

/// <summary>
/// Çekin durum geçmişi. Her satır bir durum geçişini ve (varsa) o
/// geçişte üretilen muhasebe fişini taşır.
/// </summary>
public sealed class ChequeMovement : BaseEntity
{
    public Guid ChequeId { get; set; }
    public Cheque Cheque { get; set; } = null!;

    public DateTime MovementDate { get; set; }

    /// <summary>Geçiş öncesi durum; ilk kayıtta null.</summary>
    public ChequeStatus? FromStatus { get; set; }
    public ChequeStatus ToStatus { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>Geçişte para hareketi olduysa ilgili kasa/banka hesabı.</summary>
    public Guid? CashAccountId { get; set; }
    public CashAccount? CashAccount { get; set; }

    /// <summary>Geçişte üretilen (Posted) muhasebe fişi; etkisiz geçişlerde null.</summary>
    public Guid? AccountingVoucherId { get; set; }
    public AccountingVoucher? AccountingVoucher { get; set; }
}
