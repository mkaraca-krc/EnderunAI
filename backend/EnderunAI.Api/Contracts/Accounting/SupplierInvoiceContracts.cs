namespace EnderunAI.Api.Contracts.Accounting;

public sealed record SupplierInvoiceItemRequest(
    string Description,
    decimal Quantity,
    string Unit,
    decimal UnitPrice,
    decimal VatRate,
    Guid? PurchaseOrderItemId);

public sealed record CreateSupplierInvoiceRequest(
    Guid CompanyId,
    Guid SupplierCurrentAccountId,
    Guid ProjectId,
    Guid? PurchaseOrderId,
    Guid? GoodsReceiptId,
    string InvoiceNumber,
    DateTime InvoiceDate,
    DateTime? DueDate,
    string CurrencyCode,
    decimal ExchangeRate,
    string? Description,
    IReadOnlyCollection<SupplierInvoiceItemRequest> Items);

public sealed record UpdateSupplierInvoiceRequest(
    string InvoiceNumber,
    DateTime InvoiceDate,
    DateTime? DueDate,
    string CurrencyCode,
    decimal ExchangeRate,
    string? Description,
    IReadOnlyCollection<SupplierInvoiceItemRequest> Items);

public sealed record RejectSupplierInvoiceRequest(string Reason);

public sealed record SupplierInvoiceItemResponse(
    Guid Id,
    int LineNumber,
    string Description,
    decimal Quantity,
    string Unit,
    decimal UnitPrice,
    decimal VatRate,
    decimal LineSubtotal,
    decimal VatAmount,
    decimal LineTotal,
    Guid? PurchaseOrderItemId);

public sealed record SupplierInvoiceListItemResponse(
    Guid Id,
    string InternalNumber,
    string InvoiceNumber,
    DateTime InvoiceDate,
    Guid SupplierCurrentAccountId,
    string SupplierTitle,
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    string CurrencyCode,
    decimal Subtotal,
    decimal VatTotal,
    decimal GrandTotal,
    int Status,
    int MatchStatus,
    bool RequiresGmApproval,
    string? PurchaseOrderNumber,
    string? AccountingVoucherNumber);

public sealed record SupplierInvoiceDetailResponse(
    Guid Id,
    Guid CompanyId,
    string InternalNumber,
    string InvoiceNumber,
    DateTime InvoiceDate,
    DateTime? DueDate,
    Guid SupplierCurrentAccountId,
    string SupplierTitle,
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    Guid? PurchaseOrderId,
    string? PurchaseOrderNumber,
    Guid? GoodsReceiptId,
    string? GoodsReceiptNumber,
    string CurrencyCode,
    decimal ExchangeRate,
    decimal Subtotal,
    decimal VatTotal,
    decimal GrandTotal,
    string? Description,
    int Status,
    int MatchStatus,
    decimal MatchDifferenceAmount,
    string? MatchNote,
    bool RequiresGmApproval,
    DateTime? SubmittedAtUtc,
    DateTime? ApprovedAtUtc,
    DateTime? RejectedAtUtc,
    string? RejectionReason,
    Guid? AccountingVoucherId,
    string? AccountingVoucherNumber,
    IReadOnlyCollection<SupplierInvoiceItemResponse> Items);

public sealed record SupplierInvoiceActionResponse(
    Guid Id,
    string InternalNumber,
    int Status,
    string Message);

public sealed record CompanyFinanceSettingsResponse(
    Guid CompanyId,
    decimal GmApprovalThresholdTry,
    decimal ThreeWayTolerancePercent,
    decimal DefaultVatRate,
    Guid? VatInAccountId,
    Guid? VatOutAccountId,
    Guid? SalesAccountId,
    Guid? ExpenseAccountId,
    Guid? PayablesAccountId,
    Guid? ReceivablesAccountId,
    Guid? FactoringExpenseAccountId,
    Guid? DeductionAccountId);

public sealed record UpdateCompanyFinanceSettingsRequest(
    decimal GmApprovalThresholdTry,
    decimal ThreeWayTolerancePercent,
    decimal DefaultVatRate,
    Guid? VatInAccountId,
    Guid? VatOutAccountId,
    Guid? SalesAccountId,
    Guid? ExpenseAccountId,
    Guid? PayablesAccountId,
    Guid? ReceivablesAccountId,
    Guid? FactoringExpenseAccountId,
    Guid? DeductionAccountId);
