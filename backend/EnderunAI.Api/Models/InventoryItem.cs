namespace EnderunAI.Api.Models;

public enum InventoryItemType
{
    Material = 0,
    Equipment = 1,
    Consumable = 2,
    SparePart = 3
}

public sealed class InventoryItem : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public decimal MinimumStock { get; set; }
    public decimal? MaximumStock { get; set; }
    public InventoryItemType Type { get; set; } = InventoryItemType.Material;

    /// <summary>
    /// Ağırlıklı ortalama birim maliyet, her zaman TRY. Döviz cinsi mal
    /// kabullerinde PurchaseOrder.ExchangeRate ile TRY'ye çevrilerek
    /// ortalamaya katılır (bkz. GoodsReceiptService.PostAsync).
    /// </summary>
    public decimal AverageUnitCost { get; set; }

    public ICollection<WarehouseStock> WarehouseStocks { get; set; } = new List<WarehouseStock>();
    public ICollection<StockMovement> StockMovements { get; set; } = new List<StockMovement>();
}
