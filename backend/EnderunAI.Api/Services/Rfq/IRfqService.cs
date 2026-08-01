using EnderunAI.Api.Contracts.Rfq;

namespace EnderunAI.Api.Services.Rfq;

public interface IRfqService
{
    Task<IReadOnlyList<RfqListItemResponse>> GetAllAsync(
        Guid? companyId,
        int? status,
        CancellationToken cancellationToken);

    Task<RfqDetailResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<CreateRfqResponse> CreateFromPurchaseRequestAsync(
        Guid purchaseRequestId,
        CreateRfqRequest request,
        CancellationToken cancellationToken);

    Task SendAsync(Guid id, CancellationToken cancellationToken);

    Task SaveQuotationAsync(
        Guid rfqId,
        Guid rfqSupplierId,
        SaveQuotationRequest request,
        CancellationToken cancellationToken);

    Task<RfqComparisonResponse> GetComparisonAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<AwardRfqResponse> AwardAsync(
        Guid id,
        Guid rfqSupplierId,
        CancellationToken cancellationToken);

    Task CloseAsync(Guid id, CancellationToken cancellationToken);
}
