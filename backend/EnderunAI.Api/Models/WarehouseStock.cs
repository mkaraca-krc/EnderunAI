namespace EnderunAI.Api.Models;

public sealed class WarehouseStock : BaseEntity
{
    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public Guid InventoryItemId { get; set; }
    public InventoryItem InventoryItem { get; set; } = null!;

    /// <summary>
    /// Depodaki fiili miktar. Ayrı bir "rezerve" kovası YOK: Enderun
    /// stok bloke etmiyor, ihtiyaca göre tedarik ediyor. Rezervasyon
    /// alanı yıllarca hep sıfır kaldı ve "kullanılabilir stok" her
    /// zaman bu miktara eşitti.
    /// </summary>
    public decimal Quantity { get; set; }
}
