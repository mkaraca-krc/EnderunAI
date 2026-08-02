namespace EnderunAI.Api.Models;

public enum StockMovementType
{
    Receipt = 0,
    Issue = 1,
    TransferIn = 2,
    TransferOut = 3,
    Return = 4,
    Adjustment = 5,
    Count = 6
}

public sealed class StockMovement : BaseEntity
{
    public Guid CompanyId { get; set; }

    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public Guid InventoryItemId { get; set; }
    public InventoryItem InventoryItem { get; set; } = null!;

    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public Guid? RelatedWarehouseId { get; set; }
    public Warehouse? RelatedWarehouse { get; set; }

    public Guid? PurchaseRequestId { get; set; }
    public PurchaseRequest? PurchaseRequest { get; set; }

    public StockMovementType Type { get; set; }
    public decimal Quantity { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public DateTime MovementDate { get; set; } = DateTime.UtcNow;
    public string? Description { get; set; }
}
