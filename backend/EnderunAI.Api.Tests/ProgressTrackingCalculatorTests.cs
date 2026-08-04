using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Hakedis;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Keşif–gerçekleşen motoru. Paketin asıl fikri tek cümlede: aynı sapma
/// birim fiyatlı işte fırsat, anahtar teslimde zarardır. Bu testler o
/// ayrımın bozulmadığını garanti eder.
/// </summary>
public sealed class ProgressTrackingCalculatorTests
{
    private static TrackingItemInput Item(
        decimal contract,
        decimal realized,
        ProjectContractType contractType,
        decimal unitPrice = 100m,
        decimal? issuedStock = null) =>
        new("1.01", "Test kalemi", "m", null, null,
            contract, realized, unitPrice, issuedStock, contractType);

    // ---------- Temel hesap ----------

    [Fact]
    public void Deviation_IsRealizedMinusContract()
    {
        var result = ProgressTrackingCalculator.CalculateItem(
            Item(contract: 100m, realized: 130m, ProjectContractType.UnitPrice));

        Assert.Equal(30m, result.DeviationQuantity);
        Assert.Equal(30m, result.DeviationRate);
        Assert.Equal(-30m, result.RemainingQuantity);
        Assert.Equal(3_000.00m, result.DeviationAmount);
    }

    /// <summary>Tutar etkisi = fark × birim fiyat; azalışta negatif.</summary>
    [Fact]
    public void DeviationAmount_IsNegativeWhenUnderrun()
    {
        var result = ProgressTrackingCalculator.CalculateItem(
            Item(contract: 100m, realized: 80m, ProjectContractType.LumpSum,
                unitPrice: 250m));

        Assert.Equal(-20m, result.DeviationQuantity);
        Assert.Equal(-5_000.00m, result.DeviationAmount);
        Assert.Equal(20m, result.RemainingQuantity);
    }

    /// <summary>
    /// Sözleşme miktarı sıfır olan kalem (tamamen keşif dışı iş) oran
    /// hesaplatmaz ve sıfıra bölme üretmez.
    /// </summary>
    [Fact]
    public void ZeroContractQuantity_DoesNotDivideByZeroOrWarn()
    {
        var result = ProgressTrackingCalculator.CalculateItem(
            Item(contract: 0m, realized: 45m, ProjectContractType.UnitPrice));

        Assert.Equal(0m, result.DeviationRate);
        Assert.False(result.ExceedsWarningThreshold);
    }

    // ---------- Sözleşme tipine göre yorum ----------

    /// <summary>
    /// AYNI SAPMA, ZIT ANLAM. Birim fiyatlıda keşif üstü gerçekleşme
    /// fırsat; anahtar teslimde kâr erozyonu.
    /// </summary>
    [Fact]
    public void SameOverrun_MeansOpportunityOnUnitPriceButErosionOnLumpSum()
    {
        var unitPrice = ProgressTrackingCalculator.CalculateItem(
            Item(100m, 130m, ProjectContractType.UnitPrice));

        var lumpSum = ProgressTrackingCalculator.CalculateItem(
            Item(100m, 130m, ProjectContractType.LumpSum));

        Assert.Equal(DeviationImpact.Opportunity, unitPrice.Impact);
        Assert.Equal(DeviationImpact.ProfitErosion, lumpSum.Impact);

        // Tutar aynı; anlamı farklı.
        Assert.Equal(unitPrice.DeviationAmount, lumpSum.DeviationAmount);
    }

    /// <summary>
    /// Keşif altı kalma: birim fiyatlıda yalnızca bilgi (hakediş de az
    /// olur), anahtar teslimde tasarruf.
    /// </summary>
    [Fact]
    public void SameUnderrun_MeansInformationOnUnitPriceButSavingOnLumpSum()
    {
        Assert.Equal(DeviationImpact.Information,
            ProgressTrackingCalculator.CalculateItem(
                Item(100m, 80m, ProjectContractType.UnitPrice)).Impact);

        Assert.Equal(DeviationImpact.Saving,
            ProgressTrackingCalculator.CalculateItem(
                Item(100m, 80m, ProjectContractType.LumpSum)).Impact);
    }

    [Fact]
    public void NoDeviation_HasNoImpact()
    {
        Assert.Equal(DeviationImpact.None,
            ProgressTrackingCalculator.CalculateItem(
                Item(100m, 100m, ProjectContractType.LumpSum)).Impact);
    }

