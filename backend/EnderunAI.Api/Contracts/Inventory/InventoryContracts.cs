namespace EnderunAI.Api.Contracts.Inventory;

public sealed record CreateInventoryItemRequest(
    Guid CompanyId,
    string Code,
    string Name,
    string? Category,
    string? Brand,
    string? Model,
    string Unit,
    string? Barcode,
    decimal MinimumStock,
    decimal? MaximumStock,
    int Type);

public sealed record StockReceiptRequest(
    Guid WarehouseId,
    Guid InventoryItemId,
    Guid? ProjectId,
    Guid? PurchaseRequestId,
    decimal Quantity,
    string ReferenceNumber,
    DateTime MovementDate,
    string? Description);

public sealed record StockIssueRequest(
    Guid WarehouseId,
    Guid InventoryItemId,
    Guid? ProjectId,
    Guid? ProjectSiteId,
    decimal Quantity,
    string? ReferenceNumber,
    DateTime MovementDate,
    string? Description);

public sealed record StockTransferRequest(
    Guid SourceWarehouseId,
    Guid TargetWarehouseId,
    Guid InventoryItemId,
    Guid? ProjectId,
    decimal Quantity,
    string? ReferenceNumber,
    DateTime MovementDate,
    string? Description);

public sealed record StockAdjustmentRequest(
    Guid WarehouseId,
    Guid InventoryItemId,
    decimal CountedQuantity,
    Guid? ProjectId,
    DateTime MovementDate,
    string? Description);

public sealed record UpdateInventoryItemRequest(
    string Name,
    string? Category,
    string? Brand,
    string? Model,
    string Unit,
    string? Barcode,
    decimal MinimumStock,
    decimal? MaximumStock,
    int Type,
    bool IsActive);
