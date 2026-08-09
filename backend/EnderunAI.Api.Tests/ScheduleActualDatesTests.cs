using EnderunAI.Api.Services.Schedule;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Gerçekleşen başlangıç/bitiş tarihlerinin gecikme hesabına düşmesi.
///
/// Project.ActualStartDate ve ActualEndDate daha önce hiç
/// yazılmıyordu; iş programı yalnızca planı ve tahmini görüyordu.
/// Proje fiilen bittiğinde bile "tahmini bitiş" gösteriliyordu.
/// </summary>
public sealed class ScheduleActualDatesTests
{
    // Pazar tatil, Pzt-Cmt iş günü.
    private static readonly ScheduleCalendar Calendar =
        new(WorkWeekDays.MondayToSaturday, Array.Empty<DateOnly>());

    private static readonly DateOnly PlannedStart = new(2026, 3, 2);   // Pazartesi
    private static readonly DateOnly PlannedFinish = new(2026, 3, 31);
    private static readonly DateOnly Deadline = new(2026, 3, 31);

    private static ScheduleForecast BaseForecast(
        DateOnly? forecastFinish = null, int delay = 0) =>
        new(
            PlannedFinish: PlannedFinish,
            ForecastFinish: forecastFinish ?? PlannedFinish,
            DelayWorkDays: delay,
            DrivingActivityIds: new[] { Guid.NewGuid() },
            Activities: Array.Empty<ActivityForecast>());

    /// <summary>
    /// Proje terminden sonra bittiyse gecikme, gerçekleşen bitişin
    /// termini kaç İŞ GÜNÜ aştığıdır. 31 Mart Salı → 7 Nisan Salı
    /// arası, pazar hariç 6 iş günü.
    /// </summary>
    [Fact]
    public void ActualFinishAfterDeadline_ProducesDelayFromActual()
    {
        var actualFinish = new DateOnly(2026, 4, 7);

        var result = ScheduleForecastCalculator.ApplyActuals(
            Calendar, BaseForecast(),
            plannedStart: PlannedStart,
            actualStart: PlannedStart,
            actualFinish: actualFinish,
            deadline: Deadline);

        Assert.True(result.IsActual);
        Assert.Equal(actualFinish, result.ForecastFinish);
        Assert.Equal(6, result.DelayWorkDays);
    }

    /// <summary>Termininde ya da erken biten projede gecikme yok.</summary>
    [Theory]
    [InlineData(2026, 3, 31)]
    [InlineData(2026, 3, 20)]
    public void ActualFinishOnOrBeforeDeadline_HasNoDelay(int y, int m, int d)
    {
        var result = ScheduleForecastCalculator.ApplyActuals(
            Calendar, BaseForecast(delay: 12),
            plannedStart: PlannedStart,
            actualStart: PlannedStart,
            actualFinish: new DateOnly(y, m, d),
            deadline: Deadline);

        Assert.True(result.IsActual);
        Assert.Equal(0, result.DelayWorkDays);
    }

    /// <summary>
    /// Gerçekleşen bitiş, tahminin yerini alır. Sonucu bilinen bir işi
    /// hâlâ öngörüyormuş gibi sunmak yanıltıcı olurdu.
    /// </summary>
    [Fact]
    public void ActualFinish_ReplacesForecastAndClearsDrivingActivities()
    {
        var forecast = BaseForecast(
            forecastFinish: new DateOnly(2026, 5, 15), delay: 30);

        var result = ScheduleForecastCalculator.ApplyActuals(
            Calendar, forecast,
            plannedStart: PlannedStart,
            actualStart: PlannedStart,
            actualFinish: new DateOnly(2026, 4, 7),
            deadline: Deadline);

        Assert.Equal(new DateOnly(2026, 4, 7), result.ForecastFinish);
        Assert.Equal(6, result.DelayWorkDays);
        Assert.Empty(result.DrivingActivityIds);
    }

    /// <summary>
    /// Termin yoksa karşılaştırma tabanı planlanan bitiştir; hesap
    /// yine de yapılır.
    /// </summary>
    [Fact]
    public void WithoutDeadline_FallsBackToPlannedFinish()
    {
        var result = ScheduleForecastCalculator.ApplyActuals(
            Calendar, BaseForecast(),
            plannedStart: PlannedStart,
            actualStart: PlannedStart,
            actualFinish: new DateOnly(2026, 4, 7),
            deadline: null);

        Assert.Equal(6, result.DelayWorkDays);
    }

    // ---------------- Başlangıç kayması ----------------

    /// <summary>
    /// Geç başlayan proje kaymayı ayrıca raporlar: 2 Mart Pazartesi →
    /// 9 Mart Pazartesi arası, pazar hariç 6 iş günü.
    /// </summary>
    [Fact]
    public void LateActualStart_IsReportedAsStartSlip()
    {
        var result = ScheduleForecastCalculator.ApplyActuals(
            Calendar, BaseForecast(),
            plannedStart: PlannedStart,
            actualStart: new DateOnly(2026, 3, 9),
            actualFinish: null,
            deadline: Deadline);

        Assert.Equal(6, result.StartSlipWorkDays);
    }

    /// <summary>
    /// Başlangıç kayması tahminin ÜSTÜNE EKLENMEZ: geç başlamanın
    /// bitişe yansıyıp yansımadığını aktivite ilerlemesi zaten
    /// gösteriyor, ikisini toplamak aynı gecikmeyi iki kez saymak
    /// olurdu.
    /// </summary>
    [Fact]
    public void StartSlip_DoesNotInflateForecastDelay()
    {
        var result = ScheduleForecastCalculator.ApplyActuals(
            Calendar, BaseForecast(delay: 4),
            plannedStart: PlannedStart,
            actualStart: new DateOnly(2026, 3, 9),
            actualFinish: null,
            deadline: Deadline);

        Assert.Equal(4, result.DelayWorkDays);
        Assert.False(result.IsActual);
    }

    /// <summary>Erken başlamak gecikmeyi silmez; kayma sıfırdır.</summary>
    [Fact]
    public void EarlyActualStart_HasNoSlip()
    {
        var result = ScheduleForecastCalculator.ApplyActuals(
            Calendar, BaseForecast(delay: 4),
            plannedStart: PlannedStart,
            actualStart: new DateOnly(2026, 2, 20),
            actualFinish: null,
            deadline: Deadline);

        Assert.Equal(0, result.StartSlipWorkDays);
        Assert.Equal(4, result.DelayWorkDays);
    }

    /// <summary>
    /// Gerçekleşen tarih yoksa tahmin hiç değişmiyor: bağlama geriye
    /// dönük fark yaratmadı.
    /// </summary>
    [Fact]
    public void WithoutActuals_ForecastIsUnchanged()
    {
        var forecast = BaseForecast(
            forecastFinish: new DateOnly(2026, 4, 20), delay: 15);

        var result = ScheduleForecastCalculator.ApplyActuals(
            Calendar, forecast,
            plannedStart: PlannedStart,
            actualStart: null,
            actualFinish: null,
            deadline: Deadline);

        Assert.Equal(forecast.ForecastFinish, result.ForecastFinish);
        Assert.Equal(15, result.DelayWorkDays);
        Assert.False(result.IsActual);
        Assert.Equal(0, result.StartSlipWorkDays);
        Assert.Equal(forecast.DrivingActivityIds, result.DrivingActivityIds);
    }
}
