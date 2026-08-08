using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Schedule;

namespace EnderunAI.Api.Services.HumanResources;

/// <param name="IsWorkDay">Çalışma haftasına göre çalışılan gün mü
/// (tatil olup olmadığından bağımsız).</param>
/// <param name="SuggestedStatus">Cetvel açıldığında kutuya konan
/// varsayılan; kullanıcı istisnaları düzeltir.</param>
public sealed record AttendanceSheetDay(
    DateOnly Date,
    bool IsWorkDay,
    bool IsHoliday,
    bool IsHalfDayHoliday,
    string? HolidayName,
    int SuggestedStatus,
    string SuggestedStatusName,
    decimal SuggestedNormalHours);

/// <summary>
/// Aylık puantaj cetvelinin varsayılan doldurması.
///
/// Saf ve veritabanısız.
///
/// Onaylanan karar: cetvel TAKVİMDEN DOLU gelir. 79 kişi × 26 günü elle
/// doldurmak, puantajın hiç tutulmamasının asıl nedeni. Kullanıcı
/// yalnızca istisnaları (izin, devamsızlık, mesai) düzeltir.
///
/// Üç kural, bu sırayla:
///   1. Tam gün resmî tatil → Resmi Tatil, sıfır saat (ücrete esas gün
///      sayılır ama çalışılmaz).
///   2. Yarım gün tatil (arife) → Yarım Gün, günlük saatin yarısı.
///   3. Çalışma haftasında olmayan gün → Hafta Tatili, sıfır saat.
///   4. Kalan günler → Çalıştı, günlük çalışma saati.
///
/// Tatil, hafta tatilinden ÖNCE geliyor: pazara denk gelen resmî tatil
/// yine resmî tatildir ve ikisi karıştırılırsa ücrete esas gün sayısı
/// bozulur.
/// </summary>
public static class AttendanceSheetGenerator
{
    public static IReadOnlyList<AttendanceSheetDay> Build(
        int year,
        int month,
        WorkWeekDays workWeek,
        IReadOnlyDictionary<DateOnly, (string Name, bool IsHalfDay)> holidays,
        decimal dailyWorkHours)
    {
        if (month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month), "Geçersiz ay.");

        if (year is < 2000 or > 2100)
            throw new ArgumentOutOfRangeException(nameof(year), "Geçersiz yıl.");

        var calendar = new ScheduleCalendar(
            workWeek == WorkWeekDays.None ? WorkWeekDays.MondayToSaturday : workWeek);

        var days = new List<AttendanceSheetDay>(DateTime.DaysInMonth(year, month));

        for (var day = 1; day <= DateTime.DaysInMonth(year, month); day++)
        {
            var date = new DateOnly(year, month, day);
            var isWorkDay = calendar.IsWorkDay(date);

            holidays.TryGetValue(date, out var holiday);
            var isHoliday = holiday.Name is not null;

            var (status, hours) = Resolve(
                isWorkDay, isHoliday, holiday.IsHalfDay, dailyWorkHours);

            days.Add(new AttendanceSheetDay(
                Date: date,
                IsWorkDay: isWorkDay,
                IsHoliday: isHoliday,
                IsHalfDayHoliday: isHoliday && holiday.IsHalfDay,
                HolidayName: holiday.Name,
                SuggestedStatus: (int)status,
                SuggestedStatusName: StatusName(status),
                SuggestedNormalHours: hours));
        }

        return days;
    }

    private static (AttendanceStatus Status, decimal Hours) Resolve(
        bool isWorkDay, bool isHoliday, bool isHalfDayHoliday, decimal dailyWorkHours)
    {
        if (isHoliday && !isHalfDayHoliday)
            return (AttendanceStatus.PublicHoliday, 0m);

        // Arifede yarım gün çalışılır; çalışma haftasında olmayan bir
        // güne denk gelirse yine hafta tatilidir.
        if (isHoliday && isHalfDayHoliday)
        {
            return isWorkDay
                ? (AttendanceStatus.HalfDay, decimal.Round(dailyWorkHours / 2m, 2))
                : (AttendanceStatus.WeeklyHoliday, 0m);
        }

        return isWorkDay
            ? (AttendanceStatus.Worked, dailyWorkHours)
            : (AttendanceStatus.WeeklyHoliday, 0m);
    }

    public static string StatusName(AttendanceStatus status) => status switch
    {
        AttendanceStatus.Absent => "Devamsız",
        AttendanceStatus.Worked => "Çalıştı",
        AttendanceStatus.PaidLeave => "Ücretli izin",
        AttendanceStatus.SickReport => "Raporlu",
        AttendanceStatus.PublicHoliday => "Resmi tatil",
        AttendanceStatus.WeeklyHoliday => "Hafta tatili",
        AttendanceStatus.UnpaidLeave => "Ücretsiz izin",
        AttendanceStatus.ExcusedAbsence => "Mazeretli devamsızlık",
        AttendanceStatus.HalfDay => "Yarım gün",
        AttendanceStatus.RemoteWork => "Uzaktan çalışma",
        _ => "Bilinmiyor"
    };
}
