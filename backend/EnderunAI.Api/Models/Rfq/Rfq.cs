namespace EnderunAI.Api.Models.Rfq;

public enum RfqStatus
{
    Draft = 0,
    Sent = 1,
    ResponsesReceived = 2,
    Awarded = 3,
    Closed = 4,
    Cancelled = 5
}

public sealed class Rfq : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid PurchaseRequestId { get; set; }
    public PurchaseRequest PurchaseRequest { get; set; } = null!;

    public string RfqNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime IssueDate { get; set; }
    public DateTime? ResponseDeadline { get; set; }
    public RfqStatus Status { get; set; } = RfqStatus.Draft;
    public string Currency { get; set; } = "TRY";
    public string? Description { get; set; }
    public string? Notes { get; set; }

    public ICollection<RfqItem> Items { get; set; } = new List<RfqItem>();
    public ICollection<RfqSupplier> Suppliers { get; set; } = new List<RfqSupplier>();
}
