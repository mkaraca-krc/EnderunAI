namespace EnderunAI.Api.Models;

public enum ProjectStatus
{
    Draft = 0,
    PendingApproval = 1,
    Active = 2,
    Suspended = 3,
    Completed = 4,
    Cancelled = 5,
    Archived = 6
}

public enum ProjectHealthStatus
{
    Green = 0,
    Yellow = 1,
    Red = 2
}

public sealed class Project : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public Guid EmployerCurrentAccountId { get; set; }
    public CurrentAccount EmployerCurrentAccount { get; set; } = null!;

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public string? ContractNumber { get; set; }
    public DateTime? ContractDate { get; set; }
    public decimal? ContractAmount { get; set; }

    public string CurrencyCode { get; set; } = "TRY";
    public decimal VatRate { get; set; }

    /// <summary>
    /// Sözleşme/proje artış yüzdesi.
    /// Örnek: yüzde 10 için 10.00.
    /// </summary>
    public decimal IncreaseRate { get; set; }

    /// <summary>
    /// Nakit teminat kesintisi yüzdesi.
    /// Örnek: yüzde 5 için 5.00.
    /// </summary>
    public decimal CashRetentionRate { get; set; }

    /// <summary>
    /// Stopaj kesintisi yüzdesi.
    /// Örnek: yüzde 3 için 3.00.
    /// </summary>
    public decimal WithholdingTaxRate { get; set; }

    /// <summary>
    /// Malzeme kesintisi yüzdesi.
    /// Örnek: yüzde 10 için 10.00.
    /// </summary>
    public decimal MaterialDeductionRate { get; set; }
    public string? WithholdingRate { get; set; }

    public DateTime? PlannedStartDate { get; set; }
    public DateTime? PlannedEndDate { get; set; }
    public DateTime? ActualStartDate { get; set; }
    public DateTime? ActualEndDate { get; set; }

    public string? City { get; set; }
    public string? District { get; set; }
    public string? Address { get; set; }

    public ProjectStatus Status { get; set; } = ProjectStatus.Draft;
    public ProjectHealthStatus HealthStatus { get; set; } = ProjectHealthStatus.Green;
    public string? HealthReason { get; set; }

    public Guid? ProjectManagerUserId { get; set; }

    public ICollection<Warehouse> Warehouses { get; set; } = new List<Warehouse>();
}
