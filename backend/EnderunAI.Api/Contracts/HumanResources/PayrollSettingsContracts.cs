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
    IReadOnlyCollection<PayrollTaxBracketRequest> TaxBrackets);

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
    IReadOnlyCollection<PayrollTaxBracketResponse> TaxBrackets);
