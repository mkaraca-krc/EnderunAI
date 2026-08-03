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

    /// <summary>
    /// Bu carinin muhasebedeki 320 Satıcılar alt hesabı (tedarikçi
    /// faturası fişleri buraya alacak yazar). Boşsa otomatik fişler 320
    /// grup hesabına CurrentAccountId boyutuyla yazılır; ilk fişte isim
    /// eşleşmesi bulunursa buraya kaydedilir.
    /// </summary>
    public Guid? PayableAccountingAccountId { get; set; }
    public AccountingAccount? PayableAccountingAccount { get; set; }

    /// <summary>
    /// Bu carinin muhasebedeki 120 Alıcılar alt hesabı (hakediş fişleri
    /// buraya borç yazar). Boşsa 120 grup hesabı kullanılır.
    /// </summary>
    public Guid? ReceivableAccountingAccountId { get; set; }
    public AccountingAccount? ReceivableAccountingAccount { get; set; }

    public ICollection<Project> EmployerProjects { get; set; } = new List<Project>();
}
