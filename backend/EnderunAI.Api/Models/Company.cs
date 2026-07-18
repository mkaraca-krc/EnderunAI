namespace EnderunAI.Api.Models;

public sealed class Company : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? TradeName { get; set; }

    public string? TaxOffice { get; set; }
    public string? TaxNumber { get; set; }
    public string? MersisNumber { get; set; }

    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? Address { get; set; }

    public string? LogoPath { get; set; }

    public ICollection<Branch> Branches { get; set; } = new List<Branch>();
    public ICollection<CurrentAccount> CurrentAccounts { get; set; } = new List<CurrentAccount>();
    public ICollection<Project> Projects { get; set; } = new List<Project>();
}
