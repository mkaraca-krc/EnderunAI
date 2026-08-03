using EnderunAI.Api.Contracts.HumanResources;

namespace EnderunAI.Api.Services.HumanResources;

public interface IHrApprovalService
{
    Task<IReadOnlyList<HrLeaveResponse>> GetLeavesAsync(
        Guid? companyId, Guid? personnelId, Guid? projectId, int? leaveType,
        int? status, DateTime? startDate, DateTime? endDate,
        CancellationToken cancellationToken);
    Task<HrLeaveResponse> CreateLeaveAsync(
        CreateHrLeaveRequest request, Guid? userId, CancellationToken cancellationToken);
    Task<HrLeaveResponse> UpdateLeaveAsync(
        Guid id, UpdateHrLeaveRequest request, Guid? userId,
        CancellationToken cancellationToken);
    Task<HrLeaveResponse> ApproveLeaveAsync(
        Guid id, Guid? userId, CancellationToken cancellationToken);
    Task<HrLeaveResponse> RejectLeaveAsync(
        Guid id, string reason, Guid? userId, CancellationToken cancellationToken);
    Task DeleteLeaveAsync(Guid id, Guid? userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<HrOvertimeResponse>> GetOvertimesAsync(
        Guid? companyId, Guid? personnelId, Guid? projectId, int? status,
        DateTime? startDate, DateTime? endDate, CancellationToken cancellationToken);
    Task<HrOvertimeResponse> CreateOvertimeAsync(
        CreateHrOvertimeRequest request, Guid? userId,
        CancellationToken cancellationToken);
    Task<HrOvertimeResponse> UpdateOvertimeAsync(
        Guid id, UpdateHrOvertimeRequest request, Guid? userId,
        CancellationToken cancellationToken);
    Task<HrOvertimeResponse> ApproveOvertimeAsync(
        Guid id, Guid? userId, CancellationToken cancellationToken);
    Task<HrOvertimeResponse> RejectOvertimeAsync(
        Guid id, string reason, Guid? userId, CancellationToken cancellationToken);
    Task DeleteOvertimeAsync(Guid id, Guid? userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<HrAdvanceResponse>> GetAdvancesAsync(
        Guid? companyId, Guid? personnelId, Guid? projectId, int? status,
        DateTime? startDate, DateTime? endDate, CancellationToken cancellationToken);
    Task<HrAdvanceResponse> CreateAdvanceAsync(
        CreateHrAdvanceRequest request, Guid? userId, CancellationToken cancellationToken);
    Task<HrAdvanceResponse> UpdateAdvanceAsync(
        Guid id, UpdateHrAdvanceRequest request, Guid? userId,
        CancellationToken cancellationToken);
    Task<HrAdvanceResponse> ApproveAdvanceAsync(
        Guid id, Guid? userId, CancellationToken cancellationToken);
    Task<HrAdvanceResponse> RejectAdvanceAsync(
        Guid id, string reason, Guid? userId, CancellationToken cancellationToken);
    Task<HrAdvanceResponse> MarkAdvancePaidAsync(
        Guid id, string? paymentReference, Guid? userId,
        CancellationToken cancellationToken);
    Task DeleteAdvanceAsync(Guid id, Guid? userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PayrollResponse>> GetPayrollsAsync(
        Guid? companyId, Guid? personnelId, int? year, int? month, int? status,
        CancellationToken cancellationToken);
    Task<PayrollResponse> GetPayrollAsync(Guid id, CancellationToken cancellationToken);
    Task<PayrollSummary> GetPayrollSummaryAsync(
        Guid companyId, int year, int month, CancellationToken cancellationToken);
    /// <summary>
    /// Dönemin onaylı bordrolarını tek bir tahakkuk fişiyle
    /// muhasebeleştirir (770 borç / 335 + 360 + 361 alacak).
    /// </summary>
    Task<PayrollPeriodPostingResult> PostPayrollPeriodAsync(
        PostPayrollPeriodRequest request, Guid? userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Tahakkuk etmiş dönemin net ücretini kasa/bankadan öder
    /// (335 borç / 100-102 alacak) ve bordroları ödendi işaretler.
    /// </summary>
    Task<PayrollPeriodPaymentResult> PayPayrollPeriodAsync(
        PayPayrollPeriodRequest request, Guid? userId,
        CancellationToken cancellationToken);

    Task<CompanyPayrollCalculationResult> CalculateCompanyPayrollAsync(
        CalculateCompanyPayrollRequest request, Guid? userId,
        CancellationToken cancellationToken);
    Task<PayrollResponse> ApprovePayrollAsync(
        Guid id, Guid? userId, CancellationToken cancellationToken);
    Task<PayrollResponse> CancelPayrollAsync(
        Guid id, string reason, Guid? userId, CancellationToken cancellationToken);
    Task<PayrollResponse> MarkPayrollPaidAsync(
        Guid id, MarkPayrollPaidRequest request, Guid? userId,
        CancellationToken cancellationToken);
    Task DeletePayrollAsync(Guid id, Guid? userId, CancellationToken cancellationToken);
}
