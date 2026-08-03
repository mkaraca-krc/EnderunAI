using EnderunAI.Api.Services.HumanResources;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Faz E2: bordro hesap motoru. Beklenen değerlerin tamamı mevzuat
/// hesabıyla elle çıkarıldı; motor bunları birebir üretmek zorunda.
/// Motor veritabanına bağlı olmadığı için bu testler saf birim testi.
/// </summary>
public sealed class PayrollCalculationTests
{
    /// <summary>2026 için seed edilen parametrelerin aynısı.</summary>
    private static PayrollParameters Parameters(
        bool employerDiscount = false,
        bool incomeTaxExemption = true,
        bool stampTaxExemption = true) => new(
            MinimumWageGross: 33_030m,
            SgkBaseFloor: 33_030m,
            SgkBaseCeiling: 247_725m,
            SgkEmployeeRate: 14m,
            UnemploymentEmployeeRate: 1m,
            SgkEmployerRate: 20.75m,
            UnemploymentEmployerRate: 2m,
            SgkEmployerDiscountEnabled: employerDiscount,
            SgkEmployerDiscountPoints: 5m,
            StampTaxPerMille: 7.59m,
            MinimumWageIncomeTaxExemptionEnabled: incomeTaxExemption,
            MinimumWageStampTaxExemptionEnabled: stampTaxExemption,
            TaxBrackets: new List<PayrollTaxBracketInput>
            {
                new(0m, 200_000m, 15m),
                new(200_000m, 420_000m, 20m),
                new(420_000m, 1_000_000m, 27m),
                new(1_000_000m, 5_400_000m, 35m),
                new(5_400_000m, null, 40m)
            });

    /// <summary>
    /// Asgari ücretli: istisnalar sayesinde gelir ve damga vergisi sıfır
    /// çıkar, net tam olarak net asgari ücrete eşitlenir.
    /// 33.030 − %14 SGK (4.624,20) − %1 işsizlik (330,30) = 28.075,50
    /// </summary>
    [Fact]
    public void MinimumWageEarner_PaysOnlySocialSecurity()
    {
        var result = PayrollCalculationService.Calculate(
            Parameters(), new PayrollInput(Month: 1, GrossEarnings: 33_030m));

        Assert.Equal(33_030m, result.SgkBase);
        Assert.Equal(4_624.20m, result.SgkEmployeeAmount);
        Assert.Equal(330.30m, result.UnemploymentEmployeeAmount);
        Assert.Equal(28_075.50m, result.IncomeTaxBase);

        // İstisna hesaplanan verginin tamamını karşılar.
        Assert.Equal(4_211.33m, result.IncomeTaxBeforeExemption);
        Assert.Equal(4_211.33m, result.IncomeTaxExemption);
        Assert.Equal(0m, result.IncomeTaxAmount);

        Assert.Equal(250.70m, result.StampTaxBeforeExemption);
        Assert.Equal(0m, result.StampTaxAmount);

        Assert.Equal(4_954.50m, result.TotalDeductions);
        Assert.Equal(28_075.50m, result.NetPay);
    }

    /// <summary>
    /// SGK tavanını aşan maaşta prim yalnızca tavan üzerinden kesilir,
    /// gelir vergisi ise brütün tamamı üzerinden yürür.
    /// </summary>
    [Fact]
    public void EarningsAboveCeiling_AreCappedForSocialSecurityOnly()
    {
        var result = PayrollCalculationService.Calculate(
            Parameters(), new PayrollInput(Month: 1, GrossEarnings: 300_000m));

        Assert.Equal(247_725m, result.SgkBase);
        Assert.Equal(34_681.50m, result.SgkEmployeeAmount);
        Assert.Equal(2_477.25m, result.UnemploymentEmployeeAmount);

        // 300.000 − 34.681,50 − 2.477,25
        Assert.Equal(262_841.25m, result.IncomeTaxBase);

        // 200.000 × %15 + 62.841,25 × %20
        Assert.Equal(42_568.25m, result.IncomeTaxBeforeExemption);
        Assert.Equal(4_211.33m, result.IncomeTaxExemption);
        Assert.Equal(38_356.92m, result.IncomeTaxAmount);

        Assert.Equal(2_277.00m, result.StampTaxBeforeExemption);
        Assert.Equal(2_026.30m, result.StampTaxAmount);

        Assert.Equal(77_541.97m, result.TotalDeductions);
        Assert.Equal(222_458.03m, result.NetPay);

        // İşveren maliyeti de tavandan hesaplanır.
        Assert.Equal(51_402.94m, result.SgkEmployerAmount);
        Assert.Equal(4_954.50m, result.UnemploymentEmployerAmount);
        Assert.Equal(356_357.44m, result.TotalEmployerCost);
    }

