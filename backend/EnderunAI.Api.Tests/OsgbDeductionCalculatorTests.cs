using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Isg;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// OSGB bedelinin hakediş kesintisine çevrilmesi. Saf hesap — veritabanı
/// yok.
///
/// En kritik davranış: hesaplanamayan durumda ÖNERİ ÜRETİLMEMESİ.
/// Sözleşme yokken sıfır tutarlı bir kesinti önermek, ön muhasebenin
/// "sistem hesapladı" diye geçmesine yol açardı.
/// </summary>
public sealed class OsgbDeductionCalculatorTests
{
    private static IsgOsgbContract Monthly(
        decimal fee = 12_000m,
        string start = "2026-01-01",
        string? end = null) => new()
        {
            BillingType = OsgbBillingType.MonthlyFixed,
            MonthlyFee = fee,
            StartDate = DateOnly.Parse(start),
            EndDate = end is null ? null : DateOnly.Parse(end)
        };

    private static IsgOsgbContract PerPerson(
        decimal fee = 150m,
        string start = "2026-01-01",
        string? end = null) => new()
        {
            BillingType = OsgbBillingType.PerPerson,
            PerPersonFee = fee,
            StartDate = DateOnly.Parse(start),
            EndDate = end is null ? null : DateOnly.Parse(end)
        };

    [Fact]
    public void MonthlyFixed_UsesContractFee()
    {
        var result = OsgbDeductionCalculator.Calculate(
            Monthly(fee: 12_500m), DateOnly.Parse("2026-06-15"), activePersonCount: 40);

        Assert.NotNull(result);
        // Sabit bedelde kişi sayısı tutarı etkilemez.
        Assert.Equal(12_500m, result!.Amount);
        Assert.Null(result.PersonCount);
    }

    [Fact]
    public void PerPerson_MultipliesByActivePersonnel()
    {
        var result = OsgbDeductionCalculator.Calculate(
            PerPerson(fee: 150m), DateOnly.Parse("2026-06-15"), activePersonCount: 37);

        Assert.NotNull(result);
        Assert.Equal(5_550m, result!.Amount);
        Assert.Equal(37, result.PersonCount);
        Assert.Contains("37 kişi", result.Description);
    }

    [Fact]
    public void NoContract_ProducesNoSuggestion()
    {
        Assert.Null(OsgbDeductionCalculator.Calculate(
            null, DateOnly.Parse("2026-06-15"), activePersonCount: 40));
    }

    [Fact]
    public void PeriodBeforeContractStart_ProducesNoSuggestion()
    {
        var result = OsgbDeductionCalculator.Calculate(
            Monthly(start: "2026-07-01"), DateOnly.Parse("2026-06-15"), 40);

        Assert.Null(result);
    }

    [Fact]
    public void PeriodAfterContractEnd_ProducesNoSuggestion()
    {
        var result = OsgbDeductionCalculator.Calculate(
            Monthly(start: "2026-01-01", end: "2026-05-31"),
            DateOnly.Parse("2026-06-15"), 40);

        Assert.Null(result);
    }

    [Fact]
    public void ContractEndingInsidePeriodMonth_StillCovered()
    {
        // Sözleşme 20 Haziran'da bitiyor; haziran hakedişinde kesinti
        // yapılır. Ay bazında bakılmasının sebebi bu.
        var result = OsgbDeductionCalculator.Calculate(
            Monthly(start: "2026-01-01", end: "2026-06-20"),
            DateOnly.Parse("2026-06-30"), 40);

        Assert.NotNull(result);
    }

    [Fact]
    public void ContractStartingInsidePeriodMonth_IsCovered()
    {
        var result = OsgbDeductionCalculator.Calculate(
            Monthly(start: "2026-06-20"), DateOnly.Parse("2026-06-01"), 40);

        Assert.NotNull(result);
    }

    [Fact]
    public void OpenEndedContract_NeverExpires()
    {
        var result = OsgbDeductionCalculator.Calculate(
            Monthly(start: "2020-01-01", end: null),
            DateOnly.Parse("2030-12-31"), 40);

        Assert.NotNull(result);
    }

    [Fact]
    public void PerPersonWithNoActivePersonnel_ProducesNoSuggestion()
    {
        // Sıfır kişi × birim bedel = 0 TL. Sıfırlık kesinti satırı
        // önermek yerine hiç önermiyoruz.
        Assert.Null(OsgbDeductionCalculator.Calculate(
            PerPerson(), DateOnly.Parse("2026-06-15"), activePersonCount: 0));
    }

    [Fact]
    public void ZeroMonthlyFee_ProducesNoSuggestion()
    {
        Assert.Null(OsgbDeductionCalculator.Calculate(
            Monthly(fee: 0m), DateOnly.Parse("2026-06-15"), 40));
    }

    [Fact]
    public void PerPersonAmount_IsRoundedToKurus()
    {
        var result = OsgbDeductionCalculator.Calculate(
            PerPerson(fee: 133.333m), DateOnly.Parse("2026-06-15"), 3);

        Assert.NotNull(result);
        Assert.Equal(400.00m, result!.Amount);
    }
}
