using EnderunAI.Api.Services.Accounting;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Kur farkı hesabının işaret kuralı (D3).
///
/// Bu paket veritabanına hiç dokunmaz: kur farkında hata yapmanın en
/// kolay yolu işareti ters çevirmektir — kâr yazılacak yere zarar
/// yazmak defteri ve vergi matrahını birlikte bozar. Kural tek
/// cümlede: TL karşılığı artan bir ALACAK kârdır, TL karşılığı artan
/// bir BORÇ zarardır.
/// </summary>
public sealed class ExchangeDifferenceCalculatorTests
{
    [Fact]
    public void CarryingRate_IsBookValueDividedByBalance()
    {
        // 1.000 USD, 30.000 TL defter değeri → taşıma kuru 30
        Assert.Equal(30m, ExchangeDifferenceCalculator.CarryingRate(1_000m, 30_000m));
    }

    /// <summary>
    /// Bakiye sıfırken taşıma kuru tanımsızdır; sıfıra bölmek yerine
    /// null dönmeli ki üstüne fark hesaplanmasın.
    /// </summary>
    [Fact]
    public void CarryingRate_IsNullWhenBalanceIsZero()
    {
        Assert.Null(ExchangeDifferenceCalculator.CarryingRate(0m, 5_000m));
    }

    /// <summary>
    /// Defter değeriyle döviz bakiyesi ters işaretliyse veri tutarsızdır;
    /// negatif taşıma kuru üretip üstüne fark yazmak yanlış rakam olur.
    /// </summary>
    [Fact]
    public void CarryingRate_IsNullWhenSignsDisagree()
    {
        Assert.Null(ExchangeDifferenceCalculator.CarryingRate(1_000m, -30_000m));
    }

    /// <summary>
    /// Alacak (müşteri bize borçlu) + kur yükseldi → alacağımızın TL
    /// karşılığı arttı → KÂR (646).
    /// </summary>
    [Fact]
    public void Receivable_RateUp_IsGain()
    {
        var result = ExchangeDifferenceCalculator.Calculate(
            balance: 1_000m, bookValueLocal: 30_000m, rate: 35m);

        Assert.NotNull(result);
        Assert.True(result!.IsGain);
        Assert.Equal(5_000m, result.Amount);
        Assert.Equal(30m, result.CarryingRate);
        Assert.Equal(35m, result.SettlementRate);
    }

    /// <summary>Alacak + kur düştü → ZARAR (656).</summary>
    [Fact]
    public void Receivable_RateDown_IsLoss()
    {
        var result = ExchangeDifferenceCalculator.Calculate(
            balance: 1_000m, bookValueLocal: 30_000m, rate: 28m);

        Assert.NotNull(result);
        Assert.False(result!.IsGain);
        Assert.Equal(2_000m, result.Amount);
    }

    /// <summary>
    /// Borç (biz tedarikçiye borçluyuz) + kur yükseldi → borcumuzun TL
    /// karşılığı arttı → ZARAR. Bakiye negatif olduğu için aynı formül
    /// işareti kendiliğinden ters çeviriyor.
    /// </summary>
    [Fact]
    public void Payable_RateUp_IsLoss()
    {
        var result = ExchangeDifferenceCalculator.Calculate(
            balance: -1_000m, bookValueLocal: -30_000m, rate: 35m);

        Assert.NotNull(result);
        Assert.False(result!.IsGain);
        Assert.Equal(5_000m, result.Amount);
    }

    /// <summary>Borç + kur düştü → KÂR.</summary>
    [Fact]
    public void Payable_RateDown_IsGain()
    {
        var result = ExchangeDifferenceCalculator.Calculate(
            balance: -1_000m, bookValueLocal: -30_000m, rate: 27m);

        Assert.NotNull(result);
        Assert.True(result!.IsGain);
        Assert.Equal(3_000m, result.Amount);
    }

    [Fact]
    public void NoDifference_ReturnsNull()
    {
        Assert.Null(ExchangeDifferenceCalculator.Calculate(
            balance: 1_000m, bookValueLocal: 30_000m, rate: 30m));
    }

    [Fact]
    public void ZeroOrNegativeRate_ReturnsNull()
    {
        Assert.Null(ExchangeDifferenceCalculator.Calculate(1_000m, 30_000m, 0m));
        Assert.Null(ExchangeDifferenceCalculator.Calculate(1_000m, 30_000m, -5m));
    }

    /// <summary>
    /// Gerçekleşmiş fark: 1.000 dolarlık borcu 30 kurundan taşırken 35
    /// kurundan ödedik → 5.000 TL fazla ödedik → ZARAR.
    /// </summary>
    [Fact]
    public void Realized_PayingDebtAtHigherRate_IsLoss()
    {
        var result = ExchangeDifferenceCalculator.CalculateRealized(
            settledAmount: 1_000m,
            carryingRate: 30m,
            settlementRate: 35m,
            isReceivable: false);

        Assert.NotNull(result);
        Assert.False(result!.IsGain);
        Assert.Equal(5_000m, result.Amount);
    }

    /// <summary>
    /// Gerçekleşmiş fark: 1.000 dolarlık alacağı 30 kurundan taşırken
    /// 35 kurundan tahsil ettik → 5.000 TL fazla girdi → KÂR.
    /// </summary>
    [Fact]
    public void Realized_CollectingReceivableAtHigherRate_IsGain()
    {
        var result = ExchangeDifferenceCalculator.CalculateRealized(
            settledAmount: 1_000m,
            carryingRate: 30m,
            settlementRate: 35m,
            isReceivable: true);

        Assert.NotNull(result);
        Assert.True(result!.IsGain);
        Assert.Equal(5_000m, result.Amount);
    }

    [Fact]
    public void Realized_WithoutValidRates_ReturnsNull()
    {
        Assert.Null(ExchangeDifferenceCalculator.CalculateRealized(
            1_000m, carryingRate: 0m, settlementRate: 35m, isReceivable: true));
        Assert.Null(ExchangeDifferenceCalculator.CalculateRealized(
            1_000m, carryingRate: 30m, settlementRate: 0m, isReceivable: true));
        Assert.Null(ExchangeDifferenceCalculator.CalculateRealized(
            0m, carryingRate: 30m, settlementRate: 35m, isReceivable: true));
    }
}
