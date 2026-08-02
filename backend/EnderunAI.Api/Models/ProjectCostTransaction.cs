namespace EnderunAI.Api.Models;

public enum ProjectCostType
{
    Material = 0,
    Labor = 1,
    Equipment = 2,
    Subcontractor = 3,
    Overhead = 4,
    Other = 5
}

public sealed class ProjectCostTransaction : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid? ProjectSiteId { get; set; }
    public ProjectSite? ProjectSite { get; set; }

    public ProjectCostType CostType { get; set; }
    public DateTime CostDate { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;

    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
}
