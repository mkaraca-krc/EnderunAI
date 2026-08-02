namespace EnderunAI.Api.Models;

public enum StockReservationStatus
{
    Active = 0,
    PartiallyConsumed = 1,
    Consumed = 2,
    Cancelled = 3,
    Expired = 4
}

public sealed class StockReservation : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public Guid InventoryItemId { get; set; }
    public InventoryItem InventoryItem { get; set; } = null!;

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid PurchaseRequestId { get; set; }
    public PurchaseRequest PurchaseRequest { get; set; } = null!;

    public Guid PurchaseRequestItemId { get; set; }
    public PurchaseRequestItem PurchaseRequestItem { get; set; } = null!;

    public string ReservationNumber { get; set; } = string.Empty;

    public decimal ReservedQuantity { get; set; }
    public decimal ConsumedQuantity { get; set; }

    public DateTime ReservationDate { get; set; } = DateTime.UtcNow;
    public DateTime? ExpirationDate { get; set; }

    public StockReservationStatus Status { get; set; } = StockReservationStatus.Active;

    public string? Description { get; set; }
}
