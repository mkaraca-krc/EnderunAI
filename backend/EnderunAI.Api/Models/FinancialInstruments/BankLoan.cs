namespace EnderunAI.Api.Models.FinancialInstruments;

public enum BankLoanStatus
{
    /// <summary>Sözleşme var, para henüz çekilmedi.</summary>
    Planned = 0,

    /// <summary>Çekildi, taksitleri ödeniyor.</summary>
    Active = 1,

    /// <summary>Tamamı ödendi.</summary>
    Closed = 2,

    /// <summary>
    /// İPTAL. Nakit akışta ne çekiliş ne taksit sayılır — kapatılan
    /// bir kaydın mali etkisi de kalkmalı (çekteki iptal dersi).
    /// </summary>
    Cancelled = 90
}

/// <summary>
/// Banka kredisi.
///
/// İKİ TARİH: <c>DrawdownDate</c> paranın hesaba girdiği gün (nakit
/// GİRİŞ), taksitlerin <c>DueDate</c>'i paranın çıktığı gün. Kredi
/// tek tarihle tutulsaydı ya girişi ya taksitleri kaybederdik.
///
/// ANAPARA/FAİZ AYRI: taksit tek tutar olarak tutulsaydı nakit akış
/// doğru çıkardı ama gider merkezi faizi gider sayamazdı — anapara
/// geri ödemesi gider değildir, faiz giderdir.
/// </summary>
public sealed class BankLoan : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>Krediyi veren banka carisi; zorunlu değil.</summary>
    public Guid? BankCurrentAccountId { get; set; }
    public CurrentAccount? BankCurrentAccount { get; set; }

    /// <summary>Paranın gireceği/girdiği hesap.</summary>
    public Guid? CashAccountId { get; set; }
    public CashAccount? CashAccount { get; set; }

    /// <summary>Belirli bir projeye tahsis edildiyse.</summary>
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? ContractNumber { get; set; }

    public BankLoanStatus Status { get; set; } = BankLoanStatus.Planned;

    /// <summary>Çekilen anapara.</summary>
    public decimal PrincipalAmount { get; set; }

    /// <summary>Aylık faiz oranı (%).</summary>
    public decimal MonthlyInterestRate { get; set; }

    public int InstallmentCount { get; set; }

    /// <summary>Paranın hesaba girdiği (gireceği) gün.</summary>
    public DateTime DrawdownDate { get; set; }

    /// <summary>İlk taksitin vadesi.</summary>
    public DateTime FirstInstallmentDate { get; set; }

    public string CurrencyCode { get; set; } = "TRY";

    /// <summary>
    /// Çekiliş nakit akışta GİRİŞ olarak sayıldı mı. Para hesaba
    /// girdikten sonra açılış bakiyesinin içindedir; ayrıca giriş
    /// yazılsaydı aynı para iki kez girmiş görünürdü.
    /// </summary>
    public bool IsDrawn { get; set; }

    public string? Notes { get; set; }

    public ICollection<BankLoanInstallment> Installments { get; set; }
        = new List<BankLoanInstallment>();
}

/// <summary>
/// Kredinin tek taksiti. Plan otomatik üretilir ama satır satır
/// DÜZELTİLEBİLİR: bankanın uyguladığı yuvarlama, komisyon ya da
/// erken kapama farkı hesabımıza birebir uymayabilir; plan
/// dokunulmaz olsaydı kullanıcı gerçeği yazamazdı.
/// </summary>
public sealed class BankLoanInstallment : BaseEntity
{
    public Guid BankLoanId { get; set; }
    public BankLoan BankLoan { get; set; } = null!;

    public int Number { get; set; }

    public DateTime DueDate { get; set; }

    public decimal PrincipalAmount { get; set; }
    public decimal InterestAmount { get; set; }

    /// <summary>Anapara + faiz.</summary>
    public decimal TotalAmount => decimal.Round(PrincipalAmount + InterestAmount, 2);

    /// <summary>
    /// Ödendi. Ödenen taksit nakit akışta GELECEK ÇIKIŞ olarak
    /// sayılmaz — parası zaten çıktı ve bakiyenin içinde. Vergi
    /// yükümlülüklerindeki desenin aynısı.
    /// </summary>
    public bool IsPaid { get; set; }
    public DateTime? PaidDate { get; set; }
}
