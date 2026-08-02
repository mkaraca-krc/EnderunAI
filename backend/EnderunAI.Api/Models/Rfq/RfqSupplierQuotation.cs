namespace EnderunAI.Api.Models.Rfq;

public sealed class RfqSupplierQuotation : BaseEntity
{
    public Guid RfqSupplierId { get; set; }
    public RfqSupplier RfqSupplier { get; set; } = null!;

    public string? SupplierQuotationNumber { get; set; }
    public DateTime QuotationDate { get; set; }
    public DateTime? ValidUntil { get; set; }
    public string Currency { get; set; } = "TRY";
    public decimal ExchangeRate { get; set; } = 1m;
    public int? DeliveryDays { get; set; }
    public string? PaymentTerm { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public string? Notes { get; set; }

    public ICollection<RfqSupplierQuotationItem> Items { get; set; } =
        new List<RfqSupplierQuotationItem>();
}

public sealed class RfqSupplierQuotationItem : BaseEntity
{
    public Guid RfqSupplierQuotationId { get; set; }
    public RfqSupplierQuotation RfqSupplierQuotation { get; set; } = null!;

    public Guid RfqItemId { get; set; }
    public RfqItem RfqItem { get; set; } = null!;

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountRate { get; set; }
    public decimal NetUnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public int? DeliveryDays { get; set; }
    public string? Notes { get; set; }
}
