using EnderunAI.Api.Contracts.Procurement;
using EnderunAI.Api.Contracts.PurchaseOrders;

namespace EnderunAI.Api.Services.Procurement;

public interface IProcurementApprovalService
{
    Task<ProcurementApprovalDashboardResponse> GetDashboardAsync(
        Guid companyId,
        Guid? projectId,
        CancellationToken cancellationToken);

    Task<PurchaseOrderApprovalContextResponse> GetOrderContextAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken);

    Task<ProcurementApprovalPolicyResponse> ConfigurePolicyAsync(
        Guid companyId,
        ConfigureProcurementApprovalPolicyRequest request,
        CancellationToken cancellationToken);

    Task<ProcurementBudgetResponse> CreateBudgetAsync(
        Guid projectId,
        UpsertProcurementBudgetRequest request,
        CancellationToken cancellationToken);

    Task<ProcurementBudgetResponse> UpdateBudgetAsync(
        Guid projectId,
        Guid budgetId,
        UpsertProcurementBudgetRequest request,
        CancellationToken cancellationToken);

    Task<PurchaseOrderActionResponse> SubmitOrderAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken);

    Task<PurchaseOrderActionResponse> ApproveOrderAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken);

    Task<PurchaseOrderActionResponse> RejectOrderAsync(
        Guid purchaseOrderId,
        string reason,
        CancellationToken cancellationToken);
}

