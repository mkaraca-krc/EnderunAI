namespace EnderunAI.Api.Models;

public enum PurchaseRequestStatus
{
    Draft = 0,
    PendingApproval = 1,
    Approved = 2,
    Rejected = 3,
    ConvertedToOrder = 4,
    Cancelled = 5
}

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

public enum GoodsReceiptStatus
{
    Draft = 0,
    Posted = 1,
    Cancelled = 2
}

public sealed class PurchaseRequest : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public string RequestNumber { get; set; } = string.Empty;
    public DateTime RequestDateUtc { get; set; } = DateTime.UtcNow;
    public DateTime? RequiredDateUtc { get; set; }
    public PurchaseRequestStatus Status { get; set; } = PurchaseRequestStatus.Draft;
    public string? Description { get; set; }
    public Guid RequestedByUserId { get; set; }

    public ICollection<PurchaseRequestItem> Items { get; set; } = new List<PurchaseRequestItem>();
}

public sealed class PurchaseRequestItem : BaseEntity
{
    public Guid PurchaseRequestId { get; set; }
    public PurchaseRequest PurchaseRequest { get; set; } = null!;

    public Guid MaterialId { get; set; }
    public Material Material { get; set; } = null!;

    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "Adet";
    public string? Description { get; set; }
}

public sealed class PurchaseOrder : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid SupplierCurrentAccountId { get; set; }
    public CurrentAccount SupplierCurrentAccount { get; set; } = null!;

    public Guid? PurchaseRequestId { get; set; }
    public PurchaseRequest? PurchaseRequest { get; set; }

    public string OrderNumber { get; set; } = string.Empty;
    public DateTime OrderDateUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DeliveryDateUtc { get; set; }
    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;
    public string CurrencyCode { get; set; } = "TRY";
    public decimal ExchangeRate { get; set; } = 1m;
    public decimal VatRate { get; set; } = 20m;
    public string? Description { get; set; }

    public ICollection<PurchaseOrderItem> Items { get; set; } = new List<PurchaseOrderItem>();
}

public sealed class PurchaseOrderItem : BaseEntity
{
    public Guid PurchaseOrderId { get; set; }
    public PurchaseOrder PurchaseOrder { get; set; } = null!;

    public Guid MaterialId { get; set; }
    public Material Material { get; set; } = null!;

    public decimal Quantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public string Unit { get; set; } = "Adet";
    public decimal UnitPrice { get; set; }
    public decimal DiscountRate { get; set; }
    public string? Description { get; set; }
}

public sealed class GoodsReceipt : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid PurchaseOrderId { get; set; }
    public PurchaseOrder PurchaseOrder { get; set; } = null!;

    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public string ReceiptNumber { get; set; } = string.Empty;
    public DateTime ReceiptDateUtc { get; set; } = DateTime.UtcNow;
    public GoodsReceiptStatus Status { get; set; } = GoodsReceiptStatus.Draft;
    public string? DeliveryNoteNumber { get; set; }
    public string? Description { get; set; }

    public ICollection<GoodsReceiptItem> Items { get; set; } = new List<GoodsReceiptItem>();
}

public sealed class GoodsReceiptItem : BaseEntity
{
    public Guid GoodsReceiptId { get; set; }
    public GoodsReceipt GoodsReceipt { get; set; } = null!;

    public Guid PurchaseOrderItemId { get; set; }
    public PurchaseOrderItem PurchaseOrderItem { get; set; } = null!;

    public Guid MaterialId { get; set; }
    public Material Material { get; set; } = null!;

    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
}
