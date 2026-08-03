namespace EnderunAI.Api.Models;

public enum CashAccountType
{
    /// <summary>Kasa — muhasebede 100 Kasa altında.</summary>
    Cash = 0,
    /// <summary>Banka hesabı — muhasebede 102 Bankalar altında.</summary>
    Bank = 1
}

/// <summary>
/// Kasa ve banka hesapları tek modelde toplandı: çek tahsili, faktoring
/// ödemesi ve nakit akışı her ikisiyle de aynı şekilde çalıştığı için
/// ayrı iki varlık gereksiz tekrar üretiyordu. (Eski BankAccounts /
/// BankTransactions tabloları buraya taşındı ve artık kullanılmıyor.)
/// </summary>
public sealed class CashAccount : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public CashAccountType Type { get; set; } = CashAccountType.Cash;

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Yalnızca banka hesapları için.</summary>
    public string? BankName { get; set; }
    public string? Iban { get; set; }

    public string CurrencyCode { get; set; } = "TRY";
    public decimal OpeningBalance { get; set; }

    /// <summary>Bu hesabın muhasebe karşılığı (100.x veya 102.x).</summary>
    public Guid AccountingAccountId { get; set; }
    public AccountingAccount AccountingAccount { get; set; } = null!;
}

public enum CashTransactionDirection
{
    /// <summary>Giren para (tahsilat).</summary>
    In = 0,
    /// <summary>Çıkan para (ödeme).</summary>
    Out = 1
}

public enum CashTransactionType
{
    /// <summary>Cariden tahsilat — 120 Alıcılar kapanır.</summary>
    Collection = 0,
    /// <summary>Cariye ödeme — 320 Satıcılar kapanır.</summary>
    Payment = 1,
    /// <summary>Alınan çekin tahsili — 101 Alınan Çekler kapanır.</summary>
    ChequeCollection = 2,
    /// <summary>Verilen çekin ödenmesi — 103 Verilen Çekler kapanır.</summary>
    ChequePayment = 3,
    /// <summary>Faktoring net tahsilatı.</summary>
    Factoring = 4
}

public sealed class CashTransaction : BaseEntity
{
    public Guid CashAccountId { get; set; }
    public CashAccount CashAccount { get; set; } = null!;

    public DateTime TransactionDate { get; set; }
    public CashTransactionType TransactionType { get; set; }
    public CashTransactionDirection Direction { get; set; }

    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "TRY";

    public string Description { get; set; } = string.Empty;
    public string? DocumentNumber { get; set; }

    public Guid? CurrentAccountId { get; set; }
    public CurrentAccount? CurrentAccount { get; set; }

    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public string? SourceModule { get; set; }
    public Guid? SourceEntityId { get; set; }

    /// <summary>Bu hareketle birlikte üretilen (Posted) muhasebe fişi.</summary>
    public Guid? AccountingVoucherId { get; set; }
    public AccountingVoucher? AccountingVoucher { get; set; }
}
