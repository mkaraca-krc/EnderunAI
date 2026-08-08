using EnderunAI.Api.Models;
using EnderunAI.Api.Services.HumanResources;
using EnderunAI.Api.Services.Schedule;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Aylık puantaj cetvelinin varsayılan doldurması (H4).
///
/// Onaylanan karar: cetvel TAKVİMDEN DOLU gelir; kullanıcı yalnızca
/// istisnaları düzeltir. 79 kişi × 26 günü elle doldurmak, puantajın
/// bugüne kadar hiç tutulmamasının asıl nedeni.
///
/// Referans ay: Mart 2026. 1 Mart pazar, 2 Mart pazartesi.
/// </summary>
public sealed class AttendanceSheetGeneratorTests
{
    private const decimal DailyHours = 7.5m;

    private static readonly Dictionary<DateOnly, (string Name, bool IsHalfDay)> None = [];

    private static IReadOnlyList<AttendanceSheetDay> Build(
        WorkWeekDays week = WorkWeekDays.MondayToSaturday,
        Dictionary<DateOnly, (string Name, bool IsHalfDay)>? holidays = null) =>
        AttendanceSheetGenerator.Build(2026, 3, week, holidays ?? None, DailyHours);

    private static AttendanceSheetDay Day(
        IReadOnlyList<AttendanceSheetDay> days, int dayOfMonth) =>
        days.Single(x => x.Date.Day == dayOfMonth);

    // ---------- Gün sayısı ----------

    [Fact]
    public void Sheet_CoversEveryDayOfTheMonth()
    {
        Assert.Equal(31, Build().Count);
    }

    [Fact]
    public void ShortMonth_IsHandled()
    {
        var days = AttendanceSheetGenerator.Build(
            2026, 2, WorkWeekDays.MondayToSaturday, None, DailyHours);

        Assert.Equal(28, days.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void InvalidMonth_IsRejected(int month)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AttendanceSheetGenerator.Build(
                2026, month, WorkWeekDays.MondayToSaturday, None, DailyHours));
    }

    // ---------- Varsayılan doldurma ----------

    /// <summary>Çalışma günü tam gün çalışılmış gelir.</summary>
    [Fact]
    public void WorkingDay_DefaultsToWorkedFullDay()
    {
        var monday = Day(Build(), 2);

        Assert.True(monday.IsWorkDay);
        Assert.Equal((int)AttendanceStatus.Worked, monday.SuggestedStatus);
        Assert.Equal(DailyHours, monday.SuggestedNormalHours);
    }

    /// <summary>Pazar hafta tatili; saat yazılmaz.</summary>
    [Fact]
    public void Sunday_DefaultsToWeeklyHoliday()
    {
        var sunday = Day(Build(), 1);

        Assert.False(sunday.IsWorkDay);
        Assert.Equal((int)AttendanceStatus.WeeklyHoliday, sunday.SuggestedStatus);
        Assert.Equal(0m, sunday.SuggestedNormalHours);
    }

    /// <summary>
    /// Ofis takviminde cumartesi de hafta tatilidir — ofise cumartesi
    /// yazmak gün ve mesai sayısını şişirirdi.
    /// </summary>
    [Fact]
    public void Saturday_IsAWorkDayOnSiteButNotAtTheOffice()
    {
        Assert.Equal(
            (int)AttendanceStatus.Worked,
            Day(Build(WorkWeekDays.MondayToSaturday), 7).SuggestedStatus);

        Assert.Equal(
            (int)AttendanceStatus.WeeklyHoliday,
            Day(Build(WorkWeekDays.MondayToFriday), 7).SuggestedStatus);
    }

    // ---------- Resmî tatil ----------

    [Fact]
    public void FullHoliday_DefaultsToPublicHolidayWithNoHours()
    {
        var days = Build(holidays: new()
        {
            [new DateOnly(2026, 3, 20)] = ("Ramazan Bayramı 1. gün", false)
        });

        var holiday = Day(days, 20);

        Assert.True(holiday.IsHoliday);
        Assert.Equal((int)AttendanceStatus.PublicHoliday, holiday.SuggestedStatus);
        Assert.Equal(0m, holiday.SuggestedNormalHours);
    }

