using EnderunAI.Api.Services.HumanResources;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Brütleştirme (netten brüte) motoru.
///
/// Asıl güvence tur testi: girilen net → brüt → bordro → çıkan net,
/// girilen netin kendisi olmalı. Bu sağlanmazsa "eline şu kadar
/// geçecek" sözü tutulmuyor demektir.
///
/// Motor veritabanına bağlı olmadığı için saf birim testi.
/// </summary>
public sealed class PayrollNetToGrossTests
{
    /// <summary>2026 resmi parametreleri — bordro testleriyle aynı.</summary>
    private static PayrollParameters Parameters(
        bool incomeTaxExemption = true,
        bool stampTaxExemption = true) => new(
            MinimumWageGross: 33_030m,
            SgkBaseFloor: 33_030m,
            SgkBaseCeiling: 297_270m,
            SgkEmployeeRate: 14m,
            UnemploymentEmployeeRate: 1m,
            SgkEmployerRate: 20.75m,
            UnemploymentEmployerRate: 2m,
            SgkEmployerDiscountEnabled: false,
            SgkEmployerDiscountPoints: 2m,
            StampTaxPerMille: 7.59m,
            MinimumWageIncomeTaxExemptionEnabled: incomeTaxExemption,
            MinimumWageStampTaxExemptionEnabled: stampTaxExemption,
            TaxBrackets: new List<PayrollTaxBracketInput>
            {
                new(0m, 190_000m, 15m),
                new(190_000m, 400_000m, 20m),
                new(400_000m, 1_500_000m, 27m),
                new(1_500_000m, 5_300_000m, 35m),
                new(5_300_000m, null, 40m)
            });

    /// <summary>
    /// Tur doğrulaması: bulunan brütü bordro motoruna verince girilen
    /// net çıkmalı. Tüm senaryolarda kullanılan ortak kontrol.
    /// </summary>
    private static void AssertRoundTrip(
        decimal targetNet, int month = 1, decimal cumulativeBefore = 0m)
    {
        var parameters = Parameters();

        var result = PayrollNetToGrossCalculator.CalculateGrossFromNet(
            parameters, targetNet, month, cumulativeBefore);

        var payroll = PayrollCalculationService.Calculate(
            parameters,
            new PayrollInput(
                Month: month,
                GrossEarnings: result.GrossEarnings,
                CumulativeIncomeTaxBaseBefore: cumulativeBefore));

        Assert.Equal(targetNet, payroll.NetPay);
        Assert.Equal(targetNet, result.AchievedNet);
        Assert.True(result.IsExact,
            $"Sapma {result.Difference:N2} TL — hedef {targetNet:N2}, " +
            $"bulunan brüt {result.GrossEarnings:N2}");
    }

    [Theory]
    [InlineData(28_075.50)]  // net asgari ücret
    [InlineData(33_058.43)]  // mevcut referans bordronun neti (brüt 40.000)
    [InlineData(40_000)]
    [InlineData(45_000)]
    [InlineData(75_000)]
    [InlineData(150_000)]
    [InlineData(250_000)]    // SGK tavanı üstü
    public void RoundTrip_ProducesExactlyTheEnteredNet(decimal targetNet)
    {
        AssertRoundTrip(targetNet);
    }

    [Fact]
    public void ReferencePayroll_IsInvertedCorrectly()
    {
        // Mevcut bordro testinde brüt 40.000 → net 33.058,43.
        // Aynı neti verince ~40.000 çıkmalı.
        var result = PayrollNetToGrossCalculator.CalculateGrossFromNet(
            Parameters(), targetNet: 33_058.43m, month: 1);

        Assert.True(result.IsExact);
        // Yuvarlama nedeniyle birkaç kuruş sapabilir; 1 TL bandında olmalı.
        Assert.InRange(result.GrossEarnings, 39_999m, 40_001m);
    }

    [Fact]
    public void MinimumWageNet_ResolvesToMinimumWageGross()
    {
        var result = PayrollNetToGrossCalculator.CalculateGrossFromNet(
            Parameters(), targetNet: 28_075.50m, month: 1);

        Assert.True(result.IsExact);
        Assert.Equal(33_030m, result.GrossEarnings);
    }

