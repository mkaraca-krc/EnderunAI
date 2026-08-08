using EnderunAI.Api.Services.Schedule;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Gecikme cezası (G1).
///
/// Referans sözleşme: 10.000.000 TL bedel, günlük binde 1 ceza,
/// tavan bedelin %10'u (1.000.000 TL).
///
/// Korunan fikirler:
/// - Oran YÜZDE tutulur (binde 1 = 0,1). Bu kod tabanındaki bütün
///   oranlar yüzde; tek bir alanın binde olması sessiz on kat hatası
///   üretirdi.
/// - Tanımı eksik sözleşmede ceza HESAPLANMAZ. Sıfır göstermek "ceza
///   yok" demektir; hesaplanamadığını söylemek başka bir şeydir.
/// - Tavan varsa hesap orada durur.
/// </summary>
public sealed class DelayPenaltyCalculatorTests
{
    private const decimal Contract = 10_000_000m;

    private static DelayPenaltyInput Input(
        int delayDays,
        DelayPenaltyKind kind = DelayPenaltyKind.RateOfContractPerDay,
        decimal value = 0.1m,
        decimal? cap = null,
        decimal contract = Contract) =>
        new(kind, value, cap, contract, delayDays);

    // ---------- Oransal ceza ----------

    [Fact]
    public void RateOfContract_DailyAmountIsThePermille()
    {
        var result = DelayPenaltyCalculator.Calculate(Input(delayDays: 1));

        Assert.True(result.Applicable);
        Assert.Equal(10_000m, result.DailyAmount);
        Assert.Equal(10_000m, result.Amount);
    }

    [Fact]
    public void RateOfContract_MultipliesByDelayDays()
    {
        var result = DelayPenaltyCalculator.Calculate(Input(delayDays: 30));

        Assert.Equal(300_000m, result.RawAmount);
        Assert.Equal(300_000m, result.Amount);
        Assert.False(result.CapApplied);
    }

    // ---------- Tavan ----------

    [Fact]
    public void CapFromRate_ConvertsAPercentageOfTheContract()
    {
        Assert.Equal(1_000_000m, DelayPenaltyCalculator.CapFromRate(Contract, 10m));
    }

    [Fact]
    public void CapFromRate_IsNullWhenNotDefined()
    {
        Assert.Null(DelayPenaltyCalculator.CapFromRate(Contract, null));
        Assert.Null(DelayPenaltyCalculator.CapFromRate(Contract, 0m));
        Assert.Null(DelayPenaltyCalculator.CapFromRate(0m, 10m));
    }

    [Fact]
    public void PenaltyStopsAtTheCap()
    {
        var result = DelayPenaltyCalculator.Calculate(
            Input(delayDays: 200, cap: 1_000_000m));

        Assert.Equal(2_000_000m, result.RawAmount);
        Assert.Equal(1_000_000m, result.Amount);
        Assert.True(result.CapApplied);
    }

    [Fact]
    public void PenaltyBelowTheCap_IsNotTouched()
    {
        var result = DelayPenaltyCalculator.Calculate(
            Input(delayDays: 50, cap: 1_000_000m));

        Assert.Equal(500_000m, result.Amount);
        Assert.False(result.CapApplied);
    }

    // ---------- Sabit günlük tutar ----------

    [Fact]
    public void FixedAmountPerDay_IgnoresTheContractAmount()
    {
        var result = DelayPenaltyCalculator.Calculate(Input(
            delayDays: 10,
            kind: DelayPenaltyKind.FixedAmountPerDay,
            value: 5_000m,
            contract: 0m));

        Assert.True(result.Applicable);
        Assert.Equal(5_000m, result.DailyAmount);
        Assert.Equal(50_000m, result.Amount);
    }

    // ---------- Hesaplanmayan durumlar ----------

    [Fact]
    public void NoPenaltyInContract_IsNotCalculated()
    {
        var result = DelayPenaltyCalculator.Calculate(
            Input(delayDays: 100, kind: DelayPenaltyKind.None));

        Assert.False(result.Applicable);
        Assert.Equal(0m, result.Amount);
        Assert.Contains("tanımlı değil", result.Note!);
    }

    [Fact]
    public void MissingRate_IsNotCalculated()
    {
        var result = DelayPenaltyCalculator.Calculate(
            Input(delayDays: 100, value: 0m));

        Assert.False(result.Applicable);
        Assert.Contains("girilmemiş", result.Note!);
    }

    /// <summary>
    /// Oransal ceza sözleşme bedeli olmadan hesaplanamaz; sıfır TL
    /// göstermek "ceza yok" der ve yanlış olur.
    /// </summary>
    [Fact]
    public void RateWithoutContractAmount_IsNotCalculated()
    {
        var result = DelayPenaltyCalculator.Calculate(
            Input(delayDays: 100, contract: 0m));

        Assert.False(result.Applicable);
        Assert.Equal(0m, result.Amount);
        Assert.Contains("Sözleşme bedeli", result.Note!);
    }

    // ---------- Gecikme yok ----------

    /// <summary>
    /// Gecikme yokken ceza sıfırdır ama GÜNLÜK tutar yine döner:
    /// ekran "gecikmenin günü şu kadara mal oluyor" diye gösteriyor.
    /// </summary>
    [Fact]
    public void NoDelay_StillReportsTheDailyCost()
    {
        var result = DelayPenaltyCalculator.Calculate(Input(delayDays: 0));

        Assert.False(result.Applicable);
        Assert.Equal(10_000m, result.DailyAmount);
        Assert.Equal(0m, result.Amount);
        Assert.Equal("Gecikme yok.", result.Note);
    }

    [Fact]
    public void NegativeDelay_IsTreatedAsNoDelay()
    {
        var result = DelayPenaltyCalculator.Calculate(Input(delayDays: -5));

        Assert.False(result.Applicable);
        Assert.Equal(0m, result.Amount);
    }
}
