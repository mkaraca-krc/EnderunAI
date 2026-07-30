namespace EnderunAI.Api.Models;

public sealed class ProjectBoq : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public string BoqNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public int RevisionNumber { get; set; }
    public int Status { get; set; }

    public bool IsCurrentRevision { get; set; }

    public string CurrencyCode { get; set; } = "TRY";
    public decimal TotalAmount { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }

    public string? Description { get; set; }
    public string? Notes { get; set; }

    public ICollection<ProjectBoqItem> Items { get; set; } =
        new List<ProjectBoqItem>();
}
