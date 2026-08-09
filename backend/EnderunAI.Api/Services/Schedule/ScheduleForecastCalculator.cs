namespace EnderunAI.Api.Services.Schedule;

/// <param name="ProgressRate">Gerçekleşen ilerleme yüzdesi (0–100).
/// Saha verisinden gelir; icmale bağlı olmayan aktivitede elle
/// girilir.</param>
/// <param name="TotalFloatWorkDays">Aktivitenin bolluğu — gecikmenin
/// proje bitişine yansıyıp yansımayacağını bu belirler.</param>
public sealed record ActivityForecastInput(
    Guid Id,
    string Name,
    DateOnly PlannedStart,
    DateOnly PlannedFinish,
    decimal ProgressRate,
    int TotalFloatWorkDays);

/// <param name="ExpectedRate">Plana göre bugün olması gereken yüzde.</param>
/// <param name="SlipWorkDays">Tahmini bitişin planlanan bitişi kaç iş
/// günü aştığı.</param>
/// <param name="ProjectImpactWorkDays">Bolluk düşüldükten sonra proje
/// bitişine yansıyan gecikme.</param>
public sealed record ActivityForecast(
    Guid Id,
    string Name,
    decimal ProgressRate,
    decimal ExpectedRate,
    DateOnly? ForecastFinish,
    int SlipWorkDays,
    int ProjectImpactWorkDays,
    bool IsBehind,
    bool IsCompleted,
    string? Note);

/// <param name="IsActual">
/// Bitiş TAHMİN değil, gerçekleşen. Proje fiilen bittiğinde tahmin
/// üretmenin anlamı kalmaz; gecikme gerçekleşen bitişten okunur.
/// </param>
/// <param name="StartSlipWorkDays">
/// Fiili başlangıcın planlanan başlangıcı kaç iş günü aştığı. Erken
/// başlayan projede sıfır: erken başlamak gecikmeyi silmez, program
/// bunu kendi ilerlemesinden zaten gösterir.
/// </param>
public sealed record ScheduleForecast(
    DateOnly PlannedFinish,
    DateOnly? ForecastFinish,
    int DelayWorkDays,
    IReadOnlyList<Guid> DrivingActivityIds,
    IReadOnlyList<ActivityForecast> Activities,
    bool IsActual = false,
    int StartSlipWorkDays = 0);

/// <summary>
/// Tahmini bitiş.
///
/// KARAR: fiili gecikme planı YENİDEN YAZMAZ. Planlanan tarihler
/// yalnızca kullanıcı elle değiştirdiğinde kayar; gecikme buradan ayrı
/// bir tahmin olarak çıkar. Böylece baseline karşılaştırması bozulmaz —
/// planı gecikmeye göre sürekli güncelleyen bir sistemde hiçbir zaman
/// geç kalınmış olmaz, çünkü plan her seferinde gecikmeye uydurulur.
///
/// Tahmin yöntemi bilinçli olarak basit: geçen sürede kat edilen yüzde
/// bir hız verir, kalan iş o hızla tarihe çevrilir. Hız çıkarılamayan
/// durumda tahmin ÜRETİLMEZ ya da açıkça "en erken" tabanı olduğu
/// yazılır — uydurulmuş bir tarih, tarih olmamasından kötüdür.
/// </summary>
public static class ScheduleForecastCalculator
{
    /// <summary>
    /// Tahmini süre üst sınırı. Yüzde binde birlik ilerlemelerde hız
    /// sıfıra yaklaşır ve tahmin yüzyıllara çıkar; anlamsız bir tarih
    /// yerine sınır ve açıklama veriliyor.
    /// </summary>
    private const int MaxRemainingWorkDays = 2000;

    public static ActivityForecast ForActivity(
        ScheduleCalendar calendar, ActivityForecastInput input, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        var duration = calendar.DurationOf(input.PlannedStart, input.PlannedFinish);
        var elapsed = calendar.WorkDaysBetween(
            calendar.NextWorkDay(input.PlannedStart), asOf);

        var expected = duration <= 0
            ? 0m
            : Clamp(decimal.Round((decimal)elapsed / duration * 100m, 2));

        var progress = Clamp(input.ProgressRate);

        if (progress >= 100m)
        {
            return new ActivityForecast(
                input.Id, input.Name, progress, expected,
                ForecastFinish: null,
                SlipWorkDays: 0,
                ProjectImpactWorkDays: 0,
                IsBehind: false,
                IsCompleted: true,
                Note: "Tamamlandı.");
        }

        DateOnly forecast;
        string? note;

        if (progress <= 0m)
        {
            // Hiç ilerleme yoksa hız çıkarılamaz. Ama iş de duruyor:
            // en iyi ihtimalle bugün başlar ve süresi kadar sürer.
            // Bu bir TABANDIR, tahmin değil — notu bunu söylüyor.
            var start = asOf > input.PlannedStart ? asOf : input.PlannedStart;

            forecast = calendar.FinishFromStart(start, duration);

            note = asOf > calendar.NextWorkDay(input.PlannedStart)
                ? "Başlaması gerekiyordu, hiç ilerleme girilmemiş; en erken " +
                  "bugün başladığı varsayıldı."
                : "Henüz başlamadı.";
        }
        else if (elapsed <= 0)
        {
            // Plan başlangıcından önce ilerleme var: erken başlanmış.
            // Hız hesabı için geçen süre yok, plan tarihine güveniliyor.
            forecast = calendar.NextWorkDay(input.PlannedFinish);
            note = "Planlanan başlangıçtan önce ilerleme var; tahmin plan " +
                   "bitişi kabul edildi.";
        }
        else
        {
            var perDay = progress / elapsed;
            var remaining = (100m - progress) / perDay;
            var remainingDays = (int)Math.Ceiling(remaining);

            note = null;

            if (remainingDays > MaxRemainingWorkDays)
            {
                remainingDays = MaxRemainingWorkDays;
                note = "İlerleme hızı bu tempoda anlamlı bir bitiş tarihi " +
                       "vermiyor; tahmin üst sınıra dayandı.";
            }

            forecast = calendar.AddWorkDays(asOf, remainingDays);
        }

        var slip = Math.Max(
            0, calendar.WorkDayOffset(input.PlannedFinish, forecast));

        return new ActivityForecast(
            Id: input.Id,
            Name: input.Name,
            ProgressRate: progress,
            ExpectedRate: expected,
            ForecastFinish: forecast,
            SlipWorkDays: slip,
            ProjectImpactWorkDays: Math.Max(
                0, slip - Math.Max(0, input.TotalFloatWorkDays)),
            IsBehind: progress < expected,
            IsCompleted: false,
            Note: note);
    }

