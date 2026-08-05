namespace EnderunAI.Api.Contracts.Purchasing;

public sealed record CreatePurchaseRequestItemRequest(
    string MaterialDescription,
    decimal Quantity,
    string Unit,
    DateTime? RequestedDeliveryDate,
    string? Notes,
    /// <summary>
    /// Talep edilen stok kartı. Opsiyonel ve en sonda: katalogda olmayan
    /// malzeme de talep edilebilmeli, mevcut çağıranlar da bozulmamalı.
    /// Seçilirse zincir mal kabule kadar kopmadan taşınır.
    /// </summary>
    Guid? InventoryItemId = null);

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
