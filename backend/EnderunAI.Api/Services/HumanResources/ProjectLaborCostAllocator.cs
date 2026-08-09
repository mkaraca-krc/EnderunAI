namespace EnderunAI.Api.Services.HumanResources;

/// <summary>
/// Bir personelin bir GÜNÜNE düşen ek maliyet kalemleri. Alanlar
/// doğrudan HrProjectLaborCost alanlarına karşılık gelir.
/// </summary>
public sealed record ProjectLaborCostAllocation(
    decimal MealCost,
    decimal AccommodationCost,
    decimal ShuttleCost,
    decimal OtherCost,
    decimal CompensationCost,
    decimal ProgressPaymentCost,
    decimal ProgressPaymentCompensationCost)
{
    public static readonly ProjectLaborCostAllocation Empty =
        new(0m, 0m, 0m, 0m, 0m, 0m, 0m);

    /// <summary>Günün toplam ek maliyeti (puantaj ücreti hariç).</summary>
    public decimal Total =>
        MealCost + AccommodationCost + ShuttleCost + OtherCost + CompensationCost;
}

/// <summary>
/// Kişiye özel ek ücret kalemlerini GÜNLÜK proje işçilik maliyetine
/// dağıtır.
///
/// Neden gerekti: HrProjectLaborCost'un MealCost, AccommodationCost,
/// ShuttleCost ve CompensationCost alanları toplama ve kâr hesabına
/// giriyor ama hiçbir yerde yazılmıyordu. Yemek, konaklama ve servis
/// maliyeti kâra hiç yansımadığı için kâr olduğundan yüksek
/// görünüyordu.
///
/// Dağıtım kuralları:
/// - Aylık sabit, yüzdesel ve tek seferlik kalemler, kişinin O AYKİ
///   fiilen çalışılan gün sayısına bölünür. Sabit 30'a bölmek, 20 gün
///   çalışan birinde kalemin üçte birini hiçbir projeye yazmamak
///   olurdu — düzeltmeye çalıştığımız eksikliğin aynısı.
/// - Günlük kalem güne olduğu gibi, saatlik kalem o günün çalışılan
///   saatiyle yazılır.
/// - Nakit ödenen kalem, türü ne olursa olsun CompensationCost
///   kovasına gider: bu kova ek ödeme yetkisi olmayan kullanıcıdan
///   maskeleniyor. Gerçek maliyettir, ama herkese görünmez.
/// - Yalnızca "proje maliyetine dâhil" işaretli kalemler girer.
///   Kaleme proje seçilmişse yalnızca o projenin günlerine yazılır.
/// - Hakediş kâr hesabına giren kısım ayrıca tutulur: puantaj ücreti
///   her zaman girer, kalemlerden yalnız "hakediş maliyetine dâhil"
///   işaretli olanlar girer.
/// </summary>
public static class ProjectLaborCostAllocator
{
    /// <param name="components">Kişinin dönemde yürürlükteki kalemleri.</param>
    /// <param name="projectId">Günün projesi; kaleme proje bağlıysa eşleşmeli.</param>
    /// <param name="workedDaysInPeriod">
    /// Kişinin dönemde ücret üreten proje günü sayısı. Aylık ve tek
    /// seferlik kalemlerin böleni.
    /// </param>
    /// <param name="dayHours">Günün çalışılan saati (normal + fazla mesai).</param>
    /// <param name="dayEarnings">Günün puantajdan gelen ücreti.</param>
    /// <param name="monthlyGross">Yüzdesel kalemin tabanı: aylık brüt.</param>
    public static ProjectLaborCostAllocation Allocate(
        IReadOnlyList<CompensationComponentInput> components,
        Guid projectId,
        DateTime workDate,
        int workedDaysInPeriod,
        decimal dayHours,
        decimal dayEarnings,
        decimal monthlyGross)
    {
        // Puantaj ücreti hakediş maliyetine her zaman girer: yapılan
        // işin kendisidir, bayrağa bağlı değildir.
        if (components.Count == 0 || workedDaysInPeriod <= 0)
            return ProjectLaborCostAllocation.Empty with
            {
                ProgressPaymentCost = dayEarnings
            };

        decimal meal = 0m, accommodation = 0m, shuttle = 0m,
            other = 0m, cash = 0m;
        decimal progressPayment = dayEarnings, progressPaymentCash = 0m;

        foreach (var component in components)
        {
            if (!component.IncludeInProjectCost) continue;
            if (!IsEffective(component, workDate)) continue;

            // Kaleme proje bağlanmışsa yalnızca o projenin günlerine.
            if (component.ProjectId is Guid bound && bound != projectId)
                continue;

            var amount = Round(ResolveDailyAmount(
                component, workDate, workedDaysInPeriod, dayHours, monthlyGross));

            if (amount <= 0m) continue;

            if (component.PaymentMethod == CompensationPaymentMethod.Cash)
            {
                cash += amount;
            }
            else
            {
                switch (component.ComponentType)
                {
                    case CompensationComponentType.Meal:
                        meal += amount;
                        break;
                    case CompensationComponentType.Accommodation:
                        accommodation += amount;
                        break;
                    case CompensationComponentType.Travel:
                        shuttle += amount;
                        break;
                    default:
                        other += amount;
                        break;
                }
            }

            if (component.IncludeInProgressPaymentCost)
            {
                progressPayment += amount;

                if (component.PaymentMethod == CompensationPaymentMethod.Cash)
                    progressPaymentCash += amount;
            }
        }

        return new ProjectLaborCostAllocation(
            MealCost: meal,
            AccommodationCost: accommodation,
            ShuttleCost: shuttle,
            OtherCost: other,
            CompensationCost: cash,
            ProgressPaymentCost: progressPayment,
            ProgressPaymentCompensationCost: progressPaymentCash);
    }

    private static bool IsEffective(
        CompensationComponentInput component, DateTime workDate) =>
        component.EffectiveStartDate.Date <= workDate.Date &&
        (!component.EffectiveEndDate.HasValue ||
         component.EffectiveEndDate.Value.Date >= workDate.Date);

    /// <summary>
    /// Kalemin o güne düşen tutarı. Kesinti bir maliyet değildir;
    /// dağıtıma hiç girmez.
    /// </summary>
    private static decimal ResolveDailyAmount(
        CompensationComponentInput component,
        DateTime workDate,
        int workedDaysInPeriod,
        decimal dayHours,
        decimal monthlyGross)
    {
        if (component.ComponentType == CompensationComponentType.Deduction)
            return 0m;

        return component.CalculationType switch
        {
            CompensationCalculationType.Daily => component.Amount,

            CompensationCalculationType.Hourly => component.Amount * dayHours,

            CompensationCalculationType.MonthlyFixed =>
                component.Amount / workedDaysInPeriod,

            CompensationCalculationType.Percentage =>
                monthlyGross * component.Amount / 100m / workedDaysInPeriod,

            // Tek seferlik kalem yalnızca yürürlüğe girdiği ayın
            // günlerine yayılır; sonraki aylarda tekrar etmez.
            CompensationCalculationType.OneTime =>
                component.EffectiveStartDate.Year == workDate.Year &&
                component.EffectiveStartDate.Month == workDate.Month
                    ? component.Amount / workedDaysInPeriod
                    : 0m,

            _ => 0m
        };
    }

    private static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
