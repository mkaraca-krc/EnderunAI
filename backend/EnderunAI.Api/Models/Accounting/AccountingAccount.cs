namespace EnderunAI.Api.Models;

public enum AccountingAccountNature
{
    Debit = 0,
    Credit = 1,
    Both = 2
}

public sealed class AccountingAccount : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid? ParentAccountId { get; set; }
    public AccountingAccount? ParentAccount { get; set; }
    public ICollection<AccountingAccount> ChildAccounts { get; set; } =
        new List<AccountingAccount>();

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public AccountingAccountNature Nature { get; set; } =
        AccountingAccountNature.Debit;

    public int Level { get; set; } = 1;
    public bool IsPostingAllowed { get; set; } = true;
    public bool RequiresProject { get; set; }
    public bool RequiresCostCenter { get; set; }
    public string? CurrencyCode { get; set; }
}
