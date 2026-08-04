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

/// <summary>
/// Ödeme dağılım parçası. NATURA'da üç parça: nakit, 90 gün çek,
/// 120 gün çek. Oranlar toplamı %100 olmalıdır.
/// </summary>
public sealed record ProgressPaymentPaymentPlanRequest(
    int PaymentType,
    decimal Rate,
    int? MaturityDays,
    string? Description
);

/// <summary>Bu hakedişte açılan ihzarat kalemi.</summary>
public sealed record ProgressPaymentAdvanceMaterialRequest(
    string PositionCode,
    string Description,
    string Unit,
    decimal Quantity,
    decimal UnitPrice,
    decimal ValuationRate,
    string? Notes
);

/// <summary>
/// Önceki hakedişlerde açılmış bir ihzarat kaleminden bu hakedişte
/// yapılan mahsup. Açık bakiyeyi aşamaz.
/// </summary>
public sealed record ProgressPaymentAdvanceOffsetRequest(
    Guid AdvanceMaterialId,
    decimal Amount,
    string? Notes
);

/// <summary>Alt kalemli kesintinin (yemek, konaklama, İSG) tek satırı.</summary>
public sealed record ProgressPaymentDeductionLineRequest(
    string Name,
    decimal UnitPrice,
    decimal Quantity,
    decimal VatRate,
    string? Notes
);

/// <param name="BaseAmount">Geriye dönük uyumluluk için korunuyor.
/// Kümülatif taban verilmezse bu kullanılır.</param>
/// <param name="CumulativeBaseAmount">Kesintinin uygulanacağı kümülatif
/// taban. Verilmezse hakedişin kümülatif tutarı esas alınır.</param>
/// <param name="Lines">Alt kalemler; doluysa tutar oranla değil
/// bunlardan hesaplanır.</param>
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
    Guid? AccountingAccountId = null,
    decimal? CumulativeBaseAmount = null,
    IReadOnlyCollection<ProgressPaymentDeductionLineRequest>? Lines = null
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
    IReadOnlyCollection<ProgressPaymentDeductionRequest> Deductions,
    IReadOnlyCollection<ProgressPaymentAdvanceMaterialRequest>? AdvanceMaterials = null,
    IReadOnlyCollection<ProgressPaymentAdvanceOffsetRequest>? AdvanceOffsets = null,
    decimal IncomeTaxWithholdingRate = 0m,
    IReadOnlyCollection<ProgressPaymentPaymentPlanRequest>? PaymentPlans = null
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
    IReadOnlyCollection<ProgressPaymentDeductionRequest> Deductions,
    IReadOnlyCollection<ProgressPaymentAdvanceMaterialRequest>? AdvanceMaterials = null,
    IReadOnlyCollection<ProgressPaymentAdvanceOffsetRequest>? AdvanceOffsets = null,
    decimal IncomeTaxWithholdingRate = 0m,
    IReadOnlyCollection<ProgressPaymentPaymentPlanRequest>? PaymentPlans = null
);

public sealed record CancelProgressPaymentRequest(string? Reason);
