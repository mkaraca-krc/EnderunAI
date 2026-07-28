using EnderunAI.Api.Contracts.Finance;

namespace EnderunAI.Api.Services.Finance;

public interface IFinanceDashboardService
{
    Task<FinanceDashboardResponse> GetDashboardAsync(
        Guid? companyId,
        Guid? projectId,
        Guid? hierarchyNodeId,
        CancellationToken cancellationToken);

    Task<CurrentAccountFinanceSummaryResponse> GetCurrentAccountSummaryAsync(
        Guid? companyId,
        Guid? projectId,
        Guid? hierarchyNodeId,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ProjectFinanceSummaryResponse>>
        GetProjectsSummaryAsync(
            Guid? companyId,
            Guid? projectId,
            Guid? hierarchyNodeId,
            CancellationToken cancellationToken);

    Task<CashFlowSummaryResponse> GetCashFlowAsync(
        Guid? companyId,
        Guid? projectId,
        Guid? hierarchyNodeId,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<SupplierBalanceSummaryResponse>>
        GetSuppliersSummaryAsync(
            Guid? companyId,
            Guid? projectId,
            Guid? hierarchyNodeId,
            CancellationToken cancellationToken);
}
