using EnderunAI.Api.Models;
using EnderunAI.Api.Formatting;

namespace EnderunAI.Api.Services.HumanResources;

/// <summary>Puantajdan gelen tek bir gün.</summary>
public sealed record AttendanceDay(
    AttendanceStatus Status,
    decimal OvertimeHours = 0m,
    decimal SundayHours = 0m,
    decimal PublicHolidayHours = 0m);

/// <summary>
/// Ücret kartından okunan tutar ve çarpanlar.
/// </summary>
public sealed record SalaryRates(
    decimal MonthlyGross,
    decimal DailyRate,
    decimal HourlyRate,
    decimal OvertimeMultiplier,
    decimal SundayMultiplier,
    decimal PublicHolidayMultiplier);

public sealed record AttendanceEarnings(
    decimal PaidDays,
    decimal AbsentDays,
    decimal NormalWorkAmount,
    decimal OvertimeAmount,
    decimal SundayWorkAmount,
    decimal PublicHolidayAmount,
    decimal TotalEarnings);

/// <summary>
/// Puantajı ücrete çevirir. Fazla mesai, hafta tatili ve resmi tatil
/// çalışması, ücret kartındaki çarpanlarla (İş Kanunu m.41 %50 zamlı,
/// m.47 tatil çalışması) hesaplanır ve normal ücretin üstüne eklenir.
///
/// Motor deterministik ve veritabanından bağımsız; bordro motorunda
/// olduğu gibi mevzuat hesabıyla birebir test edilebilsin diye.
/// </summary>
public static class AttendanceEarningsCalculator
{
    /// <summary>
    /// Kabul edilebilir en büyük çarpan. Ücret kartlarında çarpan alanına
    /// yüzde girilmesi (1,5 yerine 150) canlıda gerçekleşmiş bir hata;
    /// böyle bir değerle ücret hesaplanmasına izin verilmiyor.
    /// </summary>
    public const decimal MaxMultiplier = 10m;

    /// <summary>
    /// Ücrete esas gün sayısı. Gelmediği günler ödenmez, yarım gün yarım
    /// sayılır; ücretli izin, hafta tatili ve resmi tatil tam sayılır.
    /// </summary>
    public static decimal PaidDayFactor(AttendanceStatus status) => status switch
    {
        AttendanceStatus.Worked => 1m,
        AttendanceStatus.RemoteWork => 1m,
        AttendanceStatus.PaidLeave => 1m,
        AttendanceStatus.PublicHoliday => 1m,
        AttendanceStatus.WeeklyHoliday => 1m,
        AttendanceStatus.HalfDay => 0.5m,
        // Devamsız, raporlu, ücretsiz izin ve mazeretli devamsızlık
        // işveren bordrosunda ücrete esas gün sayılmaz.
        _ => 0m
    };

    public static AttendanceEarnings Calculate(
        SalaryRates rates,
        IReadOnlyCollection<AttendanceDay> days)
    {
        ValidateMultipliers(rates);

        // Hiç puantaj girilmemişse aylık ücret olduğu gibi geçerlidir;
        // eksik gün varsayıp maaş kesmek yanlış olur.
        if (days.Count == 0)
        {
            return new AttendanceEarnings(
                PaidDays: 0m,
                AbsentDays: 0m,
                NormalWorkAmount: Round(rates.MonthlyGross),
                OvertimeAmount: 0m,
                SundayWorkAmount: 0m,
                PublicHolidayAmount: 0m,
                TotalEarnings: Round(rates.MonthlyGross));
        }

        var paidDays = days.Sum(day => PaidDayFactor(day.Status));
        var absentDays = days.Count(day => day.Status == AttendanceStatus.Absent);

        var normal = Round(rates.DailyRate * paidDays);

        var overtime = Round(
            days.Sum(day => day.OvertimeHours)
            * rates.HourlyRate * rates.OvertimeMultiplier);

        var sunday = Round(
            days.Sum(day => day.SundayHours)
            * rates.HourlyRate * rates.SundayMultiplier);

        var publicHoliday = Round(
            days.Sum(day => day.PublicHolidayHours)
            * rates.HourlyRate * rates.PublicHolidayMultiplier);

        return new AttendanceEarnings(
            PaidDays: paidDays,
            AbsentDays: absentDays,
            NormalWorkAmount: normal,
            OvertimeAmount: overtime,
            SundayWorkAmount: sunday,
            PublicHolidayAmount: publicHoliday,
            TotalEarnings: Round(normal + overtime + sunday + publicHoliday));
    }

    /// <summary>
    /// Tek bir günün proje/şantiye maliyeti — işçilik maliyetinin
    /// projeye dağıtılması için kullanılır.
    /// </summary>
    public static AttendanceEarnings CalculateDay(SalaryRates rates, AttendanceDay day) =>
        Calculate(rates, new[] { day });

    private static void ValidateMultipliers(SalaryRates rates)
    {
        foreach (var (name, multiplier) in new (string, decimal)[]
        {
            ("Fazla mesai", rates.OvertimeMultiplier),
            ("Hafta tatili", rates.SundayMultiplier),
            ("Resmi tatil", rates.PublicHolidayMultiplier)
        })
        {
            if (multiplier < 0m)
                throw new InvalidOperationException($"{name} çarpanı negatif olamaz.");

            if (multiplier > MaxMultiplier)
            {
                throw new InvalidOperationException(
                    $"{name} çarpanı {TurkishFormat.Amount(multiplier)} olarak girilmiş; bu değer " +
                    "çarpan değil yüzde olarak girilmiş olabilir " +
                    $"(1,5 = %50 zamlı). Ücret kartını düzeltin.");
            }
        }
    }

    private static decimal Round(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