    /// <summary>
    /// Yıl içinde kümülatif matrah üst dilime taşarsa, taşan kısım üst
    /// dilim oranıyla vergilendirilir. 190.000 devir + 51.000 matrah →
    /// 10.000'i %15, 41.000'i %20 dilimine düşer.
    /// </summary>
    [Fact]
    public void CumulativeBase_CrossesIntoHigherBracket()
    {
        var result = PayrollCalculationService.Calculate(
            Parameters(),
            new PayrollInput(
                Month: 5,
                GrossEarnings: 60_000m,
                CumulativeIncomeTaxBaseBefore: 190_000m));

        Assert.Equal(51_000m, result.IncomeTaxBase);
        Assert.Equal(241_000m, result.CumulativeIncomeTaxBaseAfter);

        // Tax(241.000) − Tax(190.000) = 38.200 − 28.500
        Assert.Equal(9_700m, result.IncomeTaxBeforeExemption);
        Assert.Equal(4_211.33m, result.IncomeTaxExemption);
        Assert.Equal(5_488.67m, result.IncomeTaxAmount);

        Assert.Equal(45_306.63m, result.NetPay);
    }

    /// <summary>
    /// Aynı brüt, devir matrahı olmadan çok daha az vergi doğurur —
    /// kümülatif matrahın gerçekten uygulandığının kanıtı.
    /// </summary>
    [Fact]
    public void SameGross_CostsLessTaxWithoutCumulativeCarryOver()
    {
        var withCarryOver = PayrollCalculationService.Calculate(
            Parameters(),
            new PayrollInput(5, 60_000m, CumulativeIncomeTaxBaseBefore: 190_000m));

        var withoutCarryOver = PayrollCalculationService.Calculate(
            Parameters(),
            new PayrollInput(5, 60_000m, CumulativeIncomeTaxBaseBefore: 0m));

        Assert.True(withCarryOver.IncomeTaxAmount > withoutCarryOver.IncomeTaxAmount);

        // Devirsiz: 51.000 × %15 = 7.650, istisna düşülür.
        Assert.Equal(7_650m, withoutCarryOver.IncomeTaxBeforeExemption);
        Assert.Equal(3_438.67m, withoutCarryOver.IncomeTaxAmount);
    }

    /// <summary>
    /// 5 puanlık işveren prim indirimi yalnızca işveren payını düşürür;
    /// işçinin eline geçen tutar değişmez.
    /// </summary>
    [Fact]
    public void EmployerDiscount_ReducesEmployerCostOnly()
    {
        var input = new PayrollInput(Month: 1, GrossEarnings: 33_030m);

        var standard = PayrollCalculationService.Calculate(Parameters(), input);
        var discounted = PayrollCalculationService.Calculate(
            Parameters(employerDiscount: true), input);

        // %20,75 → %15,75
        Assert.Equal(6_853.73m, standard.SgkEmployerAmount);
        Assert.Equal(5_202.23m, discounted.SgkEmployerAmount);

        Assert.Equal(standard.NetPay, discounted.NetPay);
        Assert.True(discounted.TotalEmployerCost < standard.TotalEmployerCost);
    }

