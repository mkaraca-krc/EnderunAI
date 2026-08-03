namespace EnderunAI.Api.Models.PurchaseOrder;

public enum PurchaseOrderStatus
{
    Draft = 0,
    PendingApproval = 1,
    Approved = 2,
    PartiallyReceived = 3,
    Completed = 4,
    Cancelled = 5,
    Rejected = 6
}

public sealed class PurchaseOrder : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid RfqId { get; set; }
    public global::EnderunAI.Api.Models.Rfq.Rfq Rfq { get; set; } = null!;

    public Guid SupplierCurrentAccountId { get; set; }
    public CurrentAccount SupplierCurrentAccount { get; set; } = null!;

    public string OrderNumber { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public DateTime? ExpectedDeliveryDate { get; set; }
    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;
    public string Currency { get; set; } = "TRY";
    public decimal ExchangeRate { get; set; } = 1m;
    public string? PaymentTerm { get; set; }
    public string? DeliveryAddress { get; set; }
    public string? Description { get; set; }
    public string? Notes { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    /// <summary>KDV hariç tutar (Subtotal - DiscountTotal). Tarihsel alan adı korunuyor.</summary>
    public decimal GrandTotal { get; set; }

    /// <summary>Sipariş KDV oranı (%). RFQ zinciri KDV taşımadığı için sipariş seviyesinde tutulur.</summary>
    public decimal VatRate { get; set; }
    /// <summary>GrandTotal × VatRate / 100.</summary>
    public decimal VatAmount { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }
    public Guid? RejectedByUserId { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
    public string? RejectionReason { get; set; }

    public ICollection<PurchaseOrderItem> Items { get; set; } =
        new List<PurchaseOrderItem>();
    public ICollection<global::EnderunAI.Api.Models.GoodsReceipt.GoodsReceipt> GoodsReceipts { get; set; } =
        new List<global::EnderunAI.Api.Models.GoodsReceipt.GoodsReceipt>();
}
