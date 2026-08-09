namespace EnderunAI.Api.Contracts.HumanResources;

public sealed record PayrollTaxBracketRequest(
    int Order,
    decimal LowerBound,
    decimal? UpperBound,
    decimal Rate);

public sealed record UpdatePayrollSettingsRequest(
    decimal MinimumWageGross,
    decimal MinimumWageNet,
    decimal SgkBaseFloor,
    decimal SgkBaseCeiling,
    decimal SgkEmployeeRate,
    decimal UnemploymentEmployeeRate,
    decimal SgkEmployerRate,
    decimal UnemploymentEmployerRate,
    bool SgkEmployerDiscountEnabled,
    decimal SgkEmployerDiscountPoints,
    decimal StampTaxPerMille,
    bool MinimumWageIncomeTaxExemptionEnabled,
    bool MinimumWageStampTaxExemptionEnabled,
    decimal SeveranceCeiling,
    string? SeveranceCeilingPeriodNote,
    IReadOnlyCollection<PayrollTaxBracketRequest> TaxBrackets,
    /// <summary>Günlük normal çalışma süresi (saat). Saatlik ücret bundan türetilir.</summary>
    decimal DailyWorkHours = 7.5m,
    // Nakdî yemek/yol yardımının günlük istisna tavanları. Boş
    // bırakılırsa o yıl için tanımsız sayılır ve istisna uygulanmaz.
    decimal? MealSgkExemptionDailyCap = null,
    decimal? MealIncomeTaxExemptionDailyCap = null,
    decimal? TravelSgkExemptionDailyCap = null,
    decimal? TravelIncomeTaxExemptionDailyCap = null,
    // Yıllık azami fazla mesai saati (yasal 270). Boşsa aşım kontrolü
    // yapılmaz ve bordro ön kontrolü bunu söyler.
    decimal? AnnualOvertimeHourLimit = null);

/// <summary>
/// Parametrelerin yürürlükteki mevzuatla karşılaştırıldığının onayı.
/// Bu onay verilmeden bordro kesinleştirilemez.
/// </summary>
public sealed record VerifyPayrollSettingsRequest(string? VerificationNote);

public sealed record PayrollTaxBracketResponse(
    Guid Id,
    int Order,
    decimal LowerBound,
    decimal? UpperBound,
    decimal Rate);

public sealed record PayrollSettingsResponse(
    Guid Id,
    Guid CompanyId,
    int Year,
    decimal MinimumWageGross,
    decimal MinimumWageNet,
    decimal SgkBaseFloor,
    decimal SgkBaseCeiling,
    decimal SgkEmployeeRate,
    decimal UnemploymentEmployeeRate,
    decimal SgkEmployerRate,
    decimal UnemploymentEmployerRate,
    bool SgkEmployerDiscountEnabled,
    decimal SgkEmployerDiscountPoints,
    decimal StampTaxPerMille,
    bool MinimumWageIncomeTaxExemptionEnabled,
    bool MinimumWageStampTaxExemptionEnabled,
    decimal SeveranceCeiling,
    string? SeveranceCeilingPeriodNote,
    DateTime? VerifiedAtUtc,
    string? VerificationNote,
    bool IsVerified,
    IReadOnlyCollection<PayrollTaxBracketResponse> TaxBrackets,
    decimal DailyWorkHours = 7.5m,
    decimal? MealSgkExemptionDailyCap = null,
    decimal? MealIncomeTaxExemptionDailyCap = null,
    decimal? TravelSgkExemptionDailyCap = null,
    decimal? TravelIncomeTaxExemptionDailyCap = null,
    decimal? AnnualOvertimeHourLimit = null);