    /// <summary>
    /// Sözleşme tipi belirlenmemişse sapma YORUMLANMAZ. Yanlış varsayım
    /// yanlış renk ve yanlış alarm üretirdi.
    /// </summary>
    [Fact]
    public void UndeterminedContractType_IsNotInterpreted()
    {
        var result = ProgressTrackingCalculator.CalculateItem(
            Item(100m, 200m, ProjectContractType.Undetermined));

        Assert.Equal(DeviationImpact.Undetermined, result.Impact);
        // Sapma yine hesaplanır, yalnızca yorumlanmaz.
        Assert.Equal(10_000.00m, result.DeviationAmount);
    }

    // ---------- Karma proje ----------

    /// <summary>Karma projede bölümün tipi projeninkini ezer.</summary>
    [Theory]
    [InlineData(ProjectContractType.LumpSum, ProjectContractType.LumpSum)]
    [InlineData(ProjectContractType.UnitPrice, ProjectContractType.UnitPrice)]
    public void MixedProject_UsesSectionContractType(
        ProjectContractType sectionType, ProjectContractType expected)
    {
        Assert.Equal(expected,
            ProgressTrackingCalculator.ResolveEffectiveContractType(
                ProjectContractType.Mixed, sectionType));
    }

    /// <summary>
    /// Karma projede bölüm tipi seçilmemişse yorumlanmaz — proje tipi
    /// "Karma" olduğu için tek başına bir anlam taşımıyor.
    /// </summary>
    [Fact]
    public void MixedProject_WithoutSectionType_IsUndetermined()
    {
        Assert.Equal(ProjectContractType.Undetermined,
            ProgressTrackingCalculator.ResolveEffectiveContractType(
                ProjectContractType.Mixed, null));
    }

    /// <summary>
    /// Karma OLMAYAN projede bölüm tipi yok sayılır; aksi halde tek bir
    /// bölüm ayarı tüm projeyi yanlış yorumlatırdı.
    /// </summary>
    [Fact]
    public void NonMixedProject_IgnoresSectionContractType()
    {
        Assert.Equal(ProjectContractType.LumpSum,
            ProgressTrackingCalculator.ResolveEffectiveContractType(
                ProjectContractType.LumpSum, ProjectContractType.UnitPrice));
    }

    // ---------- Uyarı eşiği ----------

    /// <summary>%110 eşiği: altında uyarı yok, üstünde var.</summary>
    [Theory]
    [InlineData(109.9, false)]
    [InlineData(110, false)]
    [InlineData(110.1, true)]
    [InlineData(200, true)]
    public void WarningThreshold_TriggersAboveOneHundredTen(
        decimal realizedPercent, bool expected)
    {
        var result = ProgressTrackingCalculator.CalculateItem(
            Item(contract: 100m, realized: realizedPercent,
                ProjectContractType.LumpSum));

        Assert.Equal(expected, result.ExceedsWarningThreshold);
    }

    // ---------- Toplamlar ----------

    [Fact]
    public void Totals_SeparateOverrunAndUnderrun()
    {
        var items = new[]
        {
            ProgressTrackingCalculator.CalculateItem(
                Item(100m, 130m, ProjectContractType.LumpSum, unitPrice: 100m)),
            ProgressTrackingCalculator.CalculateItem(
                Item(200m, 180m, ProjectContractType.LumpSum, unitPrice: 50m)),
        };

        var totals = ProgressTrackingCalculator.CalculateTotals(items);

        Assert.Equal(3_000.00m, totals.OverrunAmount);
        Assert.Equal(1_000.00m, totals.UnderrunAmount);
        Assert.Equal(2_000.00m, totals.NetDeviationAmount);

        // Sözleşme 100×100 + 200×50 = 20.000; gerçekleşen 13.000 + 9.000
        Assert.Equal(20_000.00m, totals.ContractAmount);
        Assert.Equal(22_000.00m, totals.RealizedAmount);
        Assert.Equal(110.00m, totals.PhysicalCompletionRate);
    }

    // ---------- Kâr tahmini ----------

