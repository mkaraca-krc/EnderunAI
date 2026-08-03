namespace EnderunAI.Api.Contracts.ProgressPayments;

/// <param name="UnitPrice">Toplam birim fiyat. Bileşenler verilmişse
/// onların toplamı esas alınır; verilmemişse bu değer malzeme kabul
/// edilir (eski istemciler bozulmasın).</param>
/// <param name="SectionId">Pozun ait olduğu imalat bölümü.</param>
public sealed record ProgressPaymentItemRequest(
    Guid? EngineeringPositionId,
    string PositionCode,
    string Description,
    string Unit,
    decimal ContractQuantity,
    decimal CurrentQuantity,
    decimal UnitPrice,
    string? MeasurementReference,
    string? Notes,
    decimal? MaterialUnitPrice = null,
    decimal? LaborUnitPrice = null,
    decimal? OverheadUnitPrice = null,
    Guid? SectionId = null
);

public sealed record ProgressPaymentDeductionRequest(
    int DeductionType,
    string Description,
    decimal Rate,
    decimal BaseAmount,
    decimal? ManualAmount,
    string? Notes,
    /// <summary>
    /// Kesintinin borç yazılacağı hesap. Boşsa şirket finans ayarındaki
    /// varsayılan kesinti hesabı kullanılır.
    /// </summary>
    Guid? AccountingAccountId = null
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
