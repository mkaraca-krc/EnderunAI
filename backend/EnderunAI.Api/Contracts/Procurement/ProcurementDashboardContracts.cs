namespace EnderunAI.Api.Contracts.Procurement;

public sealed record ProcurementDashboardResponse(
    Guid? CompanyId,
    Guid? ProjectId,
    DateTime GeneratedAtUtc,
    PurchaseRequestDashboardSummary PurchaseRequests,
    RfqDashboardSummary Rfqs,
    PurchaseOrderDashboardSummary PurchaseOrders,
    GoodsReceiptDashboardSummary GoodsReceipts,
    IReadOnlyList<PurchaseOrderCurrencySummary> OrderValues,
    IReadOnlyList<GoodsReceiptUnitSummary> ReceiptQuantities,
    IReadOnlyList<RecentPurchaseOrderDashboardItem> RecentPurchaseOrders,
    IReadOnlyList<RecentGoodsReceiptDashboardItem> RecentGoodsReceipts,
    IReadOnlyList<ProcurementDashboardAlert> Alerts);

public sealed record PurchaseRequestDashboardSummary(
    int Total,
    int Draft,
    int Submitted,
    int Approved,
    int Quotation,
    int Ordered,
    int Completed,
    int Cancelled,
    int Rejected,
    int Open,
    int CriticalOpen);

public sealed record RfqDashboardSummary(
    int Total,
    int Draft,
    int Sent,
    int ResponsesReceived,
    int Awarded,
    int Closed,
    int Cancelled,
    int ResponseOverdue);

public sealed record PurchaseOrderDashboardSummary(
    int Total,
    int Draft,
    int PendingApproval,
    int Approved,
    int PartiallyReceived,
    int Completed,
    int Cancelled,
    int Rejected,
    int Open,
    int OverdueDelivery);

public sealed record GoodsReceiptDashboardSummary(
    int Total,
    int Draft,
    int Posted,
    int Cancelled,
    int ExceptionLineCount);

public sealed record PurchaseOrderCurrencySummary(
    string Currency,
    decimal TotalAmount,
    decimal ActiveAmount,
    decimal CompletedAmount);

public sealed record GoodsReceiptUnitSummary(
    string Unit,
    decimal AcceptedQuantity,
    decimal RejectedQuantity,
    decimal DamagedQuantity,
    int ExceptionLineCount);

public sealed record RecentPurchaseOrderDashboardItem(
    Guid Id,
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    string OrderNumber,
    DateTime OrderDate,
    DateTime? ExpectedDeliveryDate,
    int Status,
    string SupplierTitle,
    string Currency,
    decimal GrandTotal,
    int ItemCount);

public sealed record RecentGoodsReceiptDashboardItem(
    Guid Id,
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    string ReceiptNumber,
    DateTime ReceiptDate,
    int Status,
    string PurchaseOrderNumber,
    string SupplierTitle,
    string WarehouseName,
    int ItemCount,
    int ExceptionLineCount);

public sealed record ProcurementDashboardAlert(
    string Severity,
    string Code,
    string Title,
    string Message,
    int Count,
    string Href);