    /// <summary>Arifede yarım gün çalışılır.</summary>
    [Fact]
    public void HalfDayHoliday_DefaultsToHalfDayWithHalfHours()
    {
        var days = Build(holidays: new()
        {
            [new DateOnly(2026, 3, 19)] = ("Ramazan Bayramı arifesi", true)
        });

        var eve = Day(days, 19);

        Assert.True(eve.IsHalfDayHoliday);
        Assert.Equal((int)AttendanceStatus.HalfDay, eve.SuggestedStatus);
        Assert.Equal(3.75m, eve.SuggestedNormalHours);
    }

    /// <summary>
    /// Resmî tatil hafta tatilinden ÖNCE gelir: pazara denk gelen tatil
    /// yine resmî tatildir. İkisi karıştırılırsa ücrete esas gün sayısı
    /// bozulur.
    /// </summary>
    [Fact]
    public void HolidayOnSunday_StaysAPublicHoliday()
    {
        var days = Build(holidays: new()
        {
            [new DateOnly(2026, 3, 1)] = ("Örnek resmî tatil", false)
        });

        var sunday = Day(days, 1);

        Assert.False(sunday.IsWorkDay);
        Assert.Equal((int)AttendanceStatus.PublicHoliday, sunday.SuggestedStatus);
    }

    /// <summary>
    /// Çalışılmayan güne denk gelen ARİFE yarım gün sayılmaz; o gün
    /// zaten çalışılmıyor.
    /// </summary>
    [Fact]
    public void HalfDayHolidayOnANonWorkingDay_IsWeeklyHoliday()
    {
        var days = Build(holidays: new()
        {
            [new DateOnly(2026, 3, 1)] = ("Arife", true)
        });

        var sunday = Day(days, 1);

        Assert.Equal((int)AttendanceStatus.WeeklyHoliday, sunday.SuggestedStatus);
        Assert.Equal(0m, sunday.SuggestedNormalHours);
    }

    [Fact]
    public void HolidayName_IsCarriedToTheSheet()
    {
        var days = Build(holidays: new()
        {
            [new DateOnly(2026, 3, 20)] = ("Ramazan Bayramı 1. gün", false)
        });

        Assert.Equal("Ramazan Bayramı 1. gün", Day(days, 20).HolidayName);
    }

    [Fact]
    public void DaysWithoutHoliday_HaveNoHolidayName()
    {
        Assert.Null(Day(Build(), 2).HolidayName);
    }

    // ---------- Sağlamlık ----------

    /// <summary>
    /// Çalışılan günü olmayan maske süre hesabını çökertirdi; şantiye
    /// varsayılanına düşülüyor.
    /// </summary>
    [Fact]
    public void EmptyWorkWeek_FallsBackInsteadOfBreaking()
    {
        var days = Build(WorkWeekDays.None);

        Assert.Equal((int)AttendanceStatus.Worked, Day(days, 2).SuggestedStatus);
        Assert.Equal((int)AttendanceStatus.WeeklyHoliday, Day(days, 1).SuggestedStatus);
    }

    /// <summary>Takvim günü modunda pazar da çalışılır.</summary>
    [Fact]
    public void AllDaysWeek_MakesSundayAWorkingDay()
    {
        Assert.Equal(
            (int)AttendanceStatus.Worked,
            Day(Build(WorkWeekDays.AllDays), 1).SuggestedStatus);
    }

    [Fact]
    public void DailyHours_FollowTheCompanySetting()
    {
        var days = AttendanceSheetGenerator.Build(
            2026, 3, WorkWeekDays.MondayToSaturday, None, dailyWorkHours: 8m);

        Assert.Equal(8m, Day(days, 2).SuggestedNormalHours);
    }

    [Fact]
    public void EveryDay_CarriesAReadableStatusName()
    {
        Assert.All(
            Build(),
            x => Assert.False(string.IsNullOrWhiteSpace(x.SuggestedStatusName)));
    }
}
