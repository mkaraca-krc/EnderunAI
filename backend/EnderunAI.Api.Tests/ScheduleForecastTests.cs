using EnderunAI.Api.Services.Schedule;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Tahmini bitiş (G1).
///
/// Referans aktivite: 2026-03-02 pazartesi → 2026-03-07 cumartesi,
/// 6 iş günü. "Bugün" 2026-03-04 çarşamba: planın yarısı geçmiş,
/// yani beklenen ilerleme %50.
///
/// Korunan fikir: fiili gecikme PLANI DEĞİŞTİRMEZ. Plan sabittir,
/// tahmin ondan ayrı çıkar. Planı gecikmeye göre güncelleyen bir
/// sistemde hiçbir zaman geç kalınmış olmaz.
/// </summary>
public sealed class ScheduleForecastTests
{
    private static readonly ScheduleCalendar Calendar = ScheduleCalendar.Default;

    private static readonly DateOnly Start = new(2026, 3, 2);
    private static readonly DateOnly Today = new(2026, 3, 4);
    private static readonly DateOnly PlannedFinish = new(2026, 3, 7);

    private static readonly Guid A = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    private static ActivityForecastInput Input(
        decimal progress, int float_ = 0, Guid? id = null) =>
        new(id ?? A, "Kolon kablo", Start, PlannedFinish, progress, float_);

    // ---------- Beklenen ilerleme ----------

    /// <summary>
    /// Beklenen yüzde, geçen sürenin toplam süreye oranıdır. "Gerçek &lt;
    /// planlanan ise gecikme" kuralı buna dayanıyor.
    /// </summary>
    [Fact]
    public void ExpectedRate_IsElapsedOverDuration()
    {
        var result = ScheduleForecastCalculator.ForActivity(
            Calendar, Input(progress: 60m), Today);

        Assert.Equal(50m, result.ExpectedRate);
    }

    [Fact]
    public void AheadOfPlan_IsNotBehind()
    {
        var result = ScheduleForecastCalculator.ForActivity(
            Calendar, Input(progress: 60m), Today);

        Assert.False(result.IsBehind);
        Assert.Equal(new DateOnly(2026, 3, 6), result.ForecastFinish);
        Assert.Equal(0, result.SlipWorkDays);
    }

    // ---------- Gecikme ----------

    /// <summary>
    /// 3 günde %30 → günde %10. Kalan %70, 7 iş günü demek; cumartesiden
    /// sonra pazar atlanır ve tahmin 12 Mart perşembeye düşer.
    /// </summary>
    [Fact]
    public void BehindSchedule_ForecastsALaterFinish()
    {
        var result = ScheduleForecastCalculator.ForActivity(
            Calendar, Input(progress: 30m), Today);

        Assert.True(result.IsBehind);
        Assert.Equal(new DateOnly(2026, 3, 12), result.ForecastFinish);
        Assert.Equal(4, result.SlipWorkDays);
        Assert.Equal(4, result.ProjectImpactWorkDays);
    }

    /// <summary>
    /// Bolluk içinde kalan gecikme proje bitişini ötelemez. Ötelermiş
    /// gibi göstermek her küçük sapmayı alarma çevirirdi.
    /// </summary>
    [Fact]
    public void SlipWithinFloat_DoesNotAffectTheProject()
    {
        var result = ScheduleForecastCalculator.ForActivity(
            Calendar, Input(progress: 30m, float_: 6), Today);

        Assert.Equal(4, result.SlipWorkDays);
        Assert.Equal(0, result.ProjectImpactWorkDays);
    }

    [Fact]
    public void SlipBeyondFloat_AffectsTheProjectByTheRemainder()
    {
        var result = ScheduleForecastCalculator.ForActivity(
            Calendar, Input(progress: 30m, float_: 1), Today);

        Assert.Equal(3, result.ProjectImpactWorkDays);
    }

    // ---------- Sınır durumlar ----------

