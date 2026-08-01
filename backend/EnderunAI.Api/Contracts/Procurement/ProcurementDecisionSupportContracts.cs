namespace EnderunAI.Api.Contracts.Procurement;

public sealed record ProcurementDecisionSupportResponse(
    Guid? CompanyId,
    Guid? ProjectId,
    int PeriodDays,
    DateTime PeriodStartUtc,
    DateTime GeneratedAtUtc,
    ProcurementDecisionSupportSummary Summary,
    IReadOnlyList<SupplierPerformanceResponse> Suppliers,
    IReadOnlyList<RfqDecisionSupportResponse> RecentRfqComparisons,
    IReadOnlyList<ProcurementDecisionAlert> Alerts);

public sealed record ProcurementDecisionSupportSummary(
    int SupplierCount,
    int ComparedRfqCount,
    decimal AverageSupplierScore,
    decimal ResponseRate,
    decimal OnTimeDeliveryRate,
    decimal QualityRate,
    decimal ComparedOfferSpreadTotalTry);

public sealed record SupplierSpendCurrencyResponse(
    string Currency,
    decimal OrderTotal);

public sealed record SupplierPerformanceResponse(
    Guid SupplierCurrentAccountId,
    Guid CompanyId,
    string SupplierCode,
    string SupplierTitle,
    int InvitationCount,
    int ResponseCount,
    decimal ResponseRate,
    int AwardCount,
    int TotalOrderCount,
    int CompletedOrderCount,
    int ActiveOrderCount,
    int OverdueOpenOrderCount,
    int DeliveryMeasuredOrderCount,
    int OnTimeDeliveryOrderCount,
    decimal OnTimeDeliveryRate,
    int ReceiptLineCount,
    int ExceptionLineCount,
    decimal QualityRate,
    int PriceBenchmarkCount,
    decimal PriceScore,
    decimal PerformanceScore,
    string Confidence,
    DateTime? LastOrderDate,
    IReadOnlyList<SupplierSpendCurrencyResponse> SpendByCurrency);

public sealed record RfqDecisionSupportResponse(
    Guid RfqId,
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    string RfqNumber,
    DateTime IssueDate,
    int Status,
    int QuotationCount,
    string ComparisonCurrency,
    decimal LowestNormalizedTotal,
    decimal HighestNormalizedTotal,
    decimal AverageNormalizedTotal,
    decimal OfferSpread,
    Guid RecommendedSupplierCurrentAccountId,
    string RecommendedSupplierTitle,
    decimal RecommendedNormalizedTotal,
    decimal RecommendedScore,
    Guid? AwardedSupplierCurrentAccountId,
    string? AwardedSupplierTitle,
    decimal? AwardedNormalizedTotal);

public sealed record ProcurementDecisionAlert(
    string Severity,
    string Code,
    string Title,
    string Message,
    int Count,
    string Href);
