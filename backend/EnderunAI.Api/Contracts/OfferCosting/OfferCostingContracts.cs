namespace EnderunAI.Api.Contracts.OfferCosting;

public sealed record EstimatePositionCostRequest(
    Guid CompanyId,
    Guid EngineeringPositionId,
    string Currency,
    decimal LaborHourRate,
    decimal MachineHourRate);

public sealed record EstimatedMaterialCost(
    Guid RecipeMaterialId,
    string MaterialCode,
    string MaterialName,
    decimal RecipeQuantity,
    decimal WastePercent,
    decimal EffectiveQuantity,
    bool PriceFound,
    Guid? ManufacturerPriceListItemId,
    string? Manufacturer,
    string? ProductCode,
    string? Brand,
    string? Model,
    decimal UnitPrice,
    decimal TotalPrice,
    string Currency);

public sealed record EstimatePositionCostResponse(
    Guid EngineeringPositionId,
    string PositionCode,
    string PositionName,
    string PositionUnit,
    Guid EngineeringRecipeId,
    int RecipeVersion,
    decimal MaterialCost,
    decimal LaborHours,
    decimal LaborCost,
    decimal MachineHours,
    decimal MachineCost,
    decimal UnitCost,
    int PricedMaterialCount,
    int UnpricedMaterialCount,
    IReadOnlyList<EstimatedMaterialCost> Materials);
