namespace EnderunAI.Api.Models.Rfq;

public sealed class RfqItem : BaseEntity
{
    public Guid RfqId { get; set; }
    public Rfq Rfq { get; set; } = null!;

    public Guid? PurchaseRequestItemId { get; set; }
    public PurchaseRequestItem? PurchaseRequestItem { get; set; }

    public int LineNumber { get; set; }
    public string MaterialDescription { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public DateTime? RequestedDeliveryDate { get; set; }
    public string? Notes { get; set; }

    public ICollection<RfqSupplierQuotationItem> QuotationItems { get; set; } =
        new List<RfqSupplierQuotationItem>();
}
