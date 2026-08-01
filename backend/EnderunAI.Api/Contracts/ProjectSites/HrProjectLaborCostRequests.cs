namespace EnderunAI.Api.Contracts.ProjectSites;

public sealed record CreateHrProjectLaborCostRequest(
    Guid PersonnelId,
    Guid? ProjectSiteId,
    DateTime WorkDate,
    decimal NormalHours,
    decimal OvertimeHours,
    decimal NormalCost,
    decimal OvertimeCost,
    decimal OtherCost,
    string? CurrencyCode);
