namespace EnderunAI.Api.Contracts.HumanResources;

public sealed record CreateHrLeaveRequest(
    Guid CompanyId,
    Guid PersonnelId,
    Guid? ProjectId,
    int LeaveType,
    DateTime StartDate,
    DateTime EndDate,
    decimal TotalDays,
    string Reason,
    string? DocumentPath);

public sealed record UpdateHrLeaveRequest(
    Guid? ProjectId,
    int LeaveType,
    DateTime StartDate,
    DateTime EndDate,
    decimal TotalDays,
    string Reason,
    string? DocumentPath,
    int Status,
    string? ApprovalNote);

public sealed record HrLeaveResponse(
    Guid Id, Guid CompanyId, Guid PersonnelId, Guid? ProjectId,
    int LeaveType, string LeaveTypeName, DateTime StartDate, DateTime EndDate,
    decimal TotalDays, string Reason, string? DocumentPath, int Status,
    string StatusName, Guid? ApprovedByUserId, DateTime? ApprovedAtUtc,
    string? ApprovalNote, DateTime CreatedAtUtc);

public sealed record CreateHrOvertimeRequest(
    Guid CompanyId,
    Guid PersonnelId,
    Guid? ProjectId,
    DateTime WorkDate,
    decimal RequestedHours,
    bool IsSundayWork,
    bool IsPublicHolidayWork,
    bool IsNightWork,
    string Reason);

public sealed record UpdateHrOvertimeRequest(
    Guid? ProjectId,
    DateTime WorkDate,
    decimal RequestedHours,
    decimal ApprovedHours,
    bool IsSundayWork,
    bool IsPublicHolidayWork,
    bool IsNightWork,
    string Reason,
    int Status,
    string? ApprovalNote);

public sealed record HrOvertimeResponse(
    Guid Id, Guid CompanyId, Guid PersonnelId, Guid? ProjectId,
    DateTime WorkDate, decimal RequestedHours, decimal ApprovedHours,
    bool IsSundayWork, bool IsPublicHolidayWork, bool IsNightWork,
    string Reason, int Status, string StatusName, Guid? ApprovedByUserId,
    DateTime? ApprovedAtUtc, string? ApprovalNote, DateTime CreatedAtUtc);

public sealed record CreateHrAdvanceRequest(
    Guid CompanyId,
    Guid PersonnelId,
    Guid? ProjectId,
    DateTime RequestDate,
    decimal RequestedAmount,
    string CurrencyCode,
    int DeductionInstallmentCount,
    DateTime? FirstDeductionDate,
    string Reason);

public sealed record UpdateHrAdvanceRequest(
    Guid? ProjectId,
    DateTime RequestDate,
    decimal RequestedAmount,
    decimal ApprovedAmount,
    string CurrencyCode,
    int DeductionInstallmentCount,
    DateTime? FirstDeductionDate,
    string Reason,
    int Status,
    string? PaymentReference);

public sealed record HrAdvanceResponse(
    Guid Id, Guid CompanyId, Guid PersonnelId, Guid? ProjectId,
    DateTime RequestDate, decimal RequestedAmount, decimal ApprovedAmount,
    string CurrencyCode, int DeductionInstallmentCount,
    DateTime? FirstDeductionDate, string Reason, int Status, string StatusName,
    Guid? ApprovedByUserId, DateTime? ApprovedAtUtc, DateTime? PaidAtUtc,
    string? PaymentReference, DateTime CreatedAtUtc);

public sealed record PayrollResponse(
    Guid Id, Guid CompanyId, Guid PersonnelId, int Year, int Month,
    decimal GrossSalary, decimal NormalWorkAmount, decimal OvertimeAmount,
    decimal SundayWorkAmount, decimal PublicHolidayAmount, decimal BonusAmount,
    decimal MealAmount, decimal TravelAmount, decimal OtherEarningAmount,
    decimal CompensationAmount, decimal TotalEarnings,
    decimal SgkEmployeeDeduction, decimal IncomeTaxDeduction,
    decimal StampTaxDeduction, decimal AdvanceDeduction,
    decimal OtherDeductionAmount, decimal TotalDeductions,
    decimal OfficialNetPayableAmount, decimal ActualPayableAmount,
    decimal NetPayableAmount, string CurrencyCode, int Status,
    string StatusName, DateTime? ApprovedAtUtc, Guid? ApprovedByUserId,
    DateTime? PaidAtUtc, string? PaymentReference, string? Description,
    DateTime CreatedAtUtc);

/// <summary>
/// Aylık toplu bordro hesabı. Kesinti tutarları artık istekle
/// gönderilmiyor; SGK, gelir ve damga vergisi şirketin bordro
/// parametrelerinden hesaplanıyor.
/// </summary>
public sealed record CalculateCompanyPayrollRequest(
    Guid CompanyId,
    int Year,
    int Month,
    bool RecalculateExisting = false);

public sealed record CompanyPayrollCalculationResult(
    Guid CompanyId, int Year, int Month, int PersonnelCount,
    int CreatedCount, int UpdatedCount, int SkippedCount,
    decimal TotalNetPayableAmount);

public sealed record PayrollSummary(
    Guid CompanyId, int Year, int Month, int PayrollCount,
    int DraftCount, int CalculatedCount, int ApprovedCount, int PaidCount,
    decimal TotalGrossSalary, decimal TotalEarnings, decimal TotalDeductions,
    decimal TotalCompensationAmount, decimal TotalOfficialNetPayableAmount,
    decimal TotalNetPayableAmount, string CurrencyCode);

public sealed record ReasonRequest(string Reason);
public sealed record MarkAdvancePaidRequest(string? PaymentReference);
public sealed record MarkPayrollPaidRequest(
    string? PaymentReference,
    int PaymentMethod,
    Guid? BankAccountId,
    Guid? CashAccountId,
    DateTime PaymentDate);
