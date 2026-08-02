using EnderunAI.Api.Contracts.PurchaseOrders;

namespace EnderunAI.Api.Services.PurchaseOrders;

public interface IPurchaseOrderService
{
    Task<IReadOnlyList<PurchaseOrderListItemResponse>> GetAllAsync(
        Guid? companyId,
        Guid? projectId,
        int? status,
        CancellationToken cancellationToken);

    Task<PurchaseOrderDetailResponse> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<CreatePurchaseOrderFromRfqResponse> CreateFromRfqAsync(
        Guid rfqId,
        CancellationToken cancellationToken);

    Task<PurchaseOrderActionResponse> SubmitAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<PurchaseOrderActionResponse> ApproveAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<PurchaseOrderActionResponse> RejectAsync(
        Guid id,
        string reason,
        CancellationToken cancellationToken);

    Task<PurchaseOrderActionResponse> CancelAsync(
        Guid id,
        string reason,
        CancellationToken cancellationToken);
}
