using EnderunAI.Api.Services.Hakedis;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Hakediş hesap motoru. Beklenen değerler NATURA hakediş mantığıyla
/// elle çıkarıldı; motor veritabanına bağlı olmadığı için saf birim
/// testi.
/// </summary>
public sealed class HakedisCalculationTests
{
    private static HakedisItemInput Item(
        string code,
        decimal contract,
        decimal previous,
        decimal current,
        decimal material,
        decimal labor = 0m,
        decimal overhead = 0m,
        Guid? sectionId = null) =>
        new(code, contract, previous, current, material, labor, overhead, sectionId);

    // ---------- Pursantaj ----------

    /// <summary>
    /// Önceki miktar + bu dönem miktar = genel toplam; tutarlar birim
    /// fiyatla çarpılır. Pursantajın çekirdeği.
    /// </summary>
    [Fact]
    public void Pursantaj_AddsPreviousAndCurrentQuantities()
    {
        var result = HakedisCalculationService.CalculateItems(
        [
            Item("1.01", contract: 200m, previous: 120m, current: 45m,
                material: 450m, labor: 180m, overhead: 63m)
        ]);

        var item = result.Items.Single();

        Assert.Equal(165m, item.CumulativeQuantity);
        Assert.Equal(693m, item.UnitPrice);

        // Bu dönem: 45 × 693
        Assert.Equal(31_185.00m, item.CurrentAmount);
        // Önceki: 120 × 693
        Assert.Equal(83_160.00m, item.PreviousAmount);
        // Genel toplam: 165 × 693
        Assert.Equal(114_345.00m, item.CumulativeAmount);

        // Pursantaj: 165 / 200
        Assert.Equal(82.50m, item.CompletionRate);
    }

    /// <summary>
    /// Üç dönem üst üste: her dönemin önceki miktarı bir öncekinin genel
    /// toplamıdır ve kümülatif tutar hiç kopmaz.
    /// </summary>
    [Fact]
    public void Pursantaj_ChainsAcrossThreePeriods()
    {
        var first = HakedisCalculationService.CalculateItems(
            [Item("1.01", 300m, previous: 0m, current: 100m, material: 500m)]);

        var second = HakedisCalculationService.CalculateItems(
            [Item("1.01", 300m, previous: 100m, current: 80m, material: 500m)]);

        var third = HakedisCalculationService.CalculateItems(
            [Item("1.01", 300m, previous: 180m, current: 60m, material: 500m)]);

        Assert.Equal(100m, first.Items[0].CumulativeQuantity);
        Assert.Equal(180m, second.Items[0].CumulativeQuantity);
        Assert.Equal(240m, third.Items[0].CumulativeQuantity);

        // Her dönemin bu-dönem tutarları toplamı, son kümülatife eşit.
        Assert.Equal(
            third.Items[0].CumulativeAmount,
            first.Items[0].CurrentAmount +
            second.Items[0].CurrentAmount +
            third.Items[0].CurrentAmount);
    }

    /// <summary>
    /// Sözleşme miktarının aşılması hata değil (ilave iş olabilir) ama
    /// işaretlenmeli.
    /// </summary>
    [Fact]
    public void ExceedingContractQuantity_IsFlaggedNotRejected()
    {
        var result = HakedisCalculationService.CalculateItems(
            [Item("1.01", contract: 100m, previous: 90m, current: 20m, material: 10m)]);

        Assert.True(result.Items[0].ExceedsContractQuantity);
        Assert.Equal(110m, result.Items[0].CumulativeQuantity);
    }

    [Fact]
    public void ContractQuantityZero_DoesNotFlagOrDivideByZero()
    {
        var result = HakedisCalculationService.CalculateItems(
            [Item("EK.01", contract: 0m, previous: 0m, current: 5m, material: 100m)]);

        Assert.False(result.Items[0].ExceedsContractQuantity);
        Assert.Equal(0m, result.Items[0].CompletionRate);
    }

    // ---------- Bileşenler ve bölüm icmali ----------

