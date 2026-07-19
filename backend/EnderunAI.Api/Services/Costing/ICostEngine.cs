using EnderunAI.Api.Contracts.OfferCosting;

namespace EnderunAI.Api.Services.Costing;

public interface ICostEngine
{
    Task<EstimatePositionCostResponse> EstimatePositionAsync(
        EstimatePositionCostRequest request,
        CancellationToken cancellationToken);
}
