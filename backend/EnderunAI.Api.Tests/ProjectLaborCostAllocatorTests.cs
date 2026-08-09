using EnderunAI.Api.Services.HumanResources;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Ek ücret kalemlerinin günlük proje işçilik maliyetine dağıtılması.
///
/// Bu dört alan (yemek, konaklama, servis, elden) daha önce HİÇ
/// yazılmıyordu: TotalLaborCost her zaman salt puantaj ücretine
/// eşitti, dolayısıyla kâr olduğundan yüksek görünüyordu.
/// </summary>
public sealed class ProjectLaborCostAllocatorTests
{
    private static readonly Guid ProjectA = Guid.NewGuid();
    private static readonly Guid ProjectB = Guid.NewGuid();

    private static readonly DateTime WorkDate =
        new(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc);

    private const decimal MonthlyGross = 60_000m;
    private const decimal DayEarnings = 2_000m;

    private static CompensationComponentInput Component(
        int componentType = CompensationComponentType.Meal,
        int calculationType = CompensationCalculationType.Daily,
        int paymentMethod = CompensationPaymentMethod.Payroll,
        decimal amount = 100m,
        bool includeInProjectCost = true,
        bool includeInProgressPaymentCost = false,
        Guid? projectId = null,
        DateTime? start = null,
        DateTime? end = null,
        string name = "Yemek") =>
        new(name, componentType, calculationType, paymentMethod, amount,
            IsAttendanceBased: false,
            IsInKindBenefit: false,
            IncludeInPayroll: true,
            IncludeInSgkBase: false,
            IncludeInIncomeTaxBase: false,
            IncludeInStampTaxBase: true,
            EffectiveStartDate: start ?? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EffectiveEndDate: end,
            IncludeInProjectCost: includeInProjectCost,
            IncludeInProgressPaymentCost: includeInProgressPaymentCost,
            ProjectId: projectId);

    private static ProjectLaborCostAllocation Run(
        CompensationComponentInput component,
        int workedDays = 20,
        decimal dayHours = 8m,
        Guid? projectId = null) =>
        ProjectLaborCostAllocator.Allocate(
            new[] { component },
            projectId ?? ProjectA,
            WorkDate,
            workedDays,
            dayHours,
            DayEarnings,
            MonthlyGross);

    // ---------------- Kova eşlemesi ----------------

    [Fact]
    public void MealComponent_LandsOnMealCost()
    {
        var result = Run(Component(componentType: CompensationComponentType.Meal));

        Assert.Equal(100m, result.MealCost);
        Assert.Equal(100m, result.Total);
    }

    [Fact]
    public void AccommodationComponent_LandsOnAccommodationCost()
    {
        var result = Run(Component(
            componentType: CompensationComponentType.Accommodation, name: "Konaklama"));

        Assert.Equal(100m, result.AccommodationCost);
    }

    /// <summary>Yol yardımı servis maliyeti kovasına gider.</summary>
    [Fact]
    public void TravelComponent_LandsOnShuttleCost()
    {
        var result = Run(Component(
            componentType: CompensationComponentType.Travel, name: "Servis"));

        Assert.Equal(100m, result.ShuttleCost);
    }

    [Theory]
    [InlineData(CompensationComponentType.Bonus)]
    [InlineData(CompensationComponentType.Compensation)]
    [InlineData(CompensationComponentType.Other)]
    public void UnmappedTypes_LandOnOtherCost(int componentType)
    {
        var result = Run(Component(componentType: componentType));

        Assert.Equal(100m, result.OtherCost);
    }

    /// <summary>Kesinti bir maliyet değildir; dağıtıma hiç girmez.</summary>
    [Fact]
    public void Deduction_IsNotACost()
    {
        var result = Run(Component(
            componentType: CompensationComponentType.Deduction, name: "İcra"));

        Assert.Equal(0m, result.Total);
    }

