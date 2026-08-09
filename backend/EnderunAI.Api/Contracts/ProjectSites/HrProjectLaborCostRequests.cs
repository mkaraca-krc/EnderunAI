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
    Guid? ProjectBoqItemId = null,
    // Ek maliyet bileşenleri. Puantajdan üretilen satırlarda ek ücret
    // kalemlerinden gelir; elle girilen satırda buradan yazılır.
    // Toplama girmedikleri sürece kâr olduğundan yüksek görünüyordu.
    decimal MealCost = 0m,
    decimal AccommodationCost = 0m,
    decimal ShuttleCost = 0m,
    // Elden ödeme payı: yetkisiz kullanıcıya maskelenir.
    decimal CompensationCost = 0m);
