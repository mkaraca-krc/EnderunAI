using EnderunAI.Api.Models;

namespace EnderunAI.Api.Services.Inventory;

public interface IGoodsReceiptPostingService
{
    Task<GoodsReceipt> PostAsync(Guid goodsReceiptId, Guid? userId, CancellationToken cancellationToken = default);
}
