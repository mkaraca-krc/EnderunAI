namespace EnderunAI.Api.Services.HumanResources;

/// <summary>Kalem türü (HrCompensationComponent.ComponentType).</summary>
public static class CompensationComponentType
{
    public const int Bonus = 0;          // Prim
    public const int Gratuity = 1;       // İkramiye
    public const int Travel = 2;         // Yol Yardımı
    public const int Meal = 3;           // Yemek Yardımı
    public const int Accommodation = 4;  // Konaklama
    public const int ShiftDifference = 5; // Vardiya Farkı
    public const int Compensation = 6;   // Tazminat
    public const int Deduction = 7;      // Kesinti
    public const int Other = 8;          // Diğer
}

/// <summary>Hesap türü (HrCompensationComponent.CalculationType).</summary>
public static class CompensationCalculationType
{
    public const int MonthlyFixed = 0;
    public const int Daily = 1;
    public const int Hourly = 2;
    public const int Percentage = 3;
    public const int OneTime = 4;
}

/// <summary>Ödeme yöntemi (HrCompensationComponent.PaymentMethod).</summary>
public static class CompensationPaymentMethod
{
    public const int Payroll = 0;
    public const int Cash = 1;
    public const int BankTransfer = 2;
    public const int Other = 3;
}

/// <summary>
/// Tek bir ek ücret kaleminin hesaplamaya giren hali. Motorun
/// veritabanına bağlanmaması için varlıktan kopyalanır.
/// </summary>
public sealed record CompensationComponentInput(
    string Name,
    int ComponentType,
    int CalculationType,
    int PaymentMethod,
    decimal Amount,
    bool IsAttendanceBased,
    bool IsInKindBenefit,
    bool IncludeInPayroll,
    bool IncludeInSgkBase,
    bool IncludeInIncomeTaxBase,
    bool IncludeInStampTaxBase,
    DateTime EffectiveStartDate,
    DateTime? EffectiveEndDate);

/// <summary>
/// Nakdî yemek ve yol yardımının günlük istisna tavanları. Her biri
/// ayrı, çünkü SGK ve gelir vergisi tavanları farklı belirleniyor.
/// null = o yıl için tanımlanmamış (istisna uygulanmaz, uyarılır).
/// </summary>
public sealed record CompensationExemptionCaps(
    decimal? MealSgkDaily = null,
    decimal? MealIncomeTaxDaily = null,
    decimal? TravelSgkDaily = null,
    decimal? TravelIncomeTaxDaily = null);

/// <summary>
/// Kalemin hesap dönemindeki karşılığı. Bordro ekranında satır satır
/// gösterilebilsin ve denetlenebilsin diye istisna tutarları da ayrı
/// döner.
/// </summary>
public sealed record CompensationLine(
    string Name,
    int ComponentType,
    decimal Amount,
    decimal SgkExemptAmount,
    decimal IncomeTaxExemptAmount,
    decimal StampTaxExemptAmount);

/// <summary>
/// Bir personelin bir ayına ait tüm kalemlerin toplu sonucu. Kazanç
/// alanları doğrudan HrPayrollRecord alanlarına karşılık gelir.
/// </summary>
public sealed record CompensationResult(
    decimal BonusAmount,
    decimal MealAmount,
    decimal TravelAmount,
    decimal OtherEarningAmount,
    decimal CompensationAmount,
    decimal DeductionAmount,
    decimal SgkExemptEarnings,
    decimal IncomeTaxExemptEarnings,
    decimal StampTaxExemptEarnings,
    IReadOnlyList<CompensationLine> Lines,
    IReadOnlyList<string> Warnings)
{
    public static readonly CompensationResult Empty = new(
        0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m,
        Array.Empty<CompensationLine>(), Array.Empty<string>());

    /// <summary>Bordronun brütüne eklenen toplam kazanç.</summary>
    public decimal TotalEarnings =>
        BonusAmount + MealAmount + TravelAmount +
        OtherEarningAmount + CompensationAmount;
}

