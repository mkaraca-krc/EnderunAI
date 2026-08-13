namespace EnderunAI.Api.Contracts.PurchaseOrders;

public sealed record PurchaseOrderListItemResponse(
    Guid Id,
    Guid CompanyId,
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    Guid RfqId,
    string RfqNumber,
    Guid SupplierCurrentAccountId,
    string SupplierCode,
    string SupplierTitle,
    string OrderNumber,
    DateTime OrderDate,
    DateTime? ExpectedDeliveryDate,
    int Status,
    string Currency,
    decimal GrandTotal,
    int ItemCount);

public sealed record PurchaseOrderItemResponse(
    Guid Id,
    Guid? RfqItemId,
    Guid? RfqSupplierQuotationItemId,
    int LineNumber,
    string MaterialDescription,
    /// <summary>Tedarikçinin VERDİĞİ marka (kabul edilen tekliften).</summary>
    string? Brand,
    string? Model,
    decimal Quantity,
    decimal ReceivedQuantity,
    string Unit,
    decimal UnitPrice,
    decimal DiscountRate,
    decimal NetUnitPrice,
    decimal TotalPrice,
    int? DeliveryDays,
    DateTime? ExpectedDeliveryDate,
    string? Notes,
    /// <summary>
    /// Talep edenin İSTEDİĞİ marka — talepten RFQ üzerinden taşındı.
    /// <see cref="Brand"/> ile YAN YANA durur: "Schneider istendi,
    /// ABB alındı" farkı sipariş kaydında görünsün diye.
    /// Talepsiz doğrudan açılan siparişte boştur.
    /// </summary>
    string? RequestedBrand = null,
    /// <summary>Muadil kabul edildiyse marka farkı beklenen bir sonuçtur.</summary>
    bool BrandIrrelevant = true);

public sealed record PurchaseOrderDetailResponse(
    Guid Id,
    Guid CompanyId,
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    Guid RfqId,
    string RfqNumber,
    Guid PurchaseRequestId,
    string PurchaseRequestNumber,
    Guid SupplierCurrentAccountId,
    string SupplierCode,
    string SupplierTitle,
    string? SupplierAuthorizedPerson,
    string? SupplierPhone,
    string? SupplierEmail,
    string? SupplierAddress,
    string OrderNumber,
    DateTime OrderDate,
    DateTime? ExpectedDeliveryDate,
    int Status,
    string Currency,
    decimal ExchangeRate,
    string? PaymentTerm,
    string? DeliveryAddress,
    string? Description,
    string? Notes,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal GrandTotal,
    DateTime? ApprovedAtUtc,
    DateTime? CancelledAtUtc,
    string? CancellationReason,
    IReadOnlyList<PurchaseOrderItemResponse> Items);

public sealed record PurchaseOrderActionResponse(
    Guid Id,
    string OrderNumber,
    int Status,
    string Message);

public sealed record CreatePurchaseOrderFromRfqResponse(
    Guid Id,
    string OrderNumber,
    Guid RfqId,
    Guid SupplierCurrentAccountId,
    string SupplierTitle,
    decimal GrandTotal,
    string Currency);

public sealed record PurchaseOrderReasonRequest(string Reason);
