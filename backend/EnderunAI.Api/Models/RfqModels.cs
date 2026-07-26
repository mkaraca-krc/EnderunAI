namespace EnderunAI.Api.Models;

public enum RfqStatus
{
    Draft = 0,
    Published = 1,
    CollectingOffers = 2,
    Evaluating = 3,
    Awarded = 4,
    Cancelled = 5
}

public enum FreightResponsibility
{
    Supplier = 0,
    Buyer = 1,
    Shared = 2
}

public sealed class Rfq : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? PurchaseRequestId { get; set; }
    public string RfqNumber { get; set; } = string.Empty;
    public DateTime RfqDateUtc { get; set; } = DateTime.UtcNow;
    public DateTime? OfferDeadlineUtc { get; set; }
    public RfqStatus Status { get; set; } = RfqStatus.Draft;
    public string CurrencyCode { get; set; } = "TRY";
    public string? Description { get; set; }

    public ICollection<RfqItem> Items { get; set; } = new List<RfqItem>();
    public ICollection<SupplierOffer> Offers { get; set; } = new List<SupplierOffer>();
}

public sealed class RfqItem : BaseEntity
{
    public Guid RfqId { get; set; }
    public Rfq Rfq { get; set; } = null!;
    public Guid MaterialId { get; set; }
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "Adet";
    public DateTime? RequiredDateUtc { get; set; }
    public string? Description { get; set; }
}

public sealed class SupplierOffer : BaseEntity
{
    public Guid RfqId { get; set; }
    public Rfq Rfq { get; set; } = null!;
    public Guid SupplierCurrentAccountId { get; set; }
    public string OfferNumber { get; set; } = string.Empty;
    public DateTime OfferDateUtc { get; set; } = DateTime.UtcNow;
    public string CurrencyCode { get; set; } = "TRY";
    public decimal ExchangeRate { get; set; } = 1m;
    public decimal DiscountRate { get; set; }
    public decimal FreightAmount { get; set; }
    public FreightResponsibility FreightResponsibility { get; set; }
    public int PaymentTermDays { get; set; }
    public int DeliveryTermDays { get; set; }
    public bool AllowsPartialShipment { get; set; }
    public decimal SupplierPerformanceScore { get; set; } = 50m;
    public string? Notes { get; set; }

    public ICollection<SupplierOfferItem> Items { get; set; } = new List<SupplierOfferItem>();
    public ICollection<SupplierOfferCheckTerm> CheckTerms { get; set; } = new List<SupplierOfferCheckTerm>();
}

public sealed class SupplierOfferItem : BaseEntity
{
    public Guid SupplierOfferId { get; set; }
    public SupplierOffer SupplierOffer { get; set; } = null!;
    public Guid RfqItemId { get; set; }
    public Guid MaterialId { get; set; }
    public decimal OfferedQuantity { get; set; }
    public decimal AvailableStockQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public int ItemDeliveryDays { get; set; }
}

public sealed class SupplierOfferCheckTerm : BaseEntity
{
    public Guid SupplierOfferId { get; set; }
    public SupplierOffer SupplierOffer { get; set; } = null!;
    public DateTime DueDateUtc { get; set; }
    public decimal Amount { get; set; }
    public int SequenceNo { get; set; }
}
