namespace EnderunAI.Api.Contracts.Purchasing;

public sealed record GeneratePurchaseRequestFromOfferRequest(
    string RequestedByName,
    DateTime? NeededByDate,
    int Priority);

public sealed record GeneratePurchaseRequestFromOfferResponse(
    Guid PurchaseRequestId,
    string RequestNumber,
    Guid OfferId,
    string OfferNumber,
    int SourceOfferItemCount,
    int GeneratedMaterialCount,
    decimal TotalQuantity);
