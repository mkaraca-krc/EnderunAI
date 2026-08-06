using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Subcontractors;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Yansıtma motoru testleri. Motor static ve veritabanısız olduğu için
/// bu testler entegrasyon fixture'ı istemiyor.
///
/// Asıl güvence "doğru sayıyı üretiyor mu" kadar "üretemeyeceği yerde
/// SUSUYOR mu": yanlış bir öneri boş satırdan pahalıdır, çünkü
/// onaylanır.
/// </summary>
public sealed class SubcontractorReflectionTests
{
    // ---------- İSG ----------

    [Fact]
    public void Ohs_SplitsEmployerDeductionByWorkerShare()
    {
        var result = SubcontractorReflectionCalculator.CalculateOhs(
            SubcontractorResponsibility.Us,
            employerOhsDeduction: 10_000m,
            subcontractorWorkerCount: 12,
            siteWorkerCount: 40);

        Assert.NotNull(result);
        Assert.Equal(3_000m, result!.Amount);
        Assert.Equal(
            (int)HakedisDeductionType.OhsContribution, result.DeductionType);
        Assert.Contains("12", result.Basis);
        Assert.Contains("40", result.Basis);
    }

    /// <summary>
    /// Sözleşmede İSG taşerondaysa yansıtma yapılmaz — kendi masrafını
    /// kendi karşılıyor.
    /// </summary>
    [Fact]
    public void Ohs_ReturnsNullWhenResponsibilityIsSubcontractor()
    {
        var result = SubcontractorReflectionCalculator.CalculateOhs(
            SubcontractorResponsibility.Subcontractor,
            employerOhsDeduction: 10_000m,
            subcontractorWorkerCount: 12,
            siteWorkerCount: 40);

        Assert.Null(result);
    }

    [Theory]
    // İşveren hakedişinden İSG kesilmemişse yansıtılacak bir şey yok.
    [InlineData(0, 12, 40)]
    // Şantiyede taşeron işçisi puantajı yoksa pay sıfırdır.
    [InlineData(10_000, 0, 40)]
    // Payda sıfırsa oran hesaplanamaz.
    [InlineData(10_000, 12, 0)]
    // Veri tutarsız: taşeron işçisi şantiye toplamından fazla olamaz.
    [InlineData(10_000, 50, 40)]
    public void Ohs_ReturnsNullWhenInputsCannotProduceShare(
        int employerDeduction, int subcontractorWorkers, int siteWorkers)
    {
        var result = SubcontractorReflectionCalculator.CalculateOhs(
            SubcontractorResponsibility.Us,
            employerDeduction,
            subcontractorWorkers,
            siteWorkers);

        Assert.Null(result);
    }

    /// <summary>
    /// Taşeron tek başına şantiyedeyse işveren kesintisinin tamamı
    /// yansır.
    /// </summary>
    [Fact]
    public void Ohs_ReflectsFullAmountWhenSubcontractorIsTheWholeSite()
    {
        var result = SubcontractorReflectionCalculator.CalculateOhs(
            SubcontractorResponsibility.Us,
            employerOhsDeduction: 7_450.55m,
            subcontractorWorkerCount: 18,
            siteWorkerCount: 18);

        Assert.NotNull(result);
        Assert.Equal(7_450.55m, result!.Amount);
    }

    // ---------- Yemek / konaklama ----------

    [Fact]
    public void Meal_MultipliesEmployerUnitPriceBySubcontractorQuantity()
    {
        var result = SubcontractorReflectionCalculator.CalculateMeal(
            SubcontractorResponsibility.Us,
            [
                new ReflectionLineInput("Kahvaltı", 45m, 320m),
                new ReflectionLineInput("Öğlen", 90m, 320m),
                new ReflectionLineInput("Akşam", 75m, 180m)
            ]);

        Assert.NotNull(result);
        Assert.Equal((int)HakedisDeductionType.Meal, result!.DeductionType);
        // 14.400 + 28.800 + 13.500
        Assert.Equal(56_700m, result.Amount);
        Assert.Contains("Kahvaltı", result.Basis);
        Assert.Contains("Akşam", result.Basis);
    }

    [Fact]
    public void Accommodation_UsesAccommodationDeductionType()
    {
        var result = SubcontractorReflectionCalculator.CalculateAccommodation(
            SubcontractorResponsibility.Us,
            [new ReflectionLineInput("Yatılı", 250m, 62m)]);

        Assert.NotNull(result);
        Assert.Equal(
            (int)HakedisDeductionType.Accommodation, result!.DeductionType);
        Assert.Equal(15_500m, result.Amount);
    }

    /// <summary>
    /// Birim fiyatı ya da adedi olmayan alt kalem sessizce atlanır —
    /// o kalemi hiç kullanmamışız demektir; kalanlar yine hesaplanır.
    /// </summary>
    [Fact]
    public void Meal_SkipsLinesWithoutPriceOrQuantity()
    {
        var result = SubcontractorReflectionCalculator.CalculateMeal(
            SubcontractorResponsibility.Us,
            [
                new ReflectionLineInput("Kahvaltı", 45m, 0m),
                new ReflectionLineInput("Öğlen", 0m, 320m),
                new ReflectionLineInput("Kumanya", 60m, 25m)
            ]);

        Assert.NotNull(result);
        Assert.Equal(1_500m, result!.Amount);
        Assert.DoesNotContain("Kahvaltı", result.Basis);
        Assert.DoesNotContain("Öğlen", result.Basis);
    }

    [Fact]
    public void Meal_ReturnsNullWhenNoLineProducesAmount()
    {
        var result = SubcontractorReflectionCalculator.CalculateMeal(
            SubcontractorResponsibility.Us,
            [
                new ReflectionLineInput("Kahvaltı", 45m, 0m),
                new ReflectionLineInput("Öğlen", 0m, 320m)
            ]);

        Assert.Null(result);
    }

    [Fact]
    public void Meal_ReturnsNullWhenThereAreNoLines()
    {
        var result = SubcontractorReflectionCalculator.CalculateMeal(
            SubcontractorResponsibility.Us, []);

        Assert.Null(result);
    }

    [Fact]
    public void Meal_ReturnsNullWhenResponsibilityIsSubcontractor()
    {
        var result = SubcontractorReflectionCalculator.CalculateMeal(
            SubcontractorResponsibility.Subcontractor,
            [new ReflectionLineInput("Öğlen", 90m, 320m)]);

        Assert.Null(result);
    }

    /// <summary>
    /// Yansıtma kâr merkezi değildir: taşerona geçen tutar, işveren
    /// hakedişinden bize kesilen birim fiyatın aynısıyla hesaplanır.
    /// Bu test, ileride "yansıtmaya pay ekleyelim" diye sessiz bir
    /// çarpan girmesini engelliyor.
    /// </summary>
    [Fact]
    public void Reflection_DoesNotAddMarginOverEmployerPrice()
    {
        const decimal employerUnitPrice = 137.45m;
        const decimal quantity = 91m;

        var result = SubcontractorReflectionCalculator.CalculateAccommodation(
            SubcontractorResponsibility.Us,
            [new ReflectionLineInput("Evci", employerUnitPrice, quantity)]);

        Assert.NotNull(result);
        Assert.Equal(
            decimal.Round(employerUnitPrice * quantity, 2), result!.Amount);
    }
}
