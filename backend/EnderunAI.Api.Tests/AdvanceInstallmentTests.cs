using EnderunAI.Api.Services.HumanResources;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Avans taksitleri ve bordro kesintisi (H6).
///
/// Denetimde bulunan eksik: taksit sayısı ve ilk kesinti tarihi
/// alanları vardı, bordro kaydında AdvanceDeduction alanı vardı ve
/// hesaba giriyordu — ama o alana kod hiçbir yerde değer YAZMIYORDU.
/// Kullanıcı taksit giriyor, bordro çalışıyor, kesinti sıfır kalıyordu.
///
/// Korunan iki kural:
/// - Kesinti o ayın NETİNİ aşamaz. Aşan kısım kaybolmaz; kesilmediği
///   için bakiye düşmez ve gelecek ay gecikmiş taksit olarak döner.
/// - Taksitlerin toplamı onaylanan tutara BİREBİR eşittir; kuruş
///   artığı son taksite biner.
/// </summary>
public sealed class AdvanceInstallmentTests
{
    private static readonly Guid First =
        Guid.Parse("11111111-0000-0000-0000-000000000001");

    private static readonly Guid Second =
        Guid.Parse("22222222-0000-0000-0000-000000000002");

    private static AdvanceDeductionInput Advance(
        decimal amount = 12_000m,
        int installments = 3,
        int year = 2026,
        int month = 3,
        decimal alreadyDeducted = 0m,
        Guid? id = null) =>
        new(id ?? First, amount, installments,
            new DateOnly(year, month, 1), alreadyDeducted);

    // ---------- Plan ----------

    [Fact]
    public void Plan_SplitsAmountAcrossMonths()
    {
        var plan = AdvanceInstallmentCalculator.BuildPlan(
            12_000m, 3, new DateOnly(2026, 3, 1));

        Assert.Equal(3, plan.Installments.Count);
        Assert.All(plan.Installments, x => Assert.Equal(4_000m, x.Amount));

        Assert.Equal((2026, 3), (plan.Installments[0].Year, plan.Installments[0].Month));
        Assert.Equal((2026, 5), (plan.Installments[2].Year, plan.Installments[2].Month));
    }

    /// <summary>Kuruş artığı son taksite biner; toplam birebir tutar.</summary>
    [Fact]
    public void Plan_TotalMatchesExactlyDespiteRounding()
    {
        var plan = AdvanceInstallmentCalculator.BuildPlan(
            10_000m, 3, new DateOnly(2026, 1, 1));

        Assert.Equal(10_000m, plan.Installments.Sum(x => x.Amount));
        Assert.Equal(3_333.33m, plan.Installments[0].Amount);
        Assert.Equal(3_333.34m, plan.Installments[2].Amount);
    }

    [Fact]
    public void Plan_CrossesYearBoundary()
    {
        var plan = AdvanceInstallmentCalculator.BuildPlan(
            3_000m, 3, new DateOnly(2026, 12, 1));

        Assert.Equal((2026, 12), (plan.Installments[0].Year, plan.Installments[0].Month));
        Assert.Equal((2027, 1), (plan.Installments[1].Year, plan.Installments[1].Month));
        Assert.Equal((2027, 2), (plan.Installments[2].Year, plan.Installments[2].Month));
    }

    [Fact]
    public void Plan_WithoutInstallmentCount_IsASinglePayment()
    {
        var plan = AdvanceInstallmentCalculator.BuildPlan(
            5_000m, 0, new DateOnly(2026, 3, 1));

        Assert.Single(plan.Installments);
        Assert.Equal(5_000m, plan.Installments[0].Amount);
    }

    [Fact]
    public void Plan_OfNothing_IsEmpty()
    {
        Assert.Empty(AdvanceInstallmentCalculator
            .BuildPlan(0m, 3, new DateOnly(2026, 3, 1)).Installments);
    }

    // ---------- Dönem kesintisi ----------