    /// <summary>
    /// Nakit ödenen kalem türü ne olursa olsun elden kovasına gider:
    /// o kova ek ödeme yetkisi olmayan kullanıcıdan maskeleniyor.
    /// </summary>
    [Fact]
    public void CashComponent_LandsOnCompensationCostRegardlessOfType()
    {
        var result = Run(Component(
            componentType: CompensationComponentType.Meal,
            paymentMethod: CompensationPaymentMethod.Cash,
            name: "Elden Yemek"));

        Assert.Equal(0m, result.MealCost);
        Assert.Equal(100m, result.CompensationCost);
    }

    // ---------------- Hesap türleri ----------------

    /// <summary>
    /// Aylık kalem, kişinin o ayki fiilen çalışılan gün sayısına
    /// bölünür: 6.000 TL / 20 gün = 300 TL. Sabit 30'a bölmek kalemin
    /// üçte birini hiçbir projeye yazmamak olurdu.
    /// </summary>
    [Fact]
    public void MonthlyFixed_IsDividedByWorkedDays()
    {
        var result = Run(
            Component(
                calculationType: CompensationCalculationType.MonthlyFixed,
                amount: 6_000m),
            workedDays: 20);

        Assert.Equal(300m, result.MealCost);
    }

    /// <summary>
    /// Aylık kalemin TAMAMI projelere dağılır: günlük pay × çalışılan
    /// gün = kalemin kendisi.
    /// </summary>
    [Fact]
    public void MonthlyFixed_FullyDistributesAcrossWorkedDays()
    {
        const int workedDays = 24;

        var daily = Run(
            Component(
                calculationType: CompensationCalculationType.MonthlyFixed,
                amount: 4_800m),
            workedDays: workedDays);

        Assert.Equal(4_800m, daily.MealCost * workedDays);
    }

    [Fact]
    public void Hourly_UsesTheDayHours()
    {
        var result = Run(
            Component(
                calculationType: CompensationCalculationType.Hourly,
                amount: 12.5m),
            dayHours: 9m);

        Assert.Equal(112.50m, result.MealCost);
    }

    [Fact]
    public void Percentage_IsOfMonthlyGrossDividedByWorkedDays()
    {
        var result = Run(
            Component(
                calculationType: CompensationCalculationType.Percentage,
                amount: 10m),
            workedDays: 20);

        // 60.000 × %10 = 6.000 → 20 güne bölünür
        Assert.Equal(300m, result.MealCost);
    }

    /// <summary>
    /// Tek seferlik kalem yalnızca yürürlüğe girdiği ayın günlerine
    /// yayılır; sonraki ayların maliyetine girmez.
    /// </summary>
    [Fact]
    public void OneTime_SpreadsOnlyOverItsOwnMonth()
    {
        var inMonth = Run(
            Component(
                calculationType: CompensationCalculationType.OneTime,
                amount: 6_000m,
                start: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                name: "İkramiye"),
            workedDays: 20);

        var otherMonth = Run(
            Component(
                calculationType: CompensationCalculationType.OneTime,
                amount: 6_000m,
                start: new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
                name: "İkramiye"),
            workedDays: 20);

        Assert.Equal(300m, inMonth.MealCost);
        Assert.Equal(0m, otherMonth.MealCost);
    }

    // ---------------- Kapsam ----------------

    /// <summary>Proje maliyetine dâhil değilse hiç girmez.</summary>
    [Fact]
    public void NotIncludedInProjectCost_IsSkipped()
    {
        var result = Run(Component(includeInProjectCost: false));

        Assert.Equal(0m, result.Total);
    }

    /// <summary>Kaleme proje bağlıysa yalnızca o projenin günlerine yazılır.</summary>
    [Fact]
    public void ProjectBoundComponent_OnlyAppliesToItsProject()
    {
        var component = Component(projectId: ProjectB);

        Assert.Equal(0m, Run(component, projectId: ProjectA).Total);
        Assert.Equal(100m, Run(component, projectId: ProjectB).Total);
    }

