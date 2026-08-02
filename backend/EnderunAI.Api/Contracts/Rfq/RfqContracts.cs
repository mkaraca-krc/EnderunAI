namespace EnderunAI.Api.Contracts.Rfq;

public sealed record RfqListItemResponse(
    Guid Id,
    Guid CompanyId,
    Guid PurchaseRequestId,
    string PurchaseRequestNumber,
    string RfqNumber,
    string Title,
    DateTime IssueDate,
    DateTime? ResponseDeadline,
    int Status,
    string Currency,
    int ItemCount,
    int SupplierCount,
    int ResponseCount);

public sealed record RfqItemResponse(
    Guid Id,
    int LineNumber,
    string MaterialDescription,
    decimal Quantity,
    string Unit,
    DateTime? RequestedDeliveryDate,
    string? Notes);

public sealed record RfqSupplierResponse(
    Guid Id,
    Guid SupplierCurrentAccountId,
    string SupplierCode,
    string SupplierTitle,
    int Status,
    DateTime? SentAtUtc,
    DateTime? RespondedAtUtc,
    string? ContactName,
    string? ContactEmail,
    Guid? QuotationId,
    decimal? QuotationTotal,
    int? DeliveryDays,
    string? PaymentTerm);

public sealed record RfqDetailResponse(
    Guid Id,
    Guid CompanyId,
    Guid PurchaseRequestId,
    string PurchaseRequestNumber,
    string RfqNumber,
    string Title,
    DateTime IssueDate,
    DateTime? ResponseDeadline,
    int Status,
    string Currency,
    string? Description,
    string? Notes,
    IReadOnlyList<RfqItemResponse> Items,
    IReadOnlyList<RfqSupplierResponse> Suppliers);

public sealed record CreateRfqRequest(
    string Title,
    DateTime? ResponseDeadline,
    string Currency,
    string? Description,
    string? Notes,
    IReadOnlyList<Guid> SupplierCurrentAccountIds);

public sealed record CreateRfqResponse(
    Guid Id,
    string RfqNumber,
    int ItemCount,
    int SupplierCount);

public sealed record SaveQuotationItemRequest(
    Guid RfqItemId,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountRate,
    string? Brand,
    string? Model,
    int? DeliveryDays,
    string? Notes);

public sealed record SaveQuotationRequest(
    string? SupplierQuotationNumber,
    DateTime QuotationDate,
    DateTime? ValidUntil,
    string Currency,
    decimal ExchangeRate,
    int? DeliveryDays,
    string? PaymentTerm,
    string? Notes,
    IReadOnlyList<SaveQuotationItemRequest> Items);

public sealed record RfqComparisonItemResponse(
    Guid RfqItemId,
    string MaterialDescription,
    decimal RequestedQuantity,
    string Unit,
    decimal UnitPrice,
    decimal NetUnitPrice,
    decimal TotalPrice,
    string? Brand,
    string? Model,
    int? DeliveryDays,
    decimal NormalizedTotalPrice);

public sealed record RfqComparisonSupplierResponse(
    Guid RfqSupplierId,
    Guid SupplierCurrentAccountId,
    string SupplierTitle,
    bool HasQuotation,
    string Currency,
    decimal GrandTotal,
    decimal ExchangeRate,
    decimal NormalizedGrandTotal,
    int? DeliveryDays,
    string? PaymentTerm,
    decimal PriceScore,
    decimal DeliveryTermScore,
    decimal HistoricalPerformanceScore,
    decimal DecisionScore,
    int Rank,
    bool IsRecommended,
    decimal ResponseRate,
    decimal OnTimeDeliveryRate,
    decimal QualityRate,
    string Confidence,
    IReadOnlyList<RfqComparisonItemResponse> Items);

public sealed record RfqComparisonResponse(
    Guid RfqId,
    string RfqNumber,
    decimal LowestTotal,
    Guid? LowestSupplierId,
    string? LowestSupplierTitle,
    string ComparisonCurrency,
    decimal LowestNormalizedTotal,
    decimal AverageNormalizedTotal,
    decimal SavingVsSecondLowest,
    decimal SavingRate,
    Guid? RecommendedSupplierId,
    string? RecommendedSupplierTitle,
    IReadOnlyList<RfqComparisonSupplierResponse> Suppliers);

public sealed record AwardRfqResponse(
    Guid RfqId,
    Guid RfqSupplierId,
    Guid SupplierCurrentAccountId,
    string SupplierTitle,
    decimal GrandTotal);
