namespace EnderunAI.Api.Contracts.GoodsReceipts;

public sealed record GoodsReceiptListItemResponse(
    Guid Id,
    Guid CompanyId,
    Guid PurchaseOrderId,
    string PurchaseOrderNumber,
    Guid WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    Guid SupplierCurrentAccountId,
    string SupplierTitle,
    string ReceiptNumber,
    DateTime ReceiptDate,
    int Status,
    string? DispatchNoteNumber,
    string ReceivedByName,
    int ItemCount,
    decimal DeliveredQuantity,
    decimal AcceptedQuantity,
    decimal RejectedQuantity,
    decimal DamagedQuantity);

public sealed record GoodsReceiptItemResponse(
    Guid Id,
    Guid PurchaseOrderItemId,
    Guid? InventoryItemId,
    string? InventoryItemCode,
    string? InventoryItemName,
    int LineNumber,
    string MaterialDescription,
    string? Brand,
    string? Model,
    decimal OrderedQuantity,
    decimal PreviouslyReceivedQuantity,
    decimal DeliveredQuantity,
    decimal AcceptedQuantity,
    decimal RejectedQuantity,
    decimal DamagedQuantity,
    string Unit,
    string? LotNumber,
    string? SerialNumber,
    DateTime? ProductionDate,
    DateTime? ExpiryDate,
    DateTime? WarrantyEndDate,
    string? ShelfLocation,
    string? Notes,
    /// <summary>Red / hasar gerekçesi.</summary>
    string? RejectionReason = null);

public sealed record GoodsReceiptDetailResponse(
    Guid Id,
    Guid CompanyId,
    Guid PurchaseOrderId,
    string PurchaseOrderNumber,
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    Guid WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    Guid SupplierCurrentAccountId,
    string SupplierCode,
    string SupplierTitle,
    string ReceiptNumber,
    DateTime ReceiptDate,
    int Status,
    string? DispatchNoteNumber,
    DateTime? DispatchNoteDate,
    string? InvoiceNumber,
    DateTime? InvoiceDate,
    string ReceivedByName,
    string? VehiclePlate,
    string? DriverName,
    string? Description,
    string? Notes,
    DateTime? PostedAtUtc,
    DateTime? CancelledAtUtc,
    string? CancellationReason,
    Guid? AccountingVoucherId,
    string? AccountingVoucherNumber,
    int? AccountingVoucherStatus,
    decimal? AccountingVoucherTotal,
    IReadOnlyList<GoodsReceiptItemResponse> Items);

public sealed record CreateGoodsReceiptRequest(
    Guid WarehouseId,
    DateTime ReceiptDate,
    string ReceivedByName,
    string? DispatchNoteNumber,
    DateTime? DispatchNoteDate,
    string? InvoiceNumber,
    DateTime? InvoiceDate,
    string? VehiclePlate,
    string? DriverName,
    string? Description,
    string? Notes);

public sealed record CreateGoodsReceiptResponse(
    Guid Id,
    string ReceiptNumber,
    Guid PurchaseOrderId,
    string PurchaseOrderNumber,
    Guid WarehouseId,
    string WarehouseName,
    int ItemCount,
    int Status);

public sealed record GoodsReceiptInventoryOptionResponse(
    Guid Id,
    string Code,
    string Name,
    string? Category,
    string? Brand,
    string? Model,
    string Unit);

public sealed record UpdateGoodsReceiptDraftRequest(
    IReadOnlyList<UpdateGoodsReceiptItemRequest> Items);

public sealed record UpdateGoodsReceiptItemRequest(
    Guid Id,
    Guid? InventoryItemId,
    decimal DeliveredQuantity,
    decimal AcceptedQuantity,
    decimal RejectedQuantity,
    decimal DamagedQuantity,
    string? LotNumber,
    string? SerialNumber,
    DateTime? ProductionDate,
    DateTime? ExpiryDate,
    DateTime? WarrantyEndDate,
    string? ShelfLocation,
    string? Notes,
    /// <summary>
    /// Red / hasar gerekçesi. Reddedilen ya da hasarlı miktar varsa
    /// kesinleştirmede zorunlu. Opsiyonel ve sonda: mevcut çağıranlar
    /// bozulmasın.
    /// </summary>
    string? RejectionReason = null);

public sealed record GoodsReceiptReasonRequest(string Reason);

public sealed record GoodsReceiptActionResponse(
    Guid Id,
    string ReceiptNumber,
    int Status,
    int StockMovementCount,
    string Message);

