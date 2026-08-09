using EnderunAI.Api.Services.HumanResources;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Ek ücret kalemlerinin bordro tutarına ve istisna matrahına
/// dönüşümü. Motor saf olduğu için kurallar burada doğrudan
/// sabitleniyor.
/// </summary>
public sealed class CompensationComponentCalculatorTests
{
    private const decimal GrossSalary = 60_000m;
    private const decimal DailyWorkHours = 7.5m;

    private static readonly DateTime Start =
        new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static CompensationComponentInput Component(
        int componentType = CompensationComponentType.Meal,
        int calculationType = CompensationCalculationType.Daily,
        int paymentMethod = CompensationPaymentMethod.Payroll,
        decimal amount = 100m,
        bool attendanceBased = true,
        bool inKind = false,
        bool includeInPayroll = true,
        bool sgkBase = false,
        bool incomeTaxBase = false,
        bool stampTaxBase = true,
        DateTime? start = null,
        DateTime? end = null,
        string name = "Yemek Yardımı") =>
        new(name, componentType, calculationType, paymentMethod, amount,
            attendanceBased, inKind, includeInPayroll,
            sgkBase, incomeTaxBase, stampTaxBase,
            start ?? Start, end);

    private static CompensationResult Run(
        CompensationComponentInput component,
        CompensationExemptionCaps? caps = null,
        int month = 6,
        decimal workedDays = 22m) =>
        CompensationComponentCalculator.Calculate(
            new[] { component },
            year: 2026,
            month: month,
            grossSalary: GrossSalary,
            workedDays: workedDays,
            workedHours: workedDays * DailyWorkHours,
            dailyWorkHours: DailyWorkHours,
            caps: caps ?? new CompensationExemptionCaps(
                MealSgkDaily: 150m, MealIncomeTaxDaily: 200m,
                TravelSgkDaily: 100m, TravelIncomeTaxDaily: 120m));

    // ---------------- İstisna tavanı ----------------

    /// <summary>Tavanın altındaki nakdî yardımın tamamı istisnadır.</summary>
    [Fact]
    public void CashMealUnderCap_IsFullyExempt()
    {
        // 22 gün × 100 = 2.200; tavan 22 × 150 = 3.300
        var result = Run(Component(amount: 100m));

        Assert.Equal(2_200m, result.MealAmount);
        Assert.Equal(2_200m, result.SgkExemptEarnings);
        Assert.Equal(2_200m, result.IncomeTaxExemptEarnings);
    }

    /// <summary>
    /// Tavanı aşan nakdî yardımda tavana kadarı istisna, aşan kısım
    /// hem SGK hem gelir vergisi matrahına girer. SGK ve gelir
    /// vergisi tavanları farklı olduğu için matraha giren kısım da
    /// farklı çıkar.
    /// </summary>
    [Fact]
    public void CashMealOverCap_ExemptsUpToCapOnly()
    {
        // 22 gün × 300 = 6.600
        var result = Run(Component(amount: 300m));

        Assert.Equal(6_600m, result.MealAmount);

        // SGK tavanı: 22 × 150 = 3.300 → matraha giren 3.300
        Assert.Equal(3_300m, result.SgkExemptEarnings);

        // Gelir vergisi tavanı: 22 × 200 = 4.400 → matraha giren 2.200
        Assert.Equal(4_400m, result.IncomeTaxExemptEarnings);
    }

    /// <summary>Yol yardımının tavanı yemekten ayrı okunur.</summary>
    [Fact]
    public void TravelUsesItsOwnCaps()
    {
        var result = Run(Component(
            componentType: CompensationComponentType.Travel,
            amount: 300m, name: "Yol Yardımı"));

        Assert.Equal(6_600m, result.TravelAmount);
        Assert.Equal(2_200m, result.SgkExemptEarnings);   // 22 × 100
        Assert.Equal(2_640m, result.IncomeTaxExemptEarnings); // 22 × 120
    }

