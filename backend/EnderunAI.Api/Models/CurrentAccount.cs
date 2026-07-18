namespace EnderunAI.Api.Models;

[Flags]
public enum CurrentAccountRoles
{
    None = 0,
    Customer = 1,
    Supplier = 2,
    Subcontractor = 4,
    OfficialInstitution = 8,
    Bank = 16,
    ServiceCompany = 32,
    RentalCompany = 64,
    Other = 128
}

public enum CurrentAccountStatus
{
    Draft = 0,
    PendingApproval = 1,
    Approved = 2,
    Suspended = 3,
    Passive = 4
}

public sealed class CurrentAccount : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? ShortName { get; set; }

    public CurrentAccountRoles Roles { get; set; }
    public CurrentAccountStatus Status { get; set; } = CurrentAccountStatus.Draft;

    public string? TaxOffice { get; set; }
    public string? TaxNumber { get; set; }
    public string? MersisNumber { get; set; }

    public string? AuthorizedPerson { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }

    public string? PaymentTerm { get; set; }
    public decimal? CreditLimit { get; set; }

    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }

    public ICollection<Project> EmployerProjects { get; set; } = new List<Project>();
}
