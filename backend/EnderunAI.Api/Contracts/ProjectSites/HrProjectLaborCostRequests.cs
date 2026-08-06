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
    string? CurrencyCode,
    /// <summary>
    /// İşçiliğin gittiği icmal satırı (poz). OPSİYONEL — doldurulursa
    /// maliyet o poza ölçülmüş olarak yazılır.
    /// </summary>
    Guid? ProjectBoqItemId = null);
