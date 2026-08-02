namespace EnderunAI.Api.Contracts.ProgressPayments;

public sealed record ProgressPaymentItemRequest(
    Guid? EngineeringPositionId,
    string PositionCode,
    string Description,
    string Unit,
    decimal ContractQuantity,
    decimal CurrentQuantity,
    decimal UnitPrice,
    string? MeasurementReference,
    string? Notes
);

public sealed record ProgressPaymentDeductionRequest(
    int DeductionType,
    string Description,
    decimal Rate,
    decimal BaseAmount,
    decimal? ManualAmount,
    string? Notes
);

public sealed record CreateProgressPaymentRequest(
    Guid CompanyId,
    Guid ProjectId,
    Guid? ProjectMeasurementId,
    string ProgressPaymentNumber,
    int PeriodNumber,
    DateOnly? PeriodStartDate,
    DateOnly? PeriodEndDate,
    DateOnly ProgressPaymentDate,
    decimal PriceDifferenceAmount,
    decimal VatRate,
    int WithholdingNumerator,
    int WithholdingDenominator,
    string? Description,
    string? Notes,
    IReadOnlyCollection<ProgressPaymentItemRequest> Items,
    IReadOnlyCollection<ProgressPaymentDeductionRequest> Deductions
);

public sealed record UpdateProgressPaymentRequest(
    DateOnly? PeriodStartDate,
    DateOnly? PeriodEndDate,
    DateOnly ProgressPaymentDate,
    decimal PriceDifferenceAmount,
    decimal VatRate,
    int WithholdingNumerator,
    int WithholdingDenominator,
    string? Description,
    string? Notes,
    IReadOnlyCollection<ProgressPaymentItemRequest> Items,
    IReadOnlyCollection<ProgressPaymentDeductionRequest> Deductions
);

public sealed record CancelProgressPaymentRequest(string? Reason);
