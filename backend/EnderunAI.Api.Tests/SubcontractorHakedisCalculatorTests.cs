using EnderunAI.Api.Services.Subcontractors;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Taşeron hakedişinin hesabı. Static ve veritabanısız.
///
/// Asıl güvence KÜMÜLATİF (MİNHA) mantığın korunması: dönem tutarı
/// doğrudan girilmez, kümülatiften öncekinin düşülmesiyle bulunur.
/// Bu bozulursa geçmiş bir satırın düzeltilmesi toplamı sessizce
/// kaydırır.
/// </summary>
public sealed class SubcontractorHakedisCalculatorTests
{
    // ---------- Birim fiyatlı ----------

    [Fact]
    public void Item_PaysOnlyTheDifferenceFromPreviousCumulative()
    {
        var result = SubcontractorHakedisCalculator.CalculateItem(
            new SubcontractorItemInput(
                ContractQuantity: 1_000m,
                PreviousQuantity: 400m,
                AgreedQuantity: 650m,
                UnitPrice: 120m));

        Assert.Equal(250m, result.CurrentQuantity);
        Assert.Equal(30_000m, result.CurrentAmount);
        Assert.Equal(48_000m, result.PreviousAmount);
        Assert.Equal(78_000m, result.CumulativeAmount);
    }

    /// <summary>
    /// Kümülatif miktar öncekinin altına düşerse (geçmiş düzeltmesi) bu
    /// dönemde EKSİ iş yazılmaz: eksi hakediş, taşerondan para geri
    /// istemek demektir ve mutabakat konusudur.
    /// </summary>
    [Fact]
    public void Item_NeverProducesNegativePeriod()
    {
        var result = SubcontractorHakedisCalculator.CalculateItem(
            new SubcontractorItemInput(
                ContractQuantity: 1_000m,
                PreviousQuantity: 700m,
                AgreedQuantity: 500m,
                UnitPrice: 120m));

        Assert.Equal(0m, result.CurrentQuantity);
        Assert.Equal(0m, result.CurrentAmount);
    }

    /// <summary>
    /// Sözleşme miktarının aşılması hata değil: ilave iştir ve kâr
    /// analizinde sapma olarak görünür. Hesap engellenmemeli.
    /// </summary>
    [Fact]
    public void Item_AllowsQuantityAboveContract()
    {
        var result = SubcontractorHakedisCalculator.CalculateItem(
            new SubcontractorItemInput(
                ContractQuantity: 100m,
                PreviousQuantity: 100m,
                AgreedQuantity: 130m,
                UnitPrice: 50m));

        Assert.Equal(30m, result.CurrentQuantity);
        Assert.Equal(1_500m, result.CurrentAmount);
        Assert.Equal(6_500m, result.CumulativeAmount);
    }

    /// <summary>
    /// Üç dönemlik zincir: her dönemin tutarı toplandığında kümülatifi
    /// vermeli. Kümülatif mantığın asıl kanıtı budur.
    /// </summary>
    [Fact]
    public void Item_PeriodAmountsSumToCumulativeAcrossPeriods()
    {
        const decimal unitPrice = 137.45m;

        var first = SubcontractorHakedisCalculator.CalculateItem(
            new SubcontractorItemInput(500m, 0m, 120m, unitPrice));
        var second = SubcontractorHakedisCalculator.CalculateItem(
            new SubcontractorItemInput(500m, 120m, 310m, unitPrice));
        var third = SubcontractorHakedisCalculator.CalculateItem(
            new SubcontractorItemInput(500m, 310m, 480m, unitPrice));

        var periodTotal =
            first.CurrentAmount + second.CurrentAmount + third.CurrentAmount;

        Assert.Equal(third.CumulativeAmount, periodTotal);
    }

    // ---------- Götürü ----------

    [Fact]
    public void Section_PaysWeightedProgressDifference()
    {
        var result = SubcontractorHakedisCalculator.CalculateSection(
            new SubcontractorSectionInput(
                SectionAmount: 300_000m,
                PreviousProgressRate: 40m,
                AgreedProgressRate: 65m));

        Assert.Equal(120_000m, result.PreviousAmount);
        Assert.Equal(195_000m, result.CumulativeAmount);
        Assert.Equal(75_000m, result.CurrentAmount);
    }

    /// <summary>
    /// Götürüde bedel sabittir: %100'ün üstü sözleşme dışı iştir ve
    /// ayrı yürümelidir. Motor yüzdeyi kırpıyor.
    /// </summary>
    [Fact]
    public void Section_ClampsProgressAtHundredPercent()
    {
        var result = SubcontractorHakedisCalculator.CalculateSection(
            new SubcontractorSectionInput(200_000m, 90m, 140m));

        Assert.Equal(100m, result.AgreedProgressRate);
        Assert.Equal(200_000m, result.CumulativeAmount);
        Assert.Equal(20_000m, result.CurrentAmount);
    }

    [Fact]
    public void Section_NeverProducesNegativePeriod()
    {
        var result = SubcontractorHakedisCalculator.CalculateSection(
            new SubcontractorSectionInput(200_000m, 70m, 50m));

        Assert.Equal(0m, result.CurrentAmount);
    }

    // ---------- Ödeme ----------

    [Fact]
    public void Payment_SubtractsDeductionsFromPeriodAmount()
    {
        var (gross, net, uncollected) =
            SubcontractorHakedisCalculator.CalculatePayment(100_000m, 23_500m);

        Assert.Equal(100_000m, gross);
        Assert.Equal(76_500m, net);
        Assert.Equal(0m, uncollected);
    }

    /// <summary>
    /// Kesinti dönem tutarını aşarsa net sıfıra çekilir ve aşan kısım
    /// TAHSİL EDİLMEZ. Eksi ödeme, taşerondan para istemek demektir;
    /// otomatik yapılamaz, mutabakat konusudur.
    /// </summary>
    [Fact]
    public void Payment_ClampsNetAtZeroAndReportsUncollected()
    {
        var (gross, net, uncollected) =
            SubcontractorHakedisCalculator.CalculatePayment(10_000m, 14_250m);

        Assert.Equal(10_000m, gross);
        Assert.Equal(0m, net);
        Assert.Equal(4_250m, uncollected);
    }
}