    /// <summary>
    /// 10.000.000 sözleşme, %50 gerçekleşme, 4.000.000 fiili maliyet →
    /// tahmini toplam maliyet 8.000.000, tahmini kâr 2.000.000 (%20).
    /// </summary>
    [Fact]
    public void ProfitEstimate_ExtrapolatesCostFromCompletion()
    {
        var estimate = ProgressTrackingCalculator.EstimateProfit(
            contractAmount: 10_000_000m,
            actualCost: 4_000_000m,
            physicalCompletionRate: 50m);

        Assert.True(estimate.IsReliable);
        Assert.Equal(8_000_000.00m, estimate.EstimatedTotalCost);
        Assert.Equal(2_000_000.00m, estimate.EstimatedProfit);
        Assert.Equal(20.00m, estimate.EstimatedProfitRate);
    }

    /// <summary>
    /// Gerçekleşme eşiğin altındayken tahmin ÜRETİLMEZ — bölen
    /// küçüldükçe sonuç uçar ve yanıltır.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(9.99)]
    public void ProfitEstimate_IsNotProducedBelowMinimumCompletion(decimal completion)
    {
        var estimate = ProgressTrackingCalculator.EstimateProfit(
            10_000_000m, 400_000m, completion);

        Assert.False(estimate.IsReliable);
        Assert.Equal(0m, estimate.EstimatedProfit);
        Assert.Contains("yanıltıcı", estimate.UnreliableReason);
    }

    [Fact]
    public void ProfitEstimate_RequiresContractAmount()
    {
        var estimate = ProgressTrackingCalculator.EstimateProfit(0m, 500_000m, 60m);

        Assert.False(estimate.IsReliable);
        Assert.Contains("sözleşme bedeli", estimate.UnreliableReason);
    }

    [Fact]
    public void ProfitEstimate_RequiresActualCost()
    {
        var estimate = ProgressTrackingCalculator.EstimateProfit(1_000_000m, 0m, 60m);

        Assert.False(estimate.IsReliable);
        Assert.Contains("maliyet", estimate.UnreliableReason);
    }

    // ---------- Kâr erozyon alarmı ----------

    /// <summary>
    /// Alarm yalnızca anahtar teslimde ve yalnızca keşif ÜSTÜ net
    /// sapmada çalar. Birim fiyatlıda keşif üstü iş gelir getirir,
    /// alarm anlamsızdır.
    /// </summary>
    [Fact]
    public void ErosionAlarm_OnlyForLumpSum()
    {
        Assert.True(ProgressTrackingCalculator.ShouldRaiseErosionAlarm(
            ProjectContractType.LumpSum,
            netDeviationAmount: 600_000m,
            contractAmount: 10_000_000m,
            thresholdRate: 5m));

        Assert.False(ProgressTrackingCalculator.ShouldRaiseErosionAlarm(
            ProjectContractType.UnitPrice, 600_000m, 10_000_000m, 5m));
    }

    /// <summary>Tasarruf alarm üretmez.</summary>
    [Fact]
    public void ErosionAlarm_DoesNotFireOnSaving()
    {
        Assert.False(ProgressTrackingCalculator.ShouldRaiseErosionAlarm(
            ProjectContractType.LumpSum,
            netDeviationAmount: -800_000m,
            contractAmount: 10_000_000m,
            thresholdRate: 5m));
    }

    /// <summary>Eşiğin tam üstü çalar, altı çalmaz.</summary>
    [Theory]
    [InlineData(500_000, false)]
    [InlineData(500_001, true)]
    public void ErosionAlarm_RespectsThreshold(decimal deviation, bool expected)
    {
        Assert.Equal(expected, ProgressTrackingCalculator.ShouldRaiseErosionAlarm(
            ProjectContractType.LumpSum, deviation, 10_000_000m, 5m));
    }

    // ---------- Stok sarfı ----------

    /// <summary>
    /// Stok sarfı bilgi amaçlıdır; gerçekleşen miktarı DEĞİŞTİRMEZ.
    /// Hakedişe girmemiş sarf ayrı kolonda görünür.
    /// </summary>
    [Fact]
    public void IssuedStock_IsInformationalAndDoesNotAffectRealized()
    {
        var result = ProgressTrackingCalculator.CalculateItem(
            Item(100m, 60m, ProjectContractType.UnitPrice, issuedStock: 95m));

        Assert.Equal(60m, result.RealizedQuantity);
        Assert.Equal(95m, result.IssuedStockQuantity);
        Assert.Equal(-40m, result.DeviationQuantity);
    }
}