    /// <summary>
    /// Proje geneli tahmin.
    ///
    /// Proje bitişini öteleyen, gecikmesi BOLLUĞUNU aşan aktivitelerdir;
    /// projenin gecikmesi bunların en büyüğüdür. Bolluk içinde kalan
    /// gecikme proje bitişini kaydırmaz — kaydırıyormuş gibi göstermek
    /// her küçük sapmayı alarma çevirirdi.
    ///
    /// Sınır: bolluklar mevcut plana göre hesaplandı; çok sayıda
    /// aktivite aynı anda kayınca kritik yol değişebilir ve gerçek
    /// gecikme buradan çıkandan büyük olabilir.
    /// </summary>
    public static ScheduleForecast ForProject(
        ScheduleCalendar calendar,
        IReadOnlyCollection<ActivityForecastInput> activities,
        DateOnly plannedFinish,
        DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        var results = activities
            .Select(x => ForActivity(calendar, x, asOf))
            .OrderByDescending(x => x.ProjectImpactWorkDays)
            .ThenBy(x => x.Name, StringComparer.CurrentCulture)
            .ToList();

        var delay = results.Count == 0
            ? 0
            : results.Max(x => x.ProjectImpactWorkDays);

        var driving = results
            .Where(x => x.ProjectImpactWorkDays == delay && delay > 0)
            .Select(x => x.Id)
            .ToList();

        return new ScheduleForecast(
            PlannedFinish: plannedFinish,
            ForecastFinish: results.Count == 0
                ? null
                : calendar.AddWorkDays(calendar.NextWorkDay(plannedFinish), delay),
            DelayWorkDays: delay,
            DrivingActivityIds: driving,
            Activities: results);
    }

    /// <summary>
    /// Gerçekleşen tarihleri tahminin üstüne uygular.
    ///
    /// Proje fiilen BİTTİYSE tahmin düşer: gecikme, gerçekleşen bitişin
    /// termini kaç iş günü aştığıdır. Tahmini bitişi göstermeye devam
    /// etmek, sonucu bilinen bir işi hâlâ öngörüyormuş gibi sunmak
    /// olurdu.
    ///
    /// Proje geç BAŞLADIYSA bu kayma ayrıca raporlanır ama tahminin
    /// üstüne EKLENMEZ: geç başlamanın bitişe yansıyıp yansımadığını
    /// aktivite ilerlemesi zaten gösteriyor; ikisini toplamak aynı
    /// gecikmeyi iki kez saymak olurdu.
    /// </summary>
    public static ScheduleForecast ApplyActuals(
        ScheduleCalendar calendar,
        ScheduleForecast forecast,
        DateOnly? plannedStart,
        DateOnly? actualStart,
        DateOnly? actualFinish,
        DateOnly? deadline)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(forecast);

        // WorkDaysBetween iki ucu da sayar: kayma, planlanan günün
        // ERTESİNDEN itibaren sayılmalı. Planlanan günün kendisini
        // saymak, zamanında başlayan projeye bir gün gecikme yazardı.
        var startSlip = plannedStart is DateOnly planned &&
                        actualStart is DateOnly actual &&
                        actual > planned
            ? calendar.WorkDaysBetween(
                calendar.NextWorkDay(planned.AddDays(1)), actual)
            : 0;

        if (actualFinish is not DateOnly finished)
            return forecast with { StartSlipWorkDays = startSlip };

        // Termin yoksa karşılaştırma tabanı planlanan bitiştir.
        var reference = deadline ?? forecast.PlannedFinish;

        var delay = finished > reference
            ? calendar.WorkDaysBetween(
                calendar.NextWorkDay(reference.AddDays(1)), finished)
            : 0;

        return forecast with
        {
            ForecastFinish = finished,
            DelayWorkDays = delay,
            IsActual = true,
            StartSlipWorkDays = startSlip,
            // Bitmiş projede "gecikmeyi süren aktivite" diye bir şey
            // yok; sorumluyu tahminden okumak yanıltıcı olurdu.
            DrivingActivityIds = Array.Empty<Guid>()
        };
    }

    private static decimal Clamp(decimal rate) =>
        rate < 0m ? 0m : rate > 100m ? 100m : rate;
}
