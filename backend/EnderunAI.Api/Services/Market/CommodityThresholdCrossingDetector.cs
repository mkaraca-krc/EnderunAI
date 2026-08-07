using EnderunAI.Api.Models.Market;

namespace EnderunAI.Api.Services.Market;

/// <summary>
/// Fiyat serisindeki tek bir eşik geçişi.
/// </summary>
/// <param name="Direction">Geçişin yönü.</param>
/// <param name="PriceDate">Geçişin gerçekleştiği gün.</param>
/// <param name="PriceUsdPerTon">O günkü fiyat.</param>
/// <param name="PriceTryPerTon">TL karşılığı; kur yoksa null.</param>
/// <param name="ThresholdUsdPerTon">Aşılan eşik.</param>
public sealed record CommodityThresholdCrossing(
    CommodityAlertDirection Direction,
    DateTime PriceDate,
    decimal PriceUsdPerTon,
    decimal? PriceTryPerTon,
    decimal ThresholdUsdPerTon);

/// <summary>
/// Eşik geçişlerini fiyat serisinden bulur — saf, veritabanısız.
///
/// GEÇİŞ, DURUM DEĞİL: bir gün eşiğin altındaysa ve bir ÖNCEKİ işlem
/// günü değilse geçiş vardır. Fiyatın eşiğin altında kaldığı her gün
/// sinyal üretmek, bakır iki hafta ucuz kaldığında on dört kez "alım
/// fırsatı" demek olurdu ve uyarı anlamını yitirirdi.
///
/// Serinin İLK günü geçiş sayılmaz: öncesini bilmediğimiz için o gün
/// zaten eşiğin altında mıydı yoksa yeni mi indi ayırt edilemez.
/// Bilmediğimiz bir şeyi "yeni oldu" diye raporlamak yanlış alarm
/// üretir.
/// </summary>
public static class CommodityThresholdCrossingDetector
{
    /// <summary>
    /// Verilen fiyat serisindeki eşik geçişleri.
    /// </summary>
    /// <param name="points">Fiyat serisi; tarihe göre sıralanır.</param>
    /// <param name="buyBelowUsdPerTon">Alım eşiği; null ise kapalı.</param>
    /// <param name="alertAboveUsdPerTon">Risk eşiği; null ise kapalı.</param>
    public static IReadOnlyList<CommodityThresholdCrossing> Detect(
        IReadOnlyList<CommodityPricePoint> points,
        decimal? buyBelowUsdPerTon,
        decimal? alertAboveUsdPerTon)
    {
        var crossings = new List<CommodityThresholdCrossing>();

        if (points.Count < 2)
            return crossings;

        var ordered = points.OrderBy(x => x.PriceDate).ToList();

        for (var i = 1; i < ordered.Count; i++)
        {
            var previous = ordered[i - 1];
            var current = ordered[i];

            if (buyBelowUsdPerTon is > 0m)
            {
                var threshold = buyBelowUsdPerTon.Value;

                if (current.PriceUsdPerTon < threshold &&
                    previous.PriceUsdPerTon >= threshold)
                {
                    crossings.Add(new CommodityThresholdCrossing(
                        CommodityAlertDirection.BuyOpportunity,
                        current.PriceDate,
                        current.PriceUsdPerTon,
                        current.PriceTryPerTon,
                        threshold));
                }
            }

            if (alertAboveUsdPerTon is > 0m)
            {
                var threshold = alertAboveUsdPerTon.Value;

                if (current.PriceUsdPerTon > threshold &&
                    previous.PriceUsdPerTon <= threshold)
                {
                    crossings.Add(new CommodityThresholdCrossing(
                        CommodityAlertDirection.CostRisk,
                        current.PriceDate,
                        current.PriceUsdPerTon,
                        current.PriceTryPerTon,
                        threshold));
                }
            }
        }

        return crossings;
    }

    /// <summary>
    /// Serinin SON gününde eşiğin hangi tarafında olduğumuz. Geçiş
    /// değil, o anki durumdur; ekranda "şu an alım bölgesinde" demek
    /// için kullanılır.
    /// </summary>
    /// <returns>Yürürlükteki durum; hiçbir eşiğin dışında değilse null.</returns>
    public static CommodityAlertDirection? CurrentState(
        IReadOnlyList<CommodityPricePoint> points,
        decimal? buyBelowUsdPerTon,
        decimal? alertAboveUsdPerTon)
    {
        if (points.Count == 0)
            return null;

        var latest = points.OrderBy(x => x.PriceDate).Last();

        if (buyBelowUsdPerTon is > 0m &&
            latest.PriceUsdPerTon < buyBelowUsdPerTon.Value)
        {
            return CommodityAlertDirection.BuyOpportunity;
        }

        if (alertAboveUsdPerTon is > 0m &&
            latest.PriceUsdPerTon > alertAboveUsdPerTon.Value)
        {
            return CommodityAlertDirection.CostRisk;
        }

        return null;
    }
}