    /// <summary>
    /// Malzeme + montaj + GG&amp;K birim fiyatları toplanarak birim fiyatı
    /// verir; tutarlar bileşen bazında ayrı ayrı da çıkar.
    /// </summary>
    [Fact]
    public void UnitPriceComponents_SumIntoUnitPriceAndSplitAmounts()
    {
        var result = HakedisCalculationService.CalculateItems(
        [
            Item("1.01", 200m, 0m, current: 10m,
                material: 450m, labor: 180m, overhead: 63m)
        ]);

        var item = result.Items.Single();

        Assert.Equal(693m, item.UnitPrice);
        Assert.Equal(4_500.00m, item.MaterialAmount);
        Assert.Equal(1_800.00m, item.LaborAmount);
        Assert.Equal(630.00m, item.OverheadAmount);

        // Bileşenlerin toplamı bu dönem tutarına eşit olmalı.
        Assert.Equal(
            item.CurrentAmount,
            item.MaterialAmount + item.LaborAmount + item.OverheadAmount);
    }

    /// <summary>Bölüm icmali pozları bölümlerine göre toplar.</summary>
    [Fact]
    public void Sections_AggregateTheirOwnItems()
    {
        var panolar = Guid.NewGuid();
        var topraklama = Guid.NewGuid();

        var result = HakedisCalculationService.CalculateItems(
        [
            Item("1.01", 100m, 0m, 10m, material: 100m, sectionId: panolar),
            Item("1.02", 100m, 0m, 5m, material: 200m, sectionId: panolar),
            Item("8.01", 100m, 0m, 20m, material: 50m, sectionId: topraklama)
        ]);

        Assert.Equal(2, result.Sections.Count);

        var panoSection = result.Sections.Single(x => x.SectionId == panolar);
        Assert.Equal(2_000.00m, panoSection.CurrentAmount);

        var topraklamaSection = result.Sections.Single(x => x.SectionId == topraklama);
        Assert.Equal(1_000.00m, topraklamaSection.CurrentAmount);

        // Bölümlerin toplamı genel toplama eşit.
        Assert.Equal(
            result.CurrentTotal,
            result.Sections.Sum(x => x.CurrentAmount));
    }

    // ---------- Üst hesap ----------

    private static HakedisCalculationService.HakedisHeaderInput Header(
        decimal cumulativeWork,
        decimal cumulativeAdvance = 0m,
        decimal previousTotal = 0m,
        decimal priceDifference = 0m,
        decimal vatRate = 20m,
        int numerator = 4,
        int denominator = 10,
        decimal incomeTaxRate = 0m,
        decimal deductions = 0m) =>
        new(cumulativeWork, cumulativeAdvance, previousTotal, priceDifference,
            vatRate, numerator, denominator, incomeTaxRate, deductions);

    /// <summary>
    /// NATURA üst hesabı: 1.000.000 kümülatif imalat, 200.000 açık
    /// ihzarat, 700.000 önceki hakediş.
    ///   kümülatif toplam 1.200.000 − önceki 700.000 = 500.000
    ///   KDV %20 = 100.000
    ///   tevkifat 4/10 = 40.000 (alıcı beyan eder)
    ///   brüt 600.000 − tevkifat 40.000 − kesinti 75.000 = 485.000
    /// </summary>
    [Fact]
    public void Header_FollowsNaturaOrder()
    {
        var result = HakedisCalculationService.CalculateHeader(
            Header(
                cumulativeWork: 1_000_000m,
                cumulativeAdvance: 200_000m,
                previousTotal: 700_000m,
                deductions: 75_000m));

        Assert.Equal(1_200_000.00m, result.CumulativeTotalAmount);
        Assert.Equal(500_000.00m, result.CurrentAmount);
        Assert.Equal(500_000.00m, result.TaxableAmount);
        Assert.Equal(100_000.00m, result.VatAmount);
        Assert.Equal(40_000.00m, result.WithholdingAmount);
        Assert.Equal(60_000.00m, result.DeclaredVatAmount);
        Assert.Equal(600_000.00m, result.GrossPayableAmount);
        Assert.Equal(485_000.00m, result.NetPayableAmount);
    }

    /// <summary>Tevkifat KDV'nin oranıdır, hakediş tutarının değil.</summary>
    [Fact]
    public void Withholding_IsAFractionOfVatNotOfWorkAmount()
    {
        var result = HakedisCalculationService.CalculateHeader(
            Header(cumulativeWork: 100_000m));

        Assert.Equal(20_000.00m, result.VatAmount);
        Assert.Equal(8_000.00m, result.WithholdingAmount);
        Assert.Equal(12_000.00m, result.DeclaredVatAmount);
    }

