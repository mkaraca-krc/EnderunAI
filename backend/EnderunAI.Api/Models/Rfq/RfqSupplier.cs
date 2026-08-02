namespace EnderunAI.Api.Models.Rfq;

public enum RfqSupplierStatus
{
    Pending = 0,
    Sent = 1,
    Responded = 2,
    Awarded = 3,
    Rejected = 4
}

public sealed class RfqSupplier : BaseEntity
{
    public Guid RfqId { get; set; }
    public Rfq Rfq { get; set; } = null!;

    public Guid SupplierCurrentAccountId { get; set; }
    public CurrentAccount SupplierCurrentAccount { get; set; } = null!;

    public RfqSupplierStatus Status { get; set; } = RfqSupplierStatus.Pending;
    public DateTime? SentAtUtc { get; set; }
    public DateTime? RespondedAtUtc { get; set; }
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? Notes { get; set; }

    public ICollection<RfqSupplierQuotation> Quotations { get; set; } =
        new List<RfqSupplierQuotation>();
}
