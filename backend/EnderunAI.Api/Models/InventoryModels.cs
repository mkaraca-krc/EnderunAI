namespace EnderunAI.Api.Models;

public enum StockMovementType
{
    Opening = 0,
    Receipt = 1,
    Issue = 2,
    TransferIn = 3,
    TransferOut = 4,
    AdjustmentIn = 5,
    AdjustmentOut = 6,
    ReturnIn = 7,
    ReturnOut = 8
}

public sealed class Material : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = "Adet";
    public string? Category { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? Barcode { get; set; }
    public decimal MinimumStock { get; set; }
}

public sealed class WarehouseStock : BaseEntity
{
    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public Guid MaterialId { get; set; }
    public Material Material { get; set; } = null!;

    public decimal Quantity { get; set; }
    public decimal ReservedQuantity { get; set; }
    public decimal AverageUnitCost { get; set; }
}

public sealed class StockMovement : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public Guid MaterialId { get; set; }
    public Material Material { get; set; } = null!;

    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public StockMovementType MovementType { get; set; }
    public DateTime MovementDateUtc { get; set; } = DateTime.UtcNow;
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? DocumentNumber { get; set; }
    public string? Description { get; set; }
}
