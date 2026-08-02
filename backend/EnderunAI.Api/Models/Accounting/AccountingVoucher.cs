namespace EnderunAI.Api.Models;

public enum AccountingVoucherType
{
    Journal = 0,
    Collection = 1,
    Payment = 2,
    Opening = 3,
    Closing = 4
}

public enum AccountingVoucherStatus
{
    Draft = 0,
    Posted = 1,
    Cancelled = 2
}

public sealed class AccountingVoucher : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public string VoucherNumber { get; set; } = string.Empty;
    public AccountingVoucherType VoucherType { get; set; } =
        AccountingVoucherType.Journal;
    public AccountingVoucherStatus Status { get; set; } =
        AccountingVoucherStatus.Draft;

    public DateTime VoucherDate { get; set; } = DateTime.UtcNow.Date;
    public int FiscalYear { get; set; } = DateTime.UtcNow.Year;
    public int FiscalPeriod { get; set; } = DateTime.UtcNow.Month;

    public string CurrencyCode { get; set; } = "TRY";
    public decimal ExchangeRate { get; set; } = 1;

    public string? Description { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? SourceModule { get; set; }
    public Guid? SourceEntityId { get; set; }

    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }

    public DateTime? PostedAtUtc { get; set; }
    public Guid? PostedByUserId { get; set; }

    public DateTime? CancelledAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string? CancellationReason { get; set; }

    public ICollection<AccountingVoucherLine> Lines { get; set; } =
        new List<AccountingVoucherLine>();
}