    [Fact]
    public void WithholdingDenominatorZero_MeansNoWithholding()
    {
        var result = HakedisCalculationService.CalculateHeader(
            Header(cumulativeWork: 100_000m, numerator: 0, denominator: 0));

        Assert.Equal(0m, result.WithholdingAmount);
        Assert.Equal(20_000.00m, result.DeclaredVatAmount);
    }

    /// <summary>
    /// Stopaj opsiyonel ve matrahı KDV hariç tutardır.
    /// 500.000 × %5 = 25.000
    /// </summary>
    [Fact]
    public void IncomeTaxWithholding_IsOptionalAndBasedOnNetOfVat()
    {
        var without = HakedisCalculationService.CalculateHeader(
            Header(cumulativeWork: 500_000m));

        var with = HakedisCalculationService.CalculateHeader(
            Header(cumulativeWork: 500_000m, incomeTaxRate: 5m));

        Assert.Equal(0m, without.IncomeTaxWithholdingAmount);
        Assert.Equal(25_000.00m, with.IncomeTaxWithholdingAmount);
        Assert.Equal(without.NetPayableAmount - 25_000m, with.NetPayableAmount);
    }

    /// <summary>
    /// İhzarat imalata dönüştüğünde toplam değişmemeli: açık ihzarat
    /// azalırken imalat aynı tutarda artar. Çift tahsilat hesabın kendi
    /// yapısıyla engellenir.
    /// </summary>
    [Fact]
    public void AdvanceMaterial_ConvertingToWork_DoesNotChangeTotal()
    {
        var before = HakedisCalculationService.CalculateHeader(
            Header(cumulativeWork: 800_000m, cumulativeAdvance: 200_000m));

        // 150.000'lik ihzarat imalata döndü.
        var after = HakedisCalculationService.CalculateHeader(
            Header(cumulativeWork: 950_000m, cumulativeAdvance: 50_000m));

        Assert.Equal(before.CumulativeTotalAmount, after.CumulativeTotalAmount);
    }

    /// <summary>
    /// Minha: bu dönem tutarı, kümülatif toplamdan önceki hakedişlerin
    /// düşülmesiyle bulunur. Birim fiyat dönemler arasında arttığında
    /// aradaki fark bu dönemde tahsil edilir — satır toplamı yaklaşımı
    /// bunu kaçırırdı.
    /// </summary>
    [Fact]
    public void Minha_CapturesUnitPriceIncreaseFromEarlierPeriods()
    {
        // İlk dönemde 100 birim 500 TL'den ödendi = 50.000.
        // İkinci dönemde birim fiyat 550'ye çıktı, 20 birim daha yapıldı.
        // Kümülatif: 120 × 550 = 66.000; önceki ödenen 50.000.
        var result = HakedisCalculationService.CalculateHeader(
            Header(cumulativeWork: 66_000m, previousTotal: 50_000m));

        // Bu dönem yalnızca 20 × 550 = 11.000 değil, fiyat farkı dahil
        // 16.000 olmalı.
        Assert.Equal(16_000.00m, result.CurrentAmount);
    }

    /// <summary>
    /// Bütünlük: brüt = matrah + KDV ve net = brüt − tevkifat − stopaj −
    /// kesinti. Eşitlik bozulursa hakediş kendi içinde tutarsızdır.
    /// </summary>
    [Theory]
    [InlineData(500_000, 0, 0, 0)]
    [InlineData(1_250_000, 300_000, 900_000, 125_000)]
    [InlineData(90_000, 10_000, 0, 7_500)]
    public void GrossAndNet_AreInternallyConsistent(
        decimal cumulativeWork,
        decimal cumulativeAdvance,
        decimal previousTotal,
        decimal deductions)
    {
        var result = HakedisCalculationService.CalculateHeader(
            Header(cumulativeWork, cumulativeAdvance, previousTotal,
                incomeTaxRate: 5m, deductions: deductions));

        Assert.Equal(
            result.GrossPayableAmount,
            result.TaxableAmount + result.VatAmount);

        Assert.Equal(
            result.NetPayableAmount,
            result.GrossPayableAmount
            - result.WithholdingAmount
            - result.IncomeTaxWithholdingAmount
            - result.TotalDeductionAmount);

        Assert.Equal(
            result.VatAmount,
            result.WithholdingAmount + result.DeclaredVatAmount);
    }
}