    [Fact]
    public void BeforeFirstInstallment_NothingIsDeducted()
    {
        var result = AdvanceInstallmentCalculator.Resolve(
            [Advance()], 2026, 2, availableNet: 50_000m);

        Assert.Equal(0m, result.Total);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void FirstMonth_DeductsOneInstallment()
    {
        var result = AdvanceInstallmentCalculator.Resolve(
            [Advance()], 2026, 3, availableNet: 50_000m);

        Assert.Equal(4_000m, result.Total);
        Assert.Equal(0m, result.Uncovered);
    }

    /// <summary>
    /// Gecikmiş taksitler telafi edilir: ikinci ayda hiç kesilmemişse
    /// üçüncü ayda iki taksit birden düşer.
    /// </summary>
    [Fact]
    public void MissedInstallments_AreCaughtUp()
    {
        var result = AdvanceInstallmentCalculator.Resolve(
            [Advance(alreadyDeducted: 4_000m)], 2026, 5, availableNet: 50_000m);

        // 3 taksitin tamamı planlanmış, 4.000 kesilmiş: 8.000 kalır.
        Assert.Equal(8_000m, result.Total);
    }

    [Fact]
    public void FullyDeductedAdvance_IsIgnored()
    {
        var result = AdvanceInstallmentCalculator.Resolve(
            [Advance(alreadyDeducted: 12_000m)], 2026, 6, availableNet: 50_000m);

        Assert.Equal(0m, result.Total);
        Assert.Empty(result.Lines);
    }

    /// <summary>
    /// Kesinti bakiyeyi aşamaz: plan fazla gösterse bile borç kadar
    /// kesilir.
    /// </summary>
    [Fact]
    public void DeductionNeverExceedsTheBalance()
    {
        var result = AdvanceInstallmentCalculator.Resolve(
            [Advance(alreadyDeducted: 11_500m)], 2026, 12, availableNet: 50_000m);

        Assert.Equal(500m, result.Total);
    }

    // ---------- Net sınırı ----------

    /// <summary>
    /// Kesinti neti aşamaz; aşan kısım "kesilemedi" olarak raporlanır
    /// ve bakiyeden düşmez.
    /// </summary>
    [Fact]
    public void DeductionIsCappedAtAvailableNet()
    {
        var result = AdvanceInstallmentCalculator.Resolve(
            [Advance()], 2026, 3, availableNet: 1_500m);

        Assert.Equal(1_500m, result.Total);
        Assert.Equal(2_500m, result.Uncovered);
    }

    [Fact]
    public void ZeroNet_DeductsNothing()
    {
        var result = AdvanceInstallmentCalculator.Resolve(
            [Advance()], 2026, 3, availableNet: 0m);

        Assert.Equal(0m, result.Total);
        Assert.Equal(4_000m, result.Uncovered);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void NegativeNet_IsTreatedAsZero()
    {
        var result = AdvanceInstallmentCalculator.Resolve(
            [Advance()], 2026, 3, availableNet: -900m);

        Assert.Equal(0m, result.Total);
    }

    /// <summary>
    /// Kesilemeyen tutar KAYBOLMAZ: bakiye düşmediği için gelecek ay
    /// yeniden gündeme gelir.
    /// </summary>
    [Fact]
    public void UncoveredAmount_ReturnsNextMonth()
    {
        var short_ = AdvanceInstallmentCalculator.Resolve(
            [Advance()], 2026, 3, availableNet: 1_000m);

        Assert.Equal(1_000m, short_.Total);

        var next = AdvanceInstallmentCalculator.Resolve(
            [Advance(alreadyDeducted: 1_000m)], 2026, 4, availableNet: 50_000m);

        // 2 taksit planlandı (8.000), 1.000 kesildi → 7.000 kalır.
        Assert.Equal(7_000m, next.Total);
    }

    // ---------- Birden fazla avans ----------

    /// <summary>
    /// En eski avanstan başlanır: borcun yaşlanmaması için ve
    /// kullanıcının beklediği sıra bu.
    /// </summary>
    [Fact]
    public void OldestAdvance_IsDeductedFirstWhenNetIsShort()
    {
        var result = AdvanceInstallmentCalculator.Resolve(
        [
            Advance(amount: 6_000m, installments: 1, year: 2026, month: 3, id: First),
            Advance(amount: 6_000m, installments: 1, year: 2026, month: 2, id: Second)
        ],
        2026, 3, availableNet: 6_000m);

        var line = Assert.Single(result.Lines);

        Assert.Equal(Second, line.AdvanceId);
        Assert.Equal(6_000m, line.Amount);
        Assert.Equal(6_000m, result.Uncovered);
    }

    [Fact]
    public void MultipleAdvances_AreAllDeductedWhenNetAllows()
    {
        var result = AdvanceInstallmentCalculator.Resolve(
        [
            Advance(amount: 3_000m, installments: 1, id: First),
            Advance(amount: 2_000m, installments: 1, id: Second)
        ],
        2026, 3, availableNet: 50_000m);

        Assert.Equal(2, result.Lines.Count);
        Assert.Equal(5_000m, result.Total);
        Assert.Equal(0m, result.Uncovered);
    }

    [Fact]
    public void NoAdvances_ProduceNoDeduction()
    {
        var result = AdvanceInstallmentCalculator.Resolve([], 2026, 3, 50_000m);

        Assert.Equal(0m, result.Total);
        Assert.Empty(result.Lines);
    }

    /// <summary>
    /// Satırda planlanan tutar da taşınıyor: kesilenle arasındaki fark
    /// ertelenen kısımdır ve raporlanabilmeli.
    /// </summary>
    [Fact]
    public void Line_CarriesScheduledAmountBesideTheDeducted()
    {
        var result = AdvanceInstallmentCalculator.Resolve(
            [Advance()], 2026, 3, availableNet: 1_500m);

        var line = Assert.Single(result.Lines);

        Assert.Equal(4_000m, line.ScheduledAmount);
        Assert.Equal(1_500m, line.Amount);
    }
}