/// <summary>
/// Kişiye özel ek ücret kalemlerini (prim, yemek, yol, tazminat,
/// kesinti) bordro tutarlarına ve istisna matrahlarına çevirir.
///
/// Saf ve veritabanısız: bordro motorunun kendisi gibi, hesap kuralları
/// tek yerde ve doğrudan test edilebilir olsun diye.
///
/// Uygulanan kurallar:
/// - Nakit ödeme yöntemi resmî bordroya HİÇ girmez; IncludeInPayroll
///   işaretli olsa bile dışarıda kalır ve uyarı üretir. Elden ödeme
///   sistemin başka yerinde de resmî akıştan ayrı tutuluyor.
/// - Günlük ve saatlik kalemler IsAttendanceBased ise fiilen çalışılan
///   gün/saatle, değilse tam dönemle çarpılır.
/// - Yüzdesel kalem ücret kartındaki brüt maaşın yüzdesidir; toplam
///   kazancın değil — iki yüzdesel kalem birbirini besleyip döngü
///   kurmasın diye.
/// - Tek seferlik kalem yalnızca yürürlüğe girdiği ayda ödenir.
/// - İstisna yalnız yemek ve yol için tavana tabidir: matrah bayrağı
///   açıksa istisna yok; kapalı ve ayni ise tamamı istisna; kapalı ve
///   nakdî ise günlük tavan × çalışılan gün kadarı istisna, aşan kısım
///   matrahta. Tavan tanımlı değilse istisna uygulanmaz ve uyarılır.
/// </summary>
public static class CompensationComponentCalculator
{
    private const int PeriodDays = 30;

    public static CompensationResult Calculate(
        IReadOnlyList<CompensationComponentInput> components,
        int year,
        int month,
        decimal grossSalary,
        decimal workedDays,
        decimal workedHours,
        decimal dailyWorkHours,
        CompensationExemptionCaps caps)
    {
        if (components.Count == 0) return CompensationResult.Empty;

        var periodStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);

        decimal bonus = 0m, meal = 0m, travel = 0m,
            other = 0m, compensation = 0m, deduction = 0m;
        decimal sgkExempt = 0m, incomeTaxExempt = 0m, stampTaxExempt = 0m;

        var lines = new List<CompensationLine>();
        var warnings = new List<string>();

        // Tavanın çarpanı çalışılan gündür: yemek ve yol istisnası
        // günlüktür ve yalnızca fiilen çalışılan güne verilir. Puantaj
        // yoksa tam dönem varsayılır, yoksa istisna hiç oluşmazdı.
        var exemptionDays = workedDays > 0m ? workedDays : PeriodDays;

        foreach (var component in components)
        {
            if (!IsEffective(component, periodStart, periodEnd)) continue;

            if (component.PaymentMethod == CompensationPaymentMethod.Cash)
            {
                warnings.Add(
                    $"\"{component.Name}\" nakit ödeme olarak tanımlı; resmî " +
                    "bordroya, SGK matrahına ve muhasebeye yansıtılmadı.");
                continue;
            }

            if (!component.IncludeInPayroll) continue;

            var amount = Round(ResolveAmount(
                component, periodStart, grossSalary,
                workedDays, workedHours, dailyWorkHours));

            if (amount <= 0m) continue;

            if (component.ComponentType == CompensationComponentType.Deduction)
            {
                deduction += amount;
                lines.Add(new CompensationLine(
                    component.Name, component.ComponentType, amount, 0m, 0m, 0m));
                continue;
            }

            switch (component.ComponentType)
            {
                case CompensationComponentType.Bonus:
                case CompensationComponentType.Gratuity:
                    bonus += amount;
                    break;
                case CompensationComponentType.Meal:
                    meal += amount;
                    break;
                case CompensationComponentType.Travel:
                    travel += amount;
                    break;
                case CompensationComponentType.Compensation:
                    compensation += amount;
                    break;
                default:
                    other += amount;
                    break;
            }

            var sgk = ResolveExemption(
                component, amount, exemptionDays, component.IncludeInSgkBase,
                SgkCapOf(component, caps), "SGK", warnings);

            var incomeTax = ResolveExemption(
                component, amount, exemptionDays, component.IncludeInIncomeTaxBase,
                IncomeTaxCapOf(component, caps), "gelir vergisi", warnings);

            // Damga vergisinin yemek/yol için ayrı bir günlük tavanı
            // yok; bayrak kapalıysa kalem damga matrahına hiç girmez.
            var stampTax = component.IncludeInStampTaxBase ? 0m : amount;

            sgkExempt += sgk;
            incomeTaxExempt += incomeTax;
            stampTaxExempt += stampTax;

            lines.Add(new CompensationLine(
                component.Name, component.ComponentType,
                amount, sgk, incomeTax, stampTax));
        }

