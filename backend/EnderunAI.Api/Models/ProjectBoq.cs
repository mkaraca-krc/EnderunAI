namespace EnderunAI.Api.Models;

public enum ProjectBoqStatus
{
    Draft = 0,
    Approved = 1,
    Superseded = 2,
    Archived = 3
}

public enum ProjectBoqItemType
{
    Mixed = 0,
    Material = 1,
    Labor = 2
}

public sealed class ProjectBoq : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public string BoqNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int RevisionNumber { get; set; } = 1;
    public ProjectBoqStatus Status { get; set; } = ProjectBoqStatus.Draft;
    public bool IsCurrentRevision { get; set; } = true;
    public string CurrencyCode { get; set; } = "TRY";
    public decimal TotalAmount { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }

    public string? Description { get; set; }
    public string? Notes { get; set; }

    public ICollection<ProjectBoqItem> Items { get; set; } = new List<ProjectBoqItem>();
}

public sealed class ProjectBoqItem : BaseEntity
{
    public Guid ProjectBoqId { get; set; }
    public ProjectBoq ProjectBoq { get; set; } = null!;

    public Guid? EngineeringPositionId { get; set; }

    public int LineNumber { get; set; }
    public string PositionCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;

    public decimal ContractQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalAmount { get; set; }

    public ProjectBoqItemType ItemType { get; set; } = ProjectBoqItemType.Mixed;
    public string? Category { get; set; }
    public string? Notes { get; set; }
}
