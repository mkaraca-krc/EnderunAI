namespace EnderunAI.Api.Contracts.Accounting;

public sealed record SalesInvoiceItemRequest(
    string Description,
    decimal Quantity,
    string Unit,
    decimal UnitPrice,
    decimal VatRate);

public sealed record CreateSalesInvoiceRequest(
    Guid CompanyId,
    Guid CustomerCurrentAccountId,
    Guid? ProjectId,
    /// <summary>GİB/entegratör numarası; henüz kesilmediyse boş kalır.</summary>
    string? OfficialInvoiceNumber,
    DateTime InvoiceDate,
    DateTime? DueDate,
    string CurrencyCode,
    decimal ExchangeRate,
    /// <summary>KDV tevkifatı tutarı; yoksa sıfır.</summary>
    decimal WithholdingAmount,
    string? Description,
    string? Notes,
    IReadOnlyCollection<SalesInvoiceItemRequest> Items);

public sealed record UpdateSalesInvoiceRequest(
    Guid CustomerCurrentAccountId,
    Guid? ProjectId,
    string? OfficialInvoiceNumber,
    DateTime InvoiceDate,
    DateTime? DueDate,
    string CurrencyCode,
    decimal ExchangeRate,
    decimal WithholdingAmount,
    string? Description,
    string? Notes,
    IReadOnlyCollection<SalesInvoiceItemRequest> Items);

public sealed record CancelSalesInvoiceRequest(string Reason);

public sealed record SalesInvoiceItemResponse(
    Guid Id,
    int LineNumber,
    string Description,
    decimal Quantity,
    string Unit,
    decimal UnitPrice,
    decimal VatRate,
    decimal LineSubtotal,
    decimal VatAmount,
    decimal LineTotal);

public sealed record SalesInvoiceListItemResponse(
    Guid Id,
    string InternalNumber,
    string? OfficialInvoiceNumber,
    DateTime InvoiceDate,
    Guid CustomerCurrentAccountId,
    string CustomerTitle,
    Guid? ProjectId,
    string? ProjectCode,
    string? ProjectName,
    string CurrencyCode,
    decimal Subtotal,
    decimal VatTotal,
    decimal WithholdingAmount,
    decimal GrandTotal,
    decimal NetReceivableAmount,
    int Status,
    bool RequiresManualReview,
    int? ParseSource,
    string? AccountingVoucherNumber,
    bool IsReturn);

public sealed record SalesInvoiceDetailResponse(
    Guid Id,
    Guid CompanyId,
    string InternalNumber,
    string? OfficialInvoiceNumber,
    DateTime InvoiceDate,
    DateTime? DueDate,
    Guid CustomerCurrentAccountId,
    string CustomerTitle,
    Guid? ProjectId,
    string? ProjectCode,
    string? ProjectName,
    string CurrencyCode,
    decimal ExchangeRate,
    decimal Subtotal,
    decimal VatTotal,
    decimal WithholdingAmount,
    decimal GrandTotal,
    decimal NetReceivableAmount,
    string? Description,
    string? Notes,
    int Status,
    DateTime? PostedAtUtc,
    DateTime? CancelledAtUtc,
    string? CancellationReason,
    bool RequiresManualReview,
    int? ParseSource,
    bool HasSourceXml,
    Guid? AccountingVoucherId,
    string? AccountingVoucherNumber,
    IReadOnlyCollection<SalesInvoiceItemResponse> Items,
    /// <summary>Bu belge bir iade faturası mı.</summary>
    bool IsReturn,
    /// <summary>İade faturasında iade edilen orijinal fatura.</summary>
    Guid? OriginalInvoiceId,
    string? OriginalInvoiceNumber,
    /// <summary>İptalde üretilen ters fiş.</summary>
    Guid? ReversalVoucherId,
    string? ReversalVoucherNumber);

public sealed record SalesInvoiceActionResponse(
    Guid Id,
    string InternalNumber,
    int Status,
    string Message);