    /// <summary>Ayni yardımda tavan yok: tamamı istisnadır.</summary>
    [Fact]
    public void InKindMeal_IsFullyExemptRegardlessOfCap()
    {
        var result = Run(Component(amount: 300m, inKind: true));

        Assert.Equal(6_600m, result.MealAmount);
        Assert.Equal(6_600m, result.SgkExemptEarnings);
        Assert.Equal(6_600m, result.IncomeTaxExemptEarnings);
    }

    /// <summary>
    /// Matrah bayrağı açıksa kalem tamamen matrahtadır; ayni olması da
    /// tavan da hükümsüzdür.
    /// </summary>
    [Fact]
    public void FlaggedIntoBase_HasNoExemption()
    {
        var result = Run(Component(
            amount: 300m, inKind: true, sgkBase: true, incomeTaxBase: true));

        Assert.Equal(6_600m, result.MealAmount);
        Assert.Equal(0m, result.SgkExemptEarnings);
        Assert.Equal(0m, result.IncomeTaxExemptEarnings);
    }

    /// <summary>
    /// Tavan tanımlı değilse istisna UYGULANMAZ ve uyarı verilir.
    /// Varsayılana düşmek, o yılın tebliğini beklemeden sessizce eksik
    /// vergi hesaplamak olurdu.
    /// </summary>
    [Fact]
    public void MissingCap_AppliesNoExemptionAndWarns()
    {
        var result = Run(
            Component(amount: 300m),
            caps: new CompensationExemptionCaps());

        Assert.Equal(6_600m, result.MealAmount);
        Assert.Equal(0m, result.SgkExemptEarnings);
        Assert.Equal(0m, result.IncomeTaxExemptEarnings);

        Assert.Contains(result.Warnings, x => x.Contains("SGK istisna tavanı"));
        Assert.Contains(result.Warnings, x => x.Contains("gelir vergisi"));
    }

    /// <summary>
    /// Tavanlardan yalnız biri tanımsızsa diğeri çalışmaya devam eder.
    /// </summary>
    [Fact]
    public void PartiallyMissingCap_StillAppliesTheDefinedOne()
    {
        var result = Run(
            Component(amount: 300m),
            caps: new CompensationExemptionCaps(MealIncomeTaxDaily: 200m));

        Assert.Equal(0m, result.SgkExemptEarnings);
        Assert.Equal(4_400m, result.IncomeTaxExemptEarnings);
        Assert.Single(result.Warnings);
    }

    /// <summary>
    /// Damga vergisinin ayrı bir günlük tavanı yok: bayrak kapalıysa
    /// kalem damga matrahına hiç girmez.
    /// </summary>
    [Fact]
    public void StampTaxExemption_FollowsTheFlagOnly()
    {
        var included = Run(Component(amount: 300m, stampTaxBase: true));
        var excluded = Run(Component(amount: 300m, stampTaxBase: false));

        Assert.Equal(0m, included.StampTaxExemptEarnings);
        Assert.Equal(6_600m, excluded.StampTaxExemptEarnings);
    }

    // ---------------- Ödeme yöntemi ----------------

    /// <summary>
    /// Nakit ödeme resmî bordroya HİÇ girmez — IncludeInPayroll
    /// işaretli olsa bile. Elden ödeme sistemin başka yerinde de resmî
    /// akıştan ayrı tutuluyor.
    /// </summary>
    [Fact]
    public void CashPayment_NeverEntersPayroll()
    {
        var result = Run(Component(
            paymentMethod: CompensationPaymentMethod.Cash,
            includeInPayroll: true,
            amount: 300m));

        Assert.Equal(0m, result.MealAmount);
        Assert.Equal(0m, result.TotalEarnings);
        Assert.Empty(result.Lines);
        Assert.Contains(result.Warnings, x => x.Contains("nakit ödeme"));
    }

    /// <summary>
    /// Bordroya dahil edilmeyen kalem sessizce dışarıda kalır: bu bir
    /// hata değil, kullanıcının tercihi.
    /// </summary>
    [Fact]
    public void NotIncludedInPayroll_IsSkippedSilently()
    {
        var result = Run(Component(includeInPayroll: false));

        Assert.Equal(0m, result.MealAmount);
        Assert.Empty(result.Warnings);
    }

