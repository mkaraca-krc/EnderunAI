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
    int Type,
    Guid? PreferredSupplierCurrentAccountId = null,
    decimal? VatRate = null,
    string? Description = null);

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
    string? Description,
    /// <summary>
    /// Sarfın gittiği icmal kısmı. OPSİYONEL — bilinmiyorsa boş
    /// bırakılır, maliyet proje geneline yazılır.
    /// </summary>
    Guid? ProjectHakedisSectionId = null);

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
    bool IsActive,
    Guid? PreferredSupplierCurrentAccountId = null,
    decimal? VatRate = null,
    string? Description = null);

/// <summary>Malzeme kartı detayı — düzenleme ekranı bunu okur.</summary>
public sealed record InventoryItemDetail(
    Guid Id,
    Guid CompanyId,
    string CompanyName,
    string Code,
    string Name,
    string? Category,
    string? Brand,
    string? Model,
    string Unit,
    string? Barcode,
    decimal MinimumStock,
    decimal? MaximumStock,
    int Type,
    bool IsActive,
    decimal AverageUnitCost,
    decimal? LastPurchasePrice,
    DateTime? LastPurchaseDate,
    Guid? PreferredSupplierCurrentAccountId,
    string? PreferredSupplierTitle,
    decimal? VatRate,
    string? Description,
    string? ImagePath,
    decimal TotalStock,
    decimal AvailableStock,
    /// <summary>Toplam stok × ağırlıklı ortalama maliyet.</summary>
    decimal StockValue,
    IReadOnlyList<InventoryItemWarehouseStock> Warehouses);

public sealed record CreateWarehouseRequest(
    Guid CompanyId,
    Guid BranchId,
    Guid? ProjectId,
    Guid? ProjectSiteId,
    string Code,
    string Name,
    int Type,
    string? Address);

public sealed record UpdateWarehouseRequest(
    Guid BranchId,
    Guid? ProjectId,
    Guid? ProjectSiteId,
    string Name,
    int Type,
    string? Address,
    bool IsActive);

public sealed record InventoryItemWarehouseStock(
    Guid WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    decimal Quantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity);
