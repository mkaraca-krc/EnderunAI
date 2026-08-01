using EnderunAI.Api.Contracts.GoodsReceipts;

namespace EnderunAI.Api.Services.GoodsReceipts;

public interface IGoodsReceiptService
{
    Task<IReadOnlyList<GoodsReceiptListItemResponse>> GetAllAsync(
        Guid? companyId,
        Guid? warehouseId,
        Guid? purchaseOrderId,
        int? status,
        CancellationToken cancellationToken);

    Task<GoodsReceiptDetailResponse> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<CreateGoodsReceiptResponse> CreateFromPurchaseOrderAsync(
        Guid purchaseOrderId,
        CreateGoodsReceiptRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<GoodsReceiptInventoryOptionResponse>> GetInventoryOptionsAsync(
        Guid id,
        string? search,
        CancellationToken cancellationToken);

    Task<GoodsReceiptActionResponse> UpdateDraftAsync(
        Guid id,
        UpdateGoodsReceiptDraftRequest request,
        CancellationToken cancellationToken);

    Task<GoodsReceiptActionResponse> PostAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<GoodsReceiptActionResponse> CancelAsync(
        Guid id,
        string reason,
        CancellationToken cancellationToken);
}