    // ---------------- Hesap türleri ----------------

    [Fact]
    public void DailyComponent_UsesWorkedDaysWhenAttendanceBased()
    {
        var result = Run(Component(amount: 100m, attendanceBased: true));

        Assert.Equal(2_200m, result.MealAmount);
    }

    /// <summary>
    /// Puantaja bağlı değilse tam dönem (30 gün) ile çarpılır:
    /// devamsızlıktan etkilenmeyen kalemler için.
    /// </summary>
    [Fact]
    public void DailyComponent_UsesFullPeriodWhenNotAttendanceBased()
    {
        var result = Run(Component(amount: 100m, attendanceBased: false));

        Assert.Equal(3_000m, result.MealAmount);
    }

    [Fact]
    public void HourlyComponent_UsesWorkedHours()
    {
        var result = Run(Component(
            calculationType: CompensationCalculationType.Hourly,
            amount: 20m, attendanceBased: true));

        // 22 gün × 7,5 saat × 20
        Assert.Equal(3_300m, result.MealAmount);
    }

    [Fact]
    public void HourlyComponent_UsesFullPeriodHoursWhenNotAttendanceBased()
    {
        var result = Run(Component(
            calculationType: CompensationCalculationType.Hourly,
            amount: 20m, attendanceBased: false));

        // 30 gün × 7,5 saat × 20
        Assert.Equal(4_500m, result.MealAmount);
    }

    [Fact]
    public void MonthlyFixed_IsTakenAsIs()
    {
        var result = Run(Component(
            calculationType: CompensationCalculationType.MonthlyFixed,
            amount: 1_750m));

        Assert.Equal(1_750m, result.MealAmount);
    }

    /// <summary>
    /// Yüzdesel kalem ücret kartındaki brüt maaşın yüzdesidir; toplam
    /// kazancın değil — iki yüzdesel kalem birbirini beslemesin diye.
    /// </summary>
    [Fact]
    public void Percentage_IsOfGrossSalary()
    {
        var result = Run(Component(
            componentType: CompensationComponentType.Bonus,
            calculationType: CompensationCalculationType.Percentage,
            amount: 10m, name: "Prim"));

        Assert.Equal(6_000m, result.BonusAmount);
    }

    /// <summary>Tek seferlik kalem yalnızca yürürlüğe girdiği ayda ödenir.</summary>
    [Fact]
    public void OneTime_PaysOnlyInItsStartMonth()
    {
        var component = Component(
            componentType: CompensationComponentType.Gratuity,
            calculationType: CompensationCalculationType.OneTime,
            amount: 15_000m,
            start: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            name: "Bayram İkramiyesi");

        Assert.Equal(15_000m, Run(component, month: 6).BonusAmount);
        Assert.Equal(0m, Run(component, month: 7).BonusAmount);
    }

    // ---------------- Kalem türü eşlemesi ----------------

    [Theory]
    [InlineData(CompensationComponentType.Bonus)]
    [InlineData(CompensationComponentType.Gratuity)]
    public void BonusAndGratuity_LandOnBonus(int componentType)
    {
        var result = Run(Component(
            componentType: componentType,
            calculationType: CompensationCalculationType.MonthlyFixed,
            amount: 1_000m));

        Assert.Equal(1_000m, result.BonusAmount);
    }

    [Theory]
    [InlineData(CompensationComponentType.Accommodation)]
    [InlineData(CompensationComponentType.ShiftDifference)]
    [InlineData(CompensationComponentType.Other)]
    public void UncategorizedTypes_LandOnOtherEarning(int componentType)
    {
        var result = Run(Component(
            componentType: componentType,
            calculationType: CompensationCalculationType.MonthlyFixed,
            amount: 1_000m));

        Assert.Equal(1_000m, result.OtherEarningAmount);
    }

