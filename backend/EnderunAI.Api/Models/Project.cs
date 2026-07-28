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
    public decimal VatRate { get; set; } = 20;
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
    public ICollection<ProjectHierarchyLevel> HierarchyLevels { get; set; }
        = new List<ProjectHierarchyLevel>();
    public ICollection<ProjectHierarchyNode> HierarchyNodes { get; set; }
        = new List<ProjectHierarchyNode>();
    public ICollection<ProjectModuleScope> ModuleScopes { get; set; }
        = new List<ProjectModuleScope>();
}