    [Fact]
    public void SameNet_NeedsHigherGrossLaterInTheYear()
    {
        // Net sabit kararının kanıtı: yıl ilerledikçe kümülatif matrah
        // üst dilime taşındığı için aynı net daha yüksek brüt ister.
        var parameters = Parameters();
        const decimal targetNet = 60_000m;

        var january = PayrollNetToGrossCalculator.CalculateGrossFromNet(
            parameters, targetNet, month: 1, cumulativeIncomeTaxBaseBefore: 0m);

        var november = PayrollNetToGrossCalculator.CalculateGrossFromNet(
            parameters, targetNet, month: 11, cumulativeIncomeTaxBaseBefore: 700_000m);

        Assert.True(november.GrossEarnings > january.GrossEarnings);

        // İkisinde de net tam tutmalı — asıl vaat bu.
        Assert.True(january.IsExact);
        Assert.True(november.IsExact);
    }

    [Fact]
    public void RoundTrip_AcrossBracketBoundary_StillExact()
    {
        // Kümülatif matrah dilim sınırının hemen altındayken bu ayın
        // vergisi iki dilime bölünür; brütleştirme bunu da yakalamalı.
        AssertRoundTrip(targetNet: 60_000m, month: 6, cumulativeBefore: 185_000m);
        AssertRoundTrip(targetNet: 60_000m, month: 9, cumulativeBefore: 395_000m);
    }

    [Fact]
    public void RoundTrip_AboveSgkCeiling_StillExact()
    {
        // Tavan üstünde SGK primi sabitlenir; net-brüt ilişkisi kırılır
        // ama monoton kalır.
        AssertRoundTrip(targetNet: 400_000m, month: 1);
    }

    [Fact]
    public void RoundTrip_WithExemptionsDisabled_StillExact()
    {
        var parameters = Parameters(incomeTaxExemption: false, stampTaxExemption: false);

        var result = PayrollNetToGrossCalculator.CalculateGrossFromNet(
            parameters, targetNet: 45_000m, month: 1);

        var payroll = PayrollCalculationService.Calculate(
            parameters, new PayrollInput(1, result.GrossEarnings));

        Assert.Equal(45_000m, payroll.NetPay);
    }

    [Fact]
    public void OtherDeductions_AreCoveredByHigherGross()
    {
        // Avans/icra kesintisi varsa, aynı neti verebilmek için brüt
        // daha yüksek olmalı.
        var parameters = Parameters();

        var without = PayrollNetToGrossCalculator.CalculateGrossFromNet(
            parameters, targetNet: 45_000m, month: 1);

        var with = PayrollNetToGrossCalculator.CalculateGrossFromNet(
            parameters, targetNet: 45_000m, month: 1, otherDeductions: 5_000m);

        Assert.True(with.GrossEarnings > without.GrossEarnings);

        var payroll = PayrollCalculationService.Calculate(
            parameters,
            new PayrollInput(1, with.GrossEarnings, OtherDeductions: 5_000m));

        Assert.Equal(45_000m, payroll.NetPay);
    }

    [Fact]
    public void ZeroNet_ReturnsZeroGross()
    {
        var result = PayrollNetToGrossCalculator.CalculateGrossFromNet(
            Parameters(), targetNet: 0m, month: 1);

        Assert.Equal(0m, result.GrossEarnings);
        Assert.True(result.IsExact);
    }

    [Fact]
    public void NegativeNet_IsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            PayrollNetToGrossCalculator.CalculateGrossFromNet(
                Parameters(), targetNet: -1m, month: 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void InvalidMonth_IsRejected(int month)
    {
        Assert.Throws<ArgumentException>(() =>
            PayrollNetToGrossCalculator.CalculateGrossFromNet(
                Parameters(), targetNet: 45_000m, month: month));
    }

    [Fact]
    public void Result_CarriesFullPayrollBreakdown()
    {
        // Ekranda kesinti kırılımı gösterilebilsin diye bordro birlikte
        // dönüyor; ayrıca bir kez daha hesaplamaya gerek kalmıyor.
        var result = PayrollNetToGrossCalculator.CalculateGrossFromNet(
            Parameters(), targetNet: 45_000m, month: 1);

        Assert.Equal(result.GrossEarnings, result.Payroll.GrossEarnings);
        Assert.Equal(result.AchievedNet, result.Payroll.NetPay);
        Assert.Equal(
            result.Payroll.GrossEarnings,
            result.Payroll.NetPay + result.Payroll.TotalDeductions);
    }

    [Fact]
    public void Search_ConvergesQuickly()
    {
        // Yakınsama makul adımda bitmeli; sınıra dayanmak mantık
        // hatasına işaret eder.
        var result = PayrollNetToGrossCalculator.CalculateGrossFromNet(
            Parameters(), targetNet: 45_000m, month: 1);

        Assert.InRange(result.Iterations, 1, 60);
    }
}
