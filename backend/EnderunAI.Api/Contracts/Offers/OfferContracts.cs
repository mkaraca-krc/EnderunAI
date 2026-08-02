namespace EnderunAI.Api.Contracts.Offers;

public sealed record CreateOfferItemRequest(
    string? PositionNumber,

    Guid? EngineeringPositionId,
    Guid? EngineeringRecipeId,
    int? RecipeVersion,

    string Description,

    Guid? ManufacturerPriceListItemId,
    string? ManufacturerName,
    string? ProductCode,
    string? Brand,
    string? Model,

    decimal Quantity,
    string Unit,

    decimal ListPrice,
    decimal DiscountRate,

    decimal FreightRate,
    decimal WasteRate,
    decimal FinanceRate,
    decimal GeneralExpenseRate,
    decimal ProfitRate,

    string? Notes);

public sealed record CreateOfferRequest(
    Guid CompanyId,
    Guid? ProjectId,
    Guid? CustomerId,

    string Title,

    DateTime OfferDate,
    DateTime? ValidUntil,

    string Currency,
    decimal ExchangeRate,

    string? Description,
    string? Notes,

    IReadOnlyCollection<CreateOfferItemRequest> Items);

public sealed record CalculateOfferItemRequest(
    decimal Quantity,
    decimal ListPrice,
    decimal DiscountRate,
    decimal FreightRate,
    decimal WasteRate,
    decimal FinanceRate,
    decimal GeneralExpenseRate,
    decimal ProfitRate);

public sealed record CalculateOfferItemResponse(
    decimal NetPurchasePrice,
    decimal UnitCost,
    decimal UnitSalesPrice,
    decimal CostTotal,
    decimal SalesTotal,
    decimal ProfitTotal);
