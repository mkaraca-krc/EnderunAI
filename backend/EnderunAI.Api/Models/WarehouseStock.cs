namespace EnderunAI.Api.Models;

public sealed class WarehouseStock : BaseEntity
{
    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public Guid InventoryItemId { get; set; }
    public InventoryItem InventoryItem { get; set; } = null!;

    public decimal Quantity { get; set; }
    public decimal ReservedQuantity { get; set; }
}
