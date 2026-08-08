using EnderunAI.Api.Services.Schedule;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// İş programı takvimi (G1).
///
/// Referans hafta bilinçli olarak sabit seçildi: 2026-03-02 pazartesi,
/// 2026-03-07 cumartesi, 2026-03-08 pazar, 2026-03-09 pazartesi.
/// Bütün süre hesapları bu haftanın üstünde okunabilir olsun diye.
///
/// Buradaki tek iş kuralı şu: "10 günlük iş" 10 ÇALIŞMA günüdür.
/// Araya giren pazar ve resmî tatil süreyi uzatır, kısaltmaz.
/// </summary>
public sealed class ScheduleCalendarTests
{
    private static readonly DateOnly Monday = new(2026, 3, 2);
    private static readonly DateOnly Wednesday = new(2026, 3, 4);
    private static readonly DateOnly Saturday = new(2026, 3, 7);
    private static readonly DateOnly Sunday = new(2026, 3, 8);
    private static readonly DateOnly NextMonday = new(2026, 3, 9);

    // ---------- Çalışma günü tanımı ----------

    [Fact]
    public void Sunday_IsNotAWorkDay_ByDefault()
    {
        Assert.False(ScheduleCalendar.Default.IsWorkDay(Sunday));
        Assert.True(ScheduleCalendar.Default.IsWorkDay(Saturday));
    }

    [Fact]
    public void Saturday_IsOff_WhenWeekIsMondayToFriday()
    {
        var calendar = new ScheduleCalendar(WorkWeekDays.MondayToFriday);

        Assert.False(calendar.IsWorkDay(Saturday));
        Assert.True(calendar.IsWorkDay(new DateOnly(2026, 3, 6)));
    }

    [Fact]
    public void EveryDayIsWorkDay_InContinuousCalendar()
    {
        Assert.True(ScheduleCalendar.Continuous.IsWorkDay(Sunday));
    }

    [Fact]
    public void Holiday_IsNotAWorkDay()
    {
        var calendar = new ScheduleCalendar(
            WorkWeekDays.MondayToSaturday, [Wednesday]);

        Assert.False(calendar.IsWorkDay(Wednesday));
    }

    /// <summary>Çalışılan günü olmayan takvim kabul edilmez.</summary>
    [Fact]
    public void EmptyWorkWeek_IsRejected()
    {
        Assert.Throws<ArgumentException>(
            () => new ScheduleCalendar(WorkWeekDays.None));
    }

    // ---------- Gün arama ----------

    [Fact]
    public void NextWorkDay_SkipsSundayForward()
    {
        Assert.Equal(NextMonday, ScheduleCalendar.Default.NextWorkDay(Sunday));
        Assert.Equal(Saturday, ScheduleCalendar.Default.NextWorkDay(Saturday));
    }

    [Fact]
    public void PreviousWorkDay_SkipsSundayBackward()
    {
        Assert.Equal(Saturday, ScheduleCalendar.Default.PreviousWorkDay(Sunday));
    }

    [Fact]
    public void AddWorkDays_CountsFromTheDayItselfAsZero()
    {
        var calendar = ScheduleCalendar.Default;

        Assert.Equal(Saturday, calendar.AddWorkDays(Saturday, 0));
        Assert.Equal(NextMonday, calendar.AddWorkDays(Saturday, 1));
        Assert.Equal(Saturday, calendar.AddWorkDays(NextMonday, -1));
    }

    /// <summary>Kaydırma sıfır olduğunda tatil günü çalışma gününe yuvarlanır.</summary>
    [Fact]
    public void AddWorkDays_RoundsHolidayToAWorkDay()
    {
        Assert.Equal(NextMonday, ScheduleCalendar.Default.AddWorkDays(Sunday, 0));
    }

    // ---------- Süre ----------

    /// <summary>
    /// Süre başlangıç gününün KENDİSİNİ sayar: bir günlük iş aynı gün
    /// biter. Aksi halde her aktivite bir gün fazla görünürdü.
    /// </summary>
    [Fact]
    public void OneDayActivity_FinishesOnItsStartDay()
    {
        Assert.Equal(Monday, ScheduleCalendar.Default.FinishFromStart(Monday, 1));
    }

    [Fact]
    public void SixWorkDaysFromMonday_EndOnSaturday()
    {
        Assert.Equal(Saturday, ScheduleCalendar.Default.FinishFromStart(Monday, 6));
    }

    /// <summary>Yedinci gün pazara denk gelir ve pazartesiye taşar.</summary>
    [Fact]
    public void SevenWorkDaysFromMonday_SkipSundayAndEndNextMonday()
    {
        Assert.Equal(NextMonday, ScheduleCalendar.Default.FinishFromStart(Monday, 7));
    }

    [Fact]
    public void SevenCalendarDaysFromMonday_EndOnSunday()
    {
        Assert.Equal(Sunday, ScheduleCalendar.Continuous.FinishFromStart(Monday, 7));
    }

    [Fact]
    public void HolidayInTheMiddle_PushesTheFinish()
    {
        var calendar = new ScheduleCalendar(
            WorkWeekDays.MondayToSaturday, [Wednesday]);

        // Pzt, Sal, (Çar tatil), Per
        Assert.Equal(new DateOnly(2026, 3, 5), calendar.FinishFromStart(Monday, 3));
    }

    [Fact]
    public void StartFromFinish_IsTheInverseOfFinishFromStart()
    {
        var calendar = ScheduleCalendar.Default;

        Assert.Equal(Monday, calendar.StartFromFinish(NextMonday, 7));
        Assert.Equal(Monday, calendar.StartFromFinish(Saturday, 6));
    }

    [Fact]
    public void ZeroDuration_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ScheduleCalendar.Default.FinishFromStart(Monday, 0));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ScheduleCalendar.Default.StartFromFinish(Monday, 0));
    }

    // ---------- Sayım ----------

    [Fact]
    public void WorkDaysBetween_CountsBothEnds()
    {
        var calendar = ScheduleCalendar.Default;

        Assert.Equal(6, calendar.WorkDaysBetween(Monday, Saturday));
        Assert.Equal(7, calendar.WorkDaysBetween(Monday, NextMonday));
        Assert.Equal(1, calendar.WorkDaysBetween(Monday, Monday));
        Assert.Equal(0, calendar.WorkDaysBetween(Saturday, Monday));
    }

    /// <summary>
    /// Bolluk ve gecikme ADIM sayısıyla ölçülür; iki uç dahil sayım
    /// burada bir fazla verirdi.
    /// </summary>
    [Fact]
    public void WorkDayOffset_IsStepCount_NotInclusiveCount()
    {
        var calendar = ScheduleCalendar.Default;

        Assert.Equal(0, calendar.WorkDayOffset(Monday, Monday));
        Assert.Equal(6, calendar.WorkDayOffset(Monday, NextMonday));
        Assert.Equal(-6, calendar.WorkDayOffset(NextMonday, Monday));
    }

    [Fact]
    public void DurationOf_MatchesFinishFromStart()
    {
        var calendar = ScheduleCalendar.Default;

        Assert.Equal(6, calendar.DurationOf(Monday, Saturday));
        Assert.Equal(7, calendar.DurationOf(Monday, NextMonday));
    }

    /// <summary>Bitişi başlangıcından önce olan bozuk kayıt bir gün sayılır.</summary>
    [Fact]
    public void DurationOf_IsAtLeastOneDay()
    {
        Assert.Equal(1, ScheduleCalendar.Default.DurationOf(NextMonday, Monday));
    }
}