    [Fact]
    public void ComponentOutsideEffectiveRange_IsSkipped()
    {
        var result = Run(Component(
            end: new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(0m, result.Total);
    }

    /// <summary>
    /// Ücret üreten gün yoksa bölme yapılamaz: kalem yazılmaz ama
    /// puantaj ücreti hakediş maliyetinde kalır.
    /// </summary>
    [Fact]
    public void NoWorkedDays_SkipsComponentsButKeepsWage()
    {
        var result = Run(
            Component(calculationType: CompensationCalculationType.MonthlyFixed,
                amount: 6_000m),
            workedDays: 0);

        Assert.Equal(0m, result.Total);
        Assert.Equal(DayEarnings, result.ProgressPaymentCost);
    }

    // ---------------- Hakediş maliyeti ----------------

    /// <summary>
    /// Puantaj ücreti hakediş maliyetine her zaman girer: yapılan işin
    /// kendisidir, bayrağa bağlı değildir.
    /// </summary>
    [Fact]
    public void Wage_AlwaysEntersProgressPaymentCost()
    {
        var result = Run(Component(includeInProgressPaymentCost: false));

        Assert.Equal(DayEarnings, result.ProgressPaymentCost);
    }

    /// <summary>
    /// "Hakediş maliyetine dâhil" işaretlenmemiş kalem proje maliyetine
    /// girer ama hakediş kârını düşürmez: şirketin üstünde kalır.
    /// </summary>
    [Fact]
    public void UnflaggedComponent_CountsForProjectButNotProgressPayment()
    {
        var result = Run(Component(includeInProgressPaymentCost: false));

        Assert.Equal(100m, result.MealCost);
        Assert.Equal(DayEarnings, result.ProgressPaymentCost);
    }

    [Fact]
    public void FlaggedComponent_EntersProgressPaymentCost()
    {
        var result = Run(Component(includeInProgressPaymentCost: true));

        Assert.Equal(DayEarnings + 100m, result.ProgressPaymentCost);
        Assert.Equal(0m, result.ProgressPaymentCompensationCost);
    }

    /// <summary>
    /// Hakediş maliyetine giren elden ödeme ayrıca izlenir ki hakediş
    /// kârı da yetkisiz kullanıcıya maskelenebilsin.
    /// </summary>
    [Fact]
    public void CashInsideProgressPayment_IsTrackedSeparately()
    {
        var result = Run(Component(
            paymentMethod: CompensationPaymentMethod.Cash,
            includeInProgressPaymentCost: true,
            name: "Elden"));

        Assert.Equal(DayEarnings + 100m, result.ProgressPaymentCost);
        Assert.Equal(100m, result.ProgressPaymentCompensationCost);
    }

    // ---------------- Toplama ----------------

    [Fact]
    public void MultipleComponents_AreBucketedIndependently()
    {
        var result = ProjectLaborCostAllocator.Allocate(
            new[]
            {
                Component(componentType: CompensationComponentType.Meal, amount: 100m),
                Component(componentType: CompensationComponentType.Accommodation,
                    amount: 250m, name: "Konaklama"),
                Component(componentType: CompensationComponentType.Travel,
                    amount: 75m, includeInProgressPaymentCost: true, name: "Servis"),
                Component(componentType: CompensationComponentType.Bonus,
                    paymentMethod: CompensationPaymentMethod.Cash,
                    amount: 500m, name: "Elden Prim")
            },
            ProjectA, WorkDate, 20, 8m, DayEarnings, MonthlyGross);

        Assert.Equal(100m, result.MealCost);
        Assert.Equal(250m, result.AccommodationCost);
        Assert.Equal(75m, result.ShuttleCost);
        Assert.Equal(500m, result.CompensationCost);
        Assert.Equal(0m, result.OtherCost);
        Assert.Equal(925m, result.Total);

        // Yalnız servis hakediş maliyetine işaretli.
        Assert.Equal(DayEarnings + 75m, result.ProgressPaymentCost);
    }

    [Fact]
    public void NoComponents_ReturnsWageOnly()
    {
        var result = ProjectLaborCostAllocator.Allocate(
            Array.Empty<CompensationComponentInput>(),
            ProjectA, WorkDate, 20, 8m, DayEarnings, MonthlyGross);

        Assert.Equal(0m, result.Total);
        Assert.Equal(DayEarnings, result.ProgressPaymentCost);
    }
}
