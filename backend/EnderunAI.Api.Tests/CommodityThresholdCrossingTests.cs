using EnderunAI.Api.Models.Market;
using EnderunAI.Api.Services.Market;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Eşik geçişi tespiti (E4) — veritabanısız.
///
/// Bu paketin konusu tek bir ayrım: GEÇİŞ mi, DURUM mu. Fiyatın eşiğin
/// altında kaldığı her gün sinyal üretmek, bakır iki hafta ucuz
/// kaldığında on dört kez "alım fırsatı" demek olurdu; uyarı okunmaz
/// hâle gelir ve gerçekten yeni bir fırsat geldiğinde fark edilmez.
/// </summary>
public sealed class CommodityThresholdCrossingTests
{
    private static CommodityPricePoint Point(int day, decimal usd) =>
        new(new DateTime(2026, 3, day, 0, 0, 0, DateTimeKind.Utc),
            usd, usd * 30m, 30m);

    /// <summary>
    /// Fiyat eşiğin üstünden altına indiğinde TEK bir alım sinyali
    /// üretilmeli.
    /// </summary>
    [Fact]
    public void CrossingBelowBuyThreshold_ProducesOneSignal()
    {
        var points = new[]
        {
            Point(1, 9_500m),
            Point(2, 9_200m),
            Point(3, 8_800m)
        };

        var crossings = CommodityThresholdCrossingDetector.Detect(
            points, buyBelowUsdPerTon: 9_000m, alertAboveUsdPerTon: null);

        var crossing = Assert.Single(crossings);
        Assert.Equal(CommodityAlertDirection.BuyOpportunity, crossing.Direction);
        Assert.Equal(new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc),
            crossing.PriceDate);
        Assert.Equal(8_800m, crossing.PriceUsdPerTon);
        Assert.Equal(9_000m, crossing.ThresholdUsdPerTon);
    }

    /// <summary>
    /// Fiyat eşiğin altında KALMAYA devam ederse yeni sinyal
    /// üretilmemeli — bu paketin asıl güvencesi.
    /// </summary>
    [Fact]
    public void StayingBelowThreshold_DoesNotRepeatSignal()
    {
        var points = new[]
        {
            Point(1, 9_500m),
            Point(2, 8_800m),
            Point(3, 8_700m),
            Point(4, 8_600m),
            Point(5, 8_500m)
        };

        var crossings = CommodityThresholdCrossingDetector.Detect(
            points, buyBelowUsdPerTon: 9_000m, alertAboveUsdPerTon: null);

        Assert.Single(crossings);
        Assert.Equal(new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            crossings[0].PriceDate);
    }

    /// <summary>
    /// Fiyat çıkıp tekrar inerse İKİ ayrı sinyal olmalı: ikinci iniş
    /// gerçekten yeni bir fırsattır.
    /// </summary>
    [Fact]
    public void ReEnteringThreshold_ProducesSecondSignal()
    {
        var points = new[]
        {
            Point(1, 9_500m),
            Point(2, 8_800m),
            Point(3, 9_300m),
            Point(4, 8_700m)
        };

        var crossings = CommodityThresholdCrossingDetector.Detect(
            points, buyBelowUsdPerTon: 9_000m, alertAboveUsdPerTon: null);

        Assert.Equal(2, crossings.Count);
        Assert.All(crossings, x =>
            Assert.Equal(CommodityAlertDirection.BuyOpportunity, x.Direction));
    }

    /// <summary>
    /// Serinin İLK günü geçiş sayılmamalı: öncesini bilmediğimiz için
    /// o gün zaten ucuz muydu yoksa yeni mi indi ayırt edilemez.
    /// Bilmediğimizi "yeni oldu" diye raporlamak yanlış alarmdır.
    /// </summary>
    [Fact]
    public void FirstPointIsNeverACrossing()
    {
        var points = new[]
        {
            Point(1, 8_500m),
            Point(2, 8_400m)
        };

        Assert.Empty(CommodityThresholdCrossingDetector.Detect(
            points, buyBelowUsdPerTon: 9_000m, alertAboveUsdPerTon: null));
    }

    /// <summary>Risk eşiğini yukarı aşmak uyarı üretmeli.</summary>
    [Fact]
    public void CrossingAboveAlertThreshold_ProducesCostRisk()
    {
        var points = new[]
        {
            Point(1, 10_000m),
            Point(2, 10_600m)
        };

        var crossings = CommodityThresholdCrossingDetector.Detect(
            points, buyBelowUsdPerTon: null, alertAboveUsdPerTon: 10_500m);

        var crossing = Assert.Single(crossings);
        Assert.Equal(CommodityAlertDirection.CostRisk, crossing.Direction);
    }

    /// <summary>Eşik tanımlı değilse sinyal üretilmemeli.</summary>
    [Fact]
    public void WithoutThresholds_NoSignals()
    {
        var points = new[] { Point(1, 9_500m), Point(2, 100m) };

        Assert.Empty(CommodityThresholdCrossingDetector.Detect(
            points, buyBelowUsdPerTon: null, alertAboveUsdPerTon: null));
    }

    /// <summary>Tek noktalı seride karşılaştırılacak önceki gün yok.</summary>
    [Fact]
    public void SinglePoint_NoSignals()
    {
        Assert.Empty(CommodityThresholdCrossingDetector.Detect(
            [Point(1, 100m)], buyBelowUsdPerTon: 9_000m, alertAboveUsdPerTon: null));
    }

    /// <summary>
    /// Anlık durum, geçişten bağımsız okunabilmeli: ekranda "şu an
    /// alım bölgesinde" demek için.
    /// </summary>
    [Fact]
    public void CurrentState_ReflectsLatestPoint()
    {
        var points = new[] { Point(1, 9_500m), Point(2, 8_800m) };

        Assert.Equal(
            CommodityAlertDirection.BuyOpportunity,
            CommodityThresholdCrossingDetector.CurrentState(
                points, buyBelowUsdPerTon: 9_000m, alertAboveUsdPerTon: 10_500m));

        Assert.Null(CommodityThresholdCrossingDetector.CurrentState(
            [Point(1, 9_500m)], buyBelowUsdPerTon: 9_000m,
            alertAboveUsdPerTon: 10_500m));
    }

    /// <summary>
    /// Seri sırasız gelse de sonuç aynı olmalı; kaynak sıralamasına
    /// güvenmiyoruz.
    /// </summary>
    [Fact]
    public void UnorderedInput_IsSortedBeforeDetection()
    {
        var points = new[]
        {
            Point(3, 8_800m),
            Point(1, 9_500m),
            Point(2, 9_200m)
        };

        var crossings = CommodityThresholdCrossingDetector.Detect(
            points, buyBelowUsdPerTon: 9_000m, alertAboveUsdPerTon: null);

        Assert.Single(crossings);
        Assert.Equal(new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc),
            crossings[0].PriceDate);
    }
}