    /// <summary>
    /// İstisnalar kapatıldığında asgari ücretliden de gelir ve damga
    /// vergisi kesilir — bayrakların gerçekten etkili olduğunu gösterir.
    /// </summary>
    [Fact]
    public void DisabledExemptions_TaxMinimumWageFully()
    {
        var result = PayrollCalculationService.Calculate(
            Parameters(incomeTaxExemption: false, stampTaxExemption: false),
            new PayrollInput(Month: 1, GrossEarnings: 33_030m));

        Assert.Equal(0m, result.IncomeTaxExemption);
        Assert.Equal(4_211.33m, result.IncomeTaxAmount);
        Assert.Equal(0m, result.StampTaxExemption);
        Assert.Equal(250.70m, result.StampTaxAmount);

        Assert.Equal(9_416.53m, result.TotalDeductions);
        Assert.Equal(23_613.47m, result.NetPay);
    }

    /// <summary>
    /// Avans/icra gibi net üzerinden yapılan kesintiler vergiyi
    /// değiştirmez, yalnızca ele geçeni düşürür.
    /// </summary>
    [Fact]
    public void OtherDeductions_ReduceNetWithoutAffectingTax()
    {
        var withoutAdvance = PayrollCalculationService.Calculate(
            Parameters(), new PayrollInput(1, 50_000m));

        var withAdvance = PayrollCalculationService.Calculate(
            Parameters(), new PayrollInput(1, 50_000m, OtherDeductions: 5_000m));

        Assert.Equal(withoutAdvance.IncomeTaxAmount, withAdvance.IncomeTaxAmount);
        Assert.Equal(withoutAdvance.StampTaxAmount, withAdvance.StampTaxAmount);
        Assert.Equal(withoutAdvance.NetPay - 5_000m, withAdvance.NetPay);

        Assert.Equal(2_163.67m, withAdvance.IncomeTaxAmount);
        Assert.Equal(128.80m, withAdvance.StampTaxAmount);
        Assert.Equal(35_207.53m, withAdvance.NetPay);
    }

    /// <summary>
    /// Eksik gün nedeniyle kazanç SGK tabanının altına düşerse prim
    /// gerçek kazanç üzerinden hesaplanır; tabana yükseltilmez.
    /// </summary>
    [Fact]
    public void PartialMonth_DoesNotInflateBaseToFloor()
    {
        var result = PayrollCalculationService.Calculate(
            Parameters(), new PayrollInput(Month: 1, GrossEarnings: 15_000m));

        Assert.Equal(15_000m, result.SgkBase);
        Assert.Equal(2_100m, result.SgkEmployeeAmount);
        Assert.Equal(150m, result.UnemploymentEmployeeAmount);

        // İstisna hesaplanan vergiyi aştığı için vergi sıfırlanır,
        // negatife düşmez.
        Assert.Equal(1_912.50m, result.IncomeTaxBeforeExemption);
        Assert.Equal(1_912.50m, result.IncomeTaxExemption);
        Assert.Equal(0m, result.IncomeTaxAmount);
        Assert.Equal(0m, result.StampTaxAmount);

        Assert.Equal(12_750m, result.NetPay);
    }

    [Fact]
    public void MissingTaxBrackets_StopCalculationWithClearError()
    {
        var parameters = Parameters() with
        {
            TaxBrackets = new List<PayrollTaxBracketInput>()
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PayrollCalculationService.Calculate(
                parameters, new PayrollInput(1, 33_030m)));

        Assert.Contains("dilim", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void InvalidMonth_IsRejected(int month)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PayrollCalculationService.Calculate(
                Parameters(), new PayrollInput(month, 33_030m)));
    }

    /// <summary>
    /// Her senaryoda brüt = net + toplam kesinti olmalı; bu eşitlik
    /// bozulursa bordro kendi içinde tutarsız demektir.
    /// </summary>
    [Theory]
    [InlineData(1, 33_030)]
    [InlineData(3, 50_000)]
    [InlineData(7, 120_000)]
    [InlineData(12, 300_000)]
    public void GrossAlwaysEqualsNetPlusDeductions(int month, decimal gross)
    {
        var result = PayrollCalculationService.Calculate(
            Parameters(), new PayrollInput(month, gross));

        Assert.Equal(result.GrossEarnings, result.NetPay + result.TotalDeductions);
    }
}
