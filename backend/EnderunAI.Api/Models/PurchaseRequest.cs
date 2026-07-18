namespace EnderunAI.Api.Models;

public enum PurchaseRequestPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

public enum PurchaseRequestStatus
{
    Draft = 0,
    Submitted = 1,
    Approved = 2,
    Quotation = 3,
    Ordered = 4,
    Completed = 5,
    Cancelled = 6,
    Rejected = 7
}

public sealed class PurchaseRequest : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public string RequestNumber { get; set; } = string.Empty;
    public DateTime RequestDate { get; set; } = DateTime.UtcNow.Date;
    public DateTime? NeededByDate { get; set; }

    public string RequestedByName { get; set; } = string.Empty;
    public Guid? RequestedByUserId { get; set; }

    public string? Description { get; set; }

    public PurchaseRequestPriority Priority { get; set; }
        = PurchaseRequestPriority.Normal;

    public PurchaseRequestStatus Status { get; set; }
        = PurchaseRequestStatus.Draft;

    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }

    public Guid? CancelledByUserId { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }

    public ICollection<PurchaseRequestItem> Items { get; set; }
        = new List<PurchaseRequestItem>();
}
