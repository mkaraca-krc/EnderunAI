namespace EnderunAI.Api.Models;

public sealed class AccountingVoucherLine : BaseEntity
{
    public Guid AccountingVoucherId { get; set; }
    public AccountingVoucher AccountingVoucher { get; set; } = null!;

    public Guid AccountingAccountId { get; set; }
    public AccountingAccount AccountingAccount { get; set; } = null!;

    public int LineNumber { get; set; }
    public string? Description { get; set; }

    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }

    public string CurrencyCode { get; set; } = "TRY";
    public decimal ExchangeRate { get; set; } = 1;
    public decimal DebitAmountLocal { get; set; }
    public decimal CreditAmountLocal { get; set; }

    public Guid? CurrentAccountId { get; set; }
    public CurrentAccount? CurrentAccount { get; set; }

    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public string? CostCenterCode { get; set; }
    public string? DocumentNumber { get; set; }
    public DateTime? DocumentDate { get; set; }
    public DateTime? DueDate { get; set; }
}
