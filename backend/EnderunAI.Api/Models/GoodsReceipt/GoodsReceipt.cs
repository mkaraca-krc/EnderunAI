namespace EnderunAI.Api.Models.GoodsReceipt;

public enum GoodsReceiptStatus
{
    Draft = 0,
    Posted = 1,
    Cancelled = 2
}

public sealed class GoodsReceipt : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid PurchaseOrderId { get; set; }
    public global::EnderunAI.Api.Models.PurchaseOrder.PurchaseOrder PurchaseOrder { get; set; } = null!;

    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public string ReceiptNumber { get; set; } = string.Empty;
    public DateTime ReceiptDate { get; set; }
    public GoodsReceiptStatus Status { get; set; } = GoodsReceiptStatus.Draft;
    public string? DispatchNoteNumber { get; set; }
    public DateTime? DispatchNoteDate { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateTime? InvoiceDate { get; set; }
    public string ReceivedByName { get; set; } = string.Empty;
    public Guid? ReceivedByUserId { get; set; }
    public string? VehiclePlate { get; set; }
    public string? DriverName { get; set; }
    public string? Description { get; set; }
    public string? Notes { get; set; }
    public DateTime? PostedAtUtc { get; set; }
    public Guid? PostedByUserId { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string? CancellationReason { get; set; }

    public ICollection<GoodsReceiptItem> Items { get; set; } =
        new List<GoodsReceiptItem>();
}
