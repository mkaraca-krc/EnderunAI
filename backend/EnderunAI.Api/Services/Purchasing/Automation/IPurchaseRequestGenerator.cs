using EnderunAI.Api.Contracts.Inventory;
using EnderunAI.Api.Contracts.Purchasing;

namespace EnderunAI.Api.Services.Purchasing.Automation;

public interface IPurchaseRequestGenerator
{
    Task<GeneratePurchaseRequestFromOfferResponse> GenerateFromOfferAsync(
        Guid offerId,
        GeneratePurchaseRequestFromOfferRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Asgari seviyesinin altına düşmüş depo kalemlerinden talep üretir.
    /// Tekliften üretimden farkı kaynağıdır: orada "kazandığımız iş ne
    /// gerektiriyor", burada "depoda ne tükendi".
    /// </summary>
    Task<GeneratePurchaseRequestFromStockLevelsResponse> GenerateFromStockLevelsAsync(
        GeneratePurchaseRequestFromStockLevelsRequest request,
        Guid? requestedByUserId,
        CancellationToken cancellationToken);
}
