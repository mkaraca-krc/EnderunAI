using EnderunAI.Api.Contracts.GoodsReceipts;
using EnderunAI.Api.Contracts.Core;

namespace EnderunAI.Api.Services.GoodsReceipts;

public interface IGoodsReceiptService
{
    /// <summary>
    /// Sayfalanmış mal kabul listesi. Arama SUNUCUDA ve katlanmış
    /// (bkz. enderun_fold); toplam sayı ayrı sorgulanıyor ki ekran
    /// "kaç kayıt var" derken tahmin yürütmesin.
    /// </summary>
    Task<PagedResult<GoodsReceiptListItemResponse>> GetAllAsync(
        Guid? companyId,
        Guid? warehouseId,
        Guid? purchaseOrderId,
        int? status,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>Özet kartları — süzgeçlere uyan TÜM kayıtlardan.</summary>
    Task<GoodsReceiptSummaryResponse> GetSummaryAsync(
        Guid? companyId,
        Guid? warehouseId,
        Guid? purchaseOrderId,
        string? search,
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

