namespace EnderunAI.Api.Models;

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

    public int ItemType { get; set; }

    public string? Category { get; set; }
    public string? Notes { get; set; }
}