        return new CompensationResult(
            BonusAmount: bonus,
            MealAmount: meal,
            TravelAmount: travel,
            OtherEarningAmount: other,
            CompensationAmount: compensation,
            DeductionAmount: deduction,
            SgkExemptEarnings: sgkExempt,
            IncomeTaxExemptEarnings: incomeTaxExempt,
            StampTaxExemptEarnings: stampTaxExempt,
            Lines: lines,
            Warnings: warnings);
    }

    /// <summary>
    /// Kalem dönemde yürürlükte mi. Ay içinde başlayan ya da biten
    /// kalem de sayılır; kısmi ay orantısı ayrı bir iş kuralı olduğu
    /// için burada uygulanmaz.
    /// </summary>
    private static bool IsEffective(
        CompensationComponentInput component,
        DateTime periodStart,
        DateTime periodEnd) =>
        component.EffectiveStartDate.Date <= periodEnd.Date &&
        (!component.EffectiveEndDate.HasValue ||
         component.EffectiveEndDate.Value.Date >= periodStart.Date);

    private static decimal ResolveAmount(
        CompensationComponentInput component,
        DateTime periodStart,
        decimal grossSalary,
        decimal workedDays,
        decimal workedHours,
        decimal dailyWorkHours) =>
        component.CalculationType switch
        {
            CompensationCalculationType.MonthlyFixed => component.Amount,

            CompensationCalculationType.Daily => component.Amount *
                (component.IsAttendanceBased ? workedDays : PeriodDays),

            CompensationCalculationType.Hourly => component.Amount *
                (component.IsAttendanceBased
                    ? workedHours
                    : PeriodDays * dailyWorkHours),

            CompensationCalculationType.Percentage =>
                grossSalary * component.Amount / 100m,

            // Tek seferlik kalem yalnızca yürürlüğe girdiği ayda ödenir;
            // sonraki aylarda tekrar etmez.
            CompensationCalculationType.OneTime =>
                component.EffectiveStartDate.Year == periodStart.Year &&
                component.EffectiveStartDate.Month == periodStart.Month
                    ? component.Amount
                    : 0m,

            _ => 0m
        };

    /// <summary>
    /// Kalemin bir matrahtan istisna tutarı.
    ///
    /// Bayrak açık → matraha tamamen dahil, istisna yok.
    /// Bayrak kapalı + ayni → tamamı istisna, tavan uygulanmaz.
    /// Bayrak kapalı + nakdî + tavanlı tür (yemek/yol) → günlük tavan ×
    /// çalışılan gün kadarı istisna, aşan kısım matrahta.
    /// Tavan tanımsız → istisna uygulanmaz ve uyarılır: eksik parametre
    /// yüzünden sessizce eksik vergi hesaplanmaz.
    /// </summary>
    private static decimal ResolveExemption(
        CompensationComponentInput component,
        decimal amount,
        decimal exemptionDays,
        bool includedInBase,
        decimal? dailyCap,
        string baseLabel,
        List<string> warnings)
    {
        if (includedInBase) return 0m;
        if (component.IsInKindBenefit) return amount;

        if (!IsCapped(component.ComponentType)) return amount;

        if (dailyCap is null)
        {
            warnings.Add(
                $"\"{component.Name}\" için {baseLabel} istisna tavanı bu yıl " +
                "tanımlanmadığından istisna uygulanmadı; kalemin tamamı " +
                "matraha girdi.");
            return 0m;
        }

        return Math.Min(amount, Round(dailyCap.Value * exemptionDays));
    }

    /// <summary>Günlük tavana tabi tek tür: nakdî yemek ve yol.</summary>
    private static bool IsCapped(int componentType) =>
        componentType is CompensationComponentType.Meal
            or CompensationComponentType.Travel;

    private static decimal? SgkCapOf(
        CompensationComponentInput component, CompensationExemptionCaps caps) =>
        component.ComponentType switch
        {
            CompensationComponentType.Meal => caps.MealSgkDaily,
            CompensationComponentType.Travel => caps.TravelSgkDaily,
            _ => null
        };

    private static decimal? IncomeTaxCapOf(
        CompensationComponentInput component, CompensationExemptionCaps caps) =>
        component.ComponentType switch
        {
            CompensationComponentType.Meal => caps.MealIncomeTaxDaily,
            CompensationComponentType.Travel => caps.TravelIncomeTaxDaily,
            _ => null
        };

    private static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
