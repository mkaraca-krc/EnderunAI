namespace EnderunAI.Api.Models;

public sealed class PurchaseRequestItem : BaseEntity
{
    public Guid PurchaseRequestId { get; set; }
    public PurchaseRequest PurchaseRequest { get; set; } = null!;

    public int LineNumber { get; set; }

    public string MaterialDescription { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;

    public DateTime? RequestedDeliveryDate { get; set; }
    public string? Notes { get; set; }
}