    [Fact]
    public void CompensationType_LandsOnCompensation()
    {
        var result = Run(Component(
            componentType: CompensationComponentType.Compensation,
            calculationType: CompensationCalculationType.MonthlyFixed,
            amount: 5_000m));

        Assert.Equal(5_000m, result.CompensationAmount);
        Assert.Equal(5_000m, result.TotalEarnings);
    }

    /// <summary>
    /// Kesinti kazanç değildir: toplam kazanca girmez, kesinti
    /// tarafına yazılır ve istisna üretmez.
    /// </summary>
    [Fact]
    public void DeductionType_LandsOnDeductionAndNotEarnings()
    {
        var result = Run(Component(
            componentType: CompensationComponentType.Deduction,
            calculationType: CompensationCalculationType.MonthlyFixed,
            amount: 750m, name: "İcra Kesintisi"));

        Assert.Equal(750m, result.DeductionAmount);
        Assert.Equal(0m, result.TotalEarnings);
        Assert.Equal(0m, result.SgkExemptEarnings);
    }

    // ---------------- Yürürlük ----------------

    [Fact]
    public void ComponentEndedBeforePeriod_IsIgnored()
    {
        var result = Run(Component(
            end: new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc)), month: 6);

        Assert.Equal(0m, result.MealAmount);
    }

    [Fact]
    public void ComponentStartingAfterPeriod_IsIgnored()
    {
        var result = Run(Component(
            start: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)), month: 6);

        Assert.Equal(0m, result.MealAmount);
    }

    /// <summary>Ay içinde başlayan kalem o ay sayılır.</summary>
    [Fact]
    public void ComponentStartingMidPeriod_IsIncluded()
    {
        var result = Run(Component(
            calculationType: CompensationCalculationType.MonthlyFixed,
            amount: 1_000m,
            start: new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc)), month: 6);

        Assert.Equal(1_000m, result.MealAmount);
    }

    // ---------------- Toplama ----------------

    /// <summary>
    /// Birden çok kalem türlerine göre ayrışır ve istisnalar toplanır.
    /// </summary>
    [Fact]
    public void MultipleComponents_AreAggregatedByType()
    {
        var result = CompensationComponentCalculator.Calculate(
            new[]
            {
                Component(amount: 100m),
                Component(componentType: CompensationComponentType.Travel,
                    amount: 50m, name: "Yol"),
                Component(componentType: CompensationComponentType.Bonus,
                    calculationType: CompensationCalculationType.MonthlyFixed,
                    amount: 3_000m, sgkBase: true, incomeTaxBase: true,
                    name: "Prim"),
                Component(componentType: CompensationComponentType.Deduction,
                    calculationType: CompensationCalculationType.MonthlyFixed,
                    amount: 500m, name: "Kesinti")
            },
            year: 2026, month: 6, grossSalary: GrossSalary,
            workedDays: 22m, workedHours: 165m, dailyWorkHours: DailyWorkHours,
            caps: new CompensationExemptionCaps(
                MealSgkDaily: 150m, MealIncomeTaxDaily: 200m,
                TravelSgkDaily: 100m, TravelIncomeTaxDaily: 120m));

        Assert.Equal(2_200m, result.MealAmount);
        Assert.Equal(1_100m, result.TravelAmount);
        Assert.Equal(3_000m, result.BonusAmount);
        Assert.Equal(500m, result.DeductionAmount);
        Assert.Equal(6_300m, result.TotalEarnings);

        // Yemek ve yol tavan altında: ikisi de tam istisna. Prim
        // matrahta olduğu için istisnaya girmez.
        Assert.Equal(3_300m, result.SgkExemptEarnings);
        Assert.Equal(3_300m, result.IncomeTaxExemptEarnings);
    }

    [Fact]
    public void NoComponents_ReturnsEmpty()
    {
        var result = CompensationComponentCalculator.Calculate(
            Array.Empty<CompensationComponentInput>(),
            2026, 6, GrossSalary, 22m, 165m, DailyWorkHours,
            new CompensationExemptionCaps());

        Assert.Equal(0m, result.TotalEarnings);
        Assert.Empty(result.Warnings);
    }
}
