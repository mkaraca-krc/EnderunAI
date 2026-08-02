namespace EnderunAI.Api.Models;

public enum ProjectMeasurementStatus
{
    Draft = 0,
    PendingApproval = 1,
    Approved = 2,
    TransferredToProgressPayment = 3,
    Cancelled = 4
}

public sealed class ProjectMeasurement : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid ProjectBoqId { get; set; }
    public ProjectBoq ProjectBoq { get; set; } = null!;

    public string MeasurementNumber { get; set; } = string.Empty;
    public DateTime MeasurementDate { get; set; }
    public ProjectMeasurementStatus Status { get; set; } = ProjectMeasurementStatus.Draft;
    public string CurrencyCode { get; set; } = "TRY";
    public decimal TotalAmount { get; set; }

    public string? Description { get; set; }
    public string? Notes { get; set; }
    public string? CancellationReason { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }
    public Guid? SubmittedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? TransferredAtUtc { get; set; }
    public Guid? ProgressPaymentId { get; set; }

    public ICollection<ProjectMeasurementItem> Items { get; set; }
        = new List<ProjectMeasurementItem>();
}

public sealed class ProjectMeasurementItem : BaseEntity
{
    public Guid ProjectMeasurementId { get; set; }
    public ProjectMeasurement ProjectMeasurement { get; set; } = null!;

    public Guid ProjectBoqItemId { get; set; }
    public ProjectBoqItem ProjectBoqItem { get; set; } = null!;

    public Guid? EngineeringPositionId { get; set; }

    public int LineNumber { get; set; }
    public string PositionCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;

    public decimal ContractQuantity { get; set; }
    public decimal PreviousQuantity { get; set; }
    public decimal CurrentQuantity { get; set; }
    public decimal CumulativeQuantity { get; set; }
    public decimal RemainingQuantity { get; set; }

    public decimal UnitPrice { get; set; }
    public decimal CurrentAmount { get; set; }
    public decimal CumulativeAmount { get; set; }
    public decimal CompletionRate { get; set; }

    public string? MeasurementReference { get; set; }
    public string? Location { get; set; }
    public string? Block { get; set; }
    public string? Floor { get; set; }
    public string? Room { get; set; }
    public string? Notes { get; set; }
}
