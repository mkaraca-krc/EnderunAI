namespace EnderunAI.Api.Contracts.Engineering;

public sealed record RecipeMaterialRequest(
    Guid? InventoryItemId,
    string MaterialCode,
    string MaterialName,
    decimal Quantity,
    string Unit,
    decimal WastePercent,
    string? Notes);

public sealed record RecipeLaborRequest(
    int LaborType,
    decimal PersonCount,
    decimal Hours,
    string? Notes);

public sealed record RecipeMachineRequest(
    string MachineName,
    decimal Quantity,
    decimal Hours,
    string? Notes);

public sealed record CreateEngineeringRecipeRequest(
    Guid EngineeringPositionId,
    string? Description,
    bool IsDefault,
    IReadOnlyCollection<RecipeMaterialRequest> Materials,
    IReadOnlyCollection<RecipeLaborRequest> Labors,
    IReadOnlyCollection<RecipeMachineRequest> Machines);

public sealed record UpdateEngineeringRecipeRequest(
    string? Description,
    bool IsDefault,
    IReadOnlyCollection<RecipeMaterialRequest> Materials,
    IReadOnlyCollection<RecipeLaborRequest> Labors,
    IReadOnlyCollection<RecipeMachineRequest> Machines);
