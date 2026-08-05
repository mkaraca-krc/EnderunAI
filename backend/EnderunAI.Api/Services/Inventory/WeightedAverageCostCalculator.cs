namespace EnderunAI.Api.Services.Inventory;

/// <summary>
/// Stok girişinde ağırlıklı ortalama maliyet.
///
/// Saf ve veritabanısız. İki giriş yolu var — mal kabul ve doğrudan
/// alış faturası — ve ikisi de buradan geçiyor. Formül iki yere
/// kopyalansaydı biri güncellenip diğeri unutulur, aynı malzeme hangi
/// kapıdan girdiğine göre farklı maliyet taşırdı.
/// </summary>
public static class WeightedAverageCostCalculator
{
    /// <summary>
    /// Yeni ortalama maliyet.
    ///
    /// Elde stok yoksa (veya negatife düşmüşse) yeni birim maliyet
    /// doğrudan geçerlidir: sıfır miktarın ortalaması alınamaz ve eski
    /// ortalamayı taşımak, ilgisiz bir fiyatı sonsuza kadar sürüklerdi.
    /// </summary>
    /// <param name="priorQuantity">Girişten ÖNCEKİ toplam miktar.</param>
    /// <param name="priorAverageCost">Girişten önceki ortalama maliyet (TRY).</param>
    /// <param name="incomingQuantity">Giren miktar.</param>
    /// <param name="incomingUnitCost">Giren malın TRY birim maliyeti.</param>
    public static decimal Next(
        decimal priorQuantity,
        decimal priorAverageCost,
        decimal incomingQuantity,
        decimal incomingUnitCost)
    {
        if (incomingQuantity <= 0m)
            return priorAverageCost;

        if (priorQuantity <= 0m)
            return incomingUnitCost;

        return ((priorQuantity * priorAverageCost) +
                (incomingQuantity * incomingUnitCost)) /
               (priorQuantity + incomingQuantity);
    }
}
