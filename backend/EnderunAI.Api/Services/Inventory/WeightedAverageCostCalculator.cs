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

    /// <summary>
    /// Tedarikçiye iade sonrası ortalama maliyet — girişin geri
    /// sarılması.
    ///
    /// Mal, GİRDİĞİ fiyatla çıkarılır: iade güncel ortalamayla
    /// çıkarılsaydı, arada pahalı bir alım yapıldığında ucuz malın
    /// iadesi kalan stoğun ortalamasını yapay olarak yükseltirdi.
    ///
    /// İki durumda ortalama olduğu gibi bırakılır: stok tamamen
    /// tükeniyorsa (bölünecek miktar kalmaz) ve kalan değer negatife
    /// düşüyorsa (veri zaten tutarsız; ortalamayı büsbütün bozmak
    /// yerine son bilinen değerde tutulur).
    /// </summary>
    /// <param name="priorQuantity">İadeden ÖNCEKİ toplam miktar.</param>
    /// <param name="priorAverageCost">İadeden önceki ortalama maliyet (TRY).</param>
    /// <param name="outgoingQuantity">İade edilen miktar.</param>
    /// <param name="outgoingUnitCost">Malın giriş (orijinal fatura) birim maliyeti.</param>
    public static decimal Remove(
        decimal priorQuantity,
        decimal priorAverageCost,
        decimal outgoingQuantity,
        decimal outgoingUnitCost)
    {
        if (outgoingQuantity <= 0m)
            return priorAverageCost;

        var remainingQuantity = priorQuantity - outgoingQuantity;

        if (remainingQuantity <= 0m)
            return priorAverageCost;

        var remainingValue = (priorQuantity * priorAverageCost) -
                             (outgoingQuantity * outgoingUnitCost);

        if (remainingValue <= 0m)
            return priorAverageCost;

        return remainingValue / remainingQuantity;
    }
}
