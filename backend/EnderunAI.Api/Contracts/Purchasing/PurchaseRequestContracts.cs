namespace EnderunAI.Api.Contracts.Purchasing;

public sealed record CreatePurchaseRequestItemRequest(
    string MaterialDescription,
    decimal Quantity,
    string Unit,
    DateTime? RequestedDeliveryDate,
    string? Notes);

public sealed record CreatePurchaseRequestRequest(
    Guid CompanyId,
    Guid ProjectId,
    DateTime RequestDate,
    DateTime? NeededByDate,
    string RequestedByName,
    string? Description,
    int Priority,
    IReadOnlyCollection<CreatePurchaseRequestItemRequest> Items);

public sealed record UpdatePurchaseRequestRequest(
    DateTime RequestDate,
    DateTime? NeededByDate,
    string RequestedByName,
    string? Description,
    int Priority,
    IReadOnlyCollection<CreatePurchaseRequestItemRequest> Items);

public sealed record CancelPurchaseRequestRequest(
    string? Reason);
