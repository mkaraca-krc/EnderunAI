namespace EnderunAI.Api.Contracts.Accounting;

/// <summary>Nakit akışındaki tek bir beklenen giriş/çıkış kalemi.</summary>
public sealed record CashFlowItemResponse(
    string Kind,
    string KindName,
    Guid SourceId,
    string Reference,
    string Title,
    Guid? CurrentAccountId,
    string? CurrentAccountTitle,
    Guid? ProjectId,
    string? ProjectCode,
    DateTime ExpectedDate,
    int DaysToDue,
    bool IsOverdue,
    decimal Amount,
    string CurrencyCode);

public sealed record CashFlowBucketResponse(
    int Days,
    string Label,
    decimal InflowAmount,
    decimal OutflowAmount,
    decimal NetAmount,
    decimal ProjectedBalance);

public sealed record CashFlowResponse(
    Guid CompanyId,
    DateTime AsOfDate,
    decimal CurrentCashBalance,
    decimal OverdueInflowAmount,
    decimal OverdueOutflowAmount,
    IReadOnlyCollection<CashFlowBucketResponse> Buckets,
    IReadOnlyCollection<CashFlowItemResponse> Inflows,
    IReadOnlyCollection<CashFlowItemResponse> Outflows);
