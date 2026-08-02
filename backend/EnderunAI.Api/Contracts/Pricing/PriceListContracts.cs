namespace EnderunAI.Api.Contracts.Pricing;

public sealed record CreateManufacturerPriceListItemRequest(
    string ProductCode,
    string ProductDescription,
    string Unit,
    decimal ListPrice,
    string? Category,
    string? Brand,
    string? Model);

public sealed record CreateManufacturerPriceListRequest(
    Guid CompanyId,
    string ManufacturerName,
    string ListName,
    DateTime ListDate,
    DateTime? ValidUntil,
    string Currency,
    IReadOnlyCollection<CreateManufacturerPriceListItemRequest> Items);

public sealed record UpdateManufacturerPriceListRequest(
    string ManufacturerName,
    string ListName,
    DateTime ListDate,
    DateTime? ValidUntil,
    string Currency,
    IReadOnlyCollection<CreateManufacturerPriceListItemRequest> Items);
