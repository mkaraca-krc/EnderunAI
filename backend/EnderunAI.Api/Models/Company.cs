namespace EnderunAI.Api.Models;

public sealed class Company : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? TradeName { get; set; }

    public string? TaxOffice { get; set; }
    public string? TaxNumber { get; set; }
    public string? MersisNumber { get; set; }
    public string? TradeRegistryNumber { get; set; }

    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? Address { get; set; }

    public string? LogoPath { get; set; }

    public ICollection<Branch> Branches { get; set; } = new List<Branch>();
    public ICollection<CurrentAccount> CurrentAccounts { get; set; } = new List<CurrentAccount>();
    public ICollection<Project> Projects { get; set; } = new List<Project>();
    public ICollection<CompanyBankAccount> BankAccounts { get; set; } = new List<CompanyBankAccount>();
}

public sealed class CompanyBankAccount : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public string BankName { get; set; } = string.Empty;
    public string Iban { get; set; } = string.Empty;
    public string? AccountHolder { get; set; }
    public string? CurrencyCode { get; set; } = "TRY";
}
