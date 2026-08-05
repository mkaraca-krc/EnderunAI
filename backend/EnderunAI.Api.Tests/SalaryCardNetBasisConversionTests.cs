using EnderunAI.Api.Services.HumanResources;
using Xunit;
using Xunit.Abstractions;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Ücret kartlarının net esasına çevrilmesi.
///
/// Kartlardaki brüt değerler gerçek bir brütleştirmeyle değil,
/// <c>20260803130230_FixMigratedSalaryCardsGrossNet</c> göçünde
/// "Brüt = Net / 0,85" kestirmesiyle üretilmişti. O göç kendi yorumunda
/// da bunun asgari ücret üstünde yaklaşık olduğunu söylüyor.
///
/// Bu testler dönüşümü ÜRETİM parametreleriyle sabitler: göçe yazılan
/// yeni brüt değerleri buradan çıkar. Parametre değişirse test kırılır
/// ve göçteki sabit değerlerin de gözden geçirilmesi gerektiği anlaşılır.
/// </summary>
public sealed class SalaryCardNetBasisConversionTests(ITestOutputHelper output)
{
    /// <summary>2026 yılı canlı bordro parametreleri.</summary>
    private static PayrollParameters Parameters2026() => new(
        MinimumWageGross: 33_030.00m,
        SgkBaseFloor: 33_030.00m,
        SgkBaseCeiling: 297_270.00m,
        SgkEmployeeRate: 14.00m,
        UnemploymentEmployeeRate: 1.00m,
        SgkEmployerRate: 20.75m,
        UnemploymentEmployerRate: 2.00m,
        SgkEmployerDiscountEnabled: true,
        SgkEmployerDiscountPoints: 2.00m,
        StampTaxPerMille: 7.59m,
        MinimumWageIncomeTaxExemptionEnabled: true,
        MinimumWageStampTaxExemptionEnabled: true,
        TaxBrackets:
        [
            new(0m, 190_000m, 15m),
            new(190_000m, 400_000m, 20m),
            new(400_000m, 1_500_000m, 27m),
            new(1_500_000m, 5_300_000m, 35m),
            new(5_300_000m, null, 40m)
        ]);

    /// <summary>
    /// Kartlardaki mevcut (net, eski brüt) çiftleri. Eski brüt her
    /// satırda net ÷ 0,85 olarak üretilmişti.
    /// </summary>
    public static TheoryData<decimal, decimal, int> ProductionSalaryLevels() => new()
    {
        // net,        eski brüt,    kart sayısı
        { 28_075.50m,  33_030.00m,  75 },
        { 35_000.00m,  41_176.47m,   1 },
        { 60_000.00m,  70_588.24m,   1 },
        { 75_000.00m,  88_235.29m,   1 },
        { 90_000.00m, 105_882.35m,   1 }
    };

    [Theory]
    [MemberData(nameof(ProductionSalaryLevels))]
    public void Conversion_ProducesGrossThatYieldsTargetNetExactly(
        decimal net, decimal oldGross, int cardCount)
    {
        var parameters = Parameters2026();

        // Kart üzerindeki referans brüt ocak esasıyla hesaplanır —
        // HrMasterDataController.ApplyNetBasisAsync ile aynı çağrı.
        var result = PayrollNetToGrossCalculator.CalculateGrossFromNet(
            parameters, net, month: 1);

        output.WriteLine(
            $"{cardCount,3} kart | net {net,12:N2} | eski brüt {oldGross,12:N2} " +
            $"| yeni brüt {result.GrossEarnings,12:N2} " +
            $"| fark {result.GrossEarnings - oldGross,12:N2}");

        Assert.True(result.IsExact,
            $"Net {net:N2} için kuruşu kuruşuna brüt bulunamadı; " +
            $"sapma {result.Difference:N4}.");

        Assert.Equal(net, result.AchievedNet);
    }

    /// <summary>
    /// Asgari ücretli kartlarda eski brüt zaten doğruydu: ÷0,85
    /// kestirmesi tam olarak asgari ücretin brüt/net oranıdır. 75 kartın
    /// değişmemesi beklenir — dönüşümün gerçekten yalnızca yanlış olanı
    /// düzelttiğinin kanıtı.
    /// </summary>
    [Fact]
    public void Conversion_LeavesMinimumWageCardsUnchanged()
    {
        var result = PayrollNetToGrossCalculator.CalculateGrossFromNet(
            Parameters2026(), 28_075.50m, month: 1);

        Assert.Equal(33_030.00m, result.GrossEarnings);
    }

    /// <summary>
    /// Asgari ücret üstü kartlarda ÷0,85 kestirmesi brütü OLDUĞUNDAN
    /// DÜŞÜK göstermişti.
    ///
    /// Kestirme, toplam kesintiyi her ücret için sabit %15 varsayıyor.
    /// Bu yalnızca asgari ücrette doğru: orada asgari ücret istisnası
    /// gelir ve damga vergisini sıfırladığı için geriye sadece SGK %14 +
    /// işsizlik %1 kalıyor. Asgari ücret üstünde istisna sabit kalır,
    /// aşan kısım vergilenir ve toplam kesinti oranı %15'i geçer — aynı
    /// nete ulaşmak için daha yüksek brüt gerekir. Yani kartlar bugüne
    /// kadar işveren maliyetini olduğundan düşük göstermiş.
    /// </summary>
    [Theory]
    [InlineData(35_000.00, 41_176.47, 42_715.82)]
    [InlineData(60_000.00, 70_588.24, 77_685.26)]
    [InlineData(75_000.00, 88_235.29, 98_666.92)]
    [InlineData(90_000.00, 105_882.35, 119_648.58)]
    public void Conversion_RaisesUnderstatedGrossAboveMinimumWage(
        double netValue, double oldGrossValue, double expectedNewGrossValue)
    {
        var net = (decimal)netValue;
        var oldGross = (decimal)oldGrossValue;
        var expected = (decimal)expectedNewGrossValue;

        var result = PayrollNetToGrossCalculator.CalculateGrossFromNet(
            Parameters2026(), net, month: 1);

        Assert.True(result.GrossEarnings > oldGross,
            $"Net {net:N2}: yeni brüt {result.GrossEarnings:N2} " +
            $"eski brütten ({oldGross:N2}) büyük olmalıydı.");

        // Göçe yazılan sabit değerlerle birebir aynı olmalı.
        Assert.Equal(expected, result.GrossEarnings);
    }

    /// <summary>
    /// Net sabit kararının kanıtı: aynı net, kümülatif matrah büyüdükçe
    /// daha yüksek brüt gerektirir ama net her ay birebir tutar. Kartta
    /// saklanan brüt yalnızca ocak esaslı referanstır.
    /// </summary>
    [Fact]
    public void SameNetRequiresHigherGrossLaterInYear()
    {
        var parameters = Parameters2026();

        var january = PayrollNetToGrossCalculator.CalculateGrossFromNet(
            parameters, 60_000m, month: 1);

        // Kasıma kadar birikmiş matrah ilk dilimi aşırır.
        var november = PayrollNetToGrossCalculator.CalculateGrossFromNet(
            parameters, 60_000m, month: 11,
            cumulativeIncomeTaxBaseBefore: 600_000m);

        Assert.Equal(60_000m, january.AchievedNet);
        Assert.Equal(60_000m, november.AchievedNet);
        Assert.True(november.GrossEarnings > january.GrossEarnings);
    }
}
