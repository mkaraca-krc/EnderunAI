using EnderunAI.Api.Contracts.Purchasing;

namespace EnderunAI.Api.Services.Purchasing.Automation;

public interface IPurchaseRequestGenerator
{
    Task<GeneratePurchaseRequestFromOfferResponse> GenerateFromOfferAsync(
        Guid offerId,
        GeneratePurchaseRequestFromOfferRequest request,
        CancellationToken cancellationToken);
}