    /// <summary>
    /// Hiç ilerleme yoksa hız çıkarılamaz; tahmin "en erken bugün
    /// başlarsa" tabanıdır ve notu bunu söyler.
    /// </summary>
    [Fact]
    public void NoProgressAfterStart_FallsBackToAnEarliestPossibleFinish()
    {
        var result = ScheduleForecastCalculator.ForActivity(
            Calendar, Input(progress: 0m), Today);

        Assert.Equal(new DateOnly(2026, 3, 10), result.ForecastFinish);
        Assert.Equal(2, result.SlipWorkDays);
        Assert.Contains("hiç ilerleme", result.Note!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NotStartedYet_HasNoSlip()
    {
        var result = ScheduleForecastCalculator.ForActivity(
            Calendar, Input(progress: 0m), new DateOnly(2026, 2, 25));

        Assert.Equal(PlannedFinish, result.ForecastFinish);
        Assert.Equal(0, result.SlipWorkDays);
        Assert.Equal("Henüz başlamadı.", result.Note);
    }

    /// <summary>
    /// Tamamlanan aktivite için tahmin ÜRETİLMEZ: bitiş tarihi zaten
    /// geçmişte ve elimizde gerçek bitiş tarihi yok. Uydurulmuş bir
    /// tarih, tarih olmamasından kötüdür.
    /// </summary>
    [Fact]
    public void Completed_ProducesNoForecast()
    {
        var result = ScheduleForecastCalculator.ForActivity(
            Calendar, Input(progress: 100m), Today);

        Assert.True(result.IsCompleted);
        Assert.Null(result.ForecastFinish);
        Assert.Equal(0, result.SlipWorkDays);
        Assert.False(result.IsBehind);
    }

    [Fact]
    public void ProgressAboveHundred_IsClampedNotCounted()
    {
        var result = ScheduleForecastCalculator.ForActivity(
            Calendar, Input(progress: 140m), Today);

        Assert.Equal(100m, result.ProgressRate);
        Assert.True(result.IsCompleted);
    }

    [Fact]
    public void NegativeProgress_IsTreatedAsZero()
    {
        var result = ScheduleForecastCalculator.ForActivity(
            Calendar, Input(progress: -20m), Today);

        Assert.Equal(0m, result.ProgressRate);
    }

    /// <summary>
    /// Hız neredeyse sıfırken tahmin yüzyıllara çıkar; anlamsız bir
    /// tarih yerine sınır ve açıklama verilir.
    /// </summary>
    [Fact]
    public void NearZeroSpeed_HitsTheForecastCeilingWithANote()
    {
        var result = ScheduleForecastCalculator.ForActivity(
            Calendar, Input(progress: 0.01m), Today);

        Assert.NotNull(result.Note);
        Assert.Contains("üst sınır", result.Note!);
    }

    // ---------- Proje geneli ----------

    /// <summary>
    /// Projenin gecikmesi, bolluğunu aşan aktivitelerin en büyüğüdür.
    /// </summary>
    [Fact]
    public void ProjectDelay_IsDrivenByTheWorstActivityBeyondItsFloat()
    {
        var forecast = ScheduleForecastCalculator.ForProject(
            Calendar,
            [
                Input(progress: 30m, float_: 0, id: A),
                Input(progress: 60m, float_: 0, id: B)
            ],
            PlannedFinish,
            Today);

        Assert.Equal(4, forecast.DelayWorkDays);
        Assert.Equal(new DateOnly(2026, 3, 12), forecast.ForecastFinish);
        Assert.Equal(new[] { A }, forecast.DrivingActivityIds);
    }

    [Fact]
    public void ProjectOnTrack_HasNoDelayAndNoDrivingActivity()
    {
        var forecast = ScheduleForecastCalculator.ForProject(
            Calendar,
            [Input(progress: 60m, float_: 0, id: A)],
            PlannedFinish,
            Today);

        Assert.Equal(0, forecast.DelayWorkDays);
        Assert.Equal(PlannedFinish, forecast.ForecastFinish);
        Assert.Empty(forecast.DrivingActivityIds);
    }

    [Fact]
    public void EmptyProject_HasNoForecast()
    {
        var forecast = ScheduleForecastCalculator.ForProject(
            Calendar, [], PlannedFinish, Today);

        Assert.Null(forecast.ForecastFinish);
        Assert.Equal(0, forecast.DelayWorkDays);
    }
}
