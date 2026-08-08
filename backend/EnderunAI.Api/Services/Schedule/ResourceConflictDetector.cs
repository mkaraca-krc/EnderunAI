namespace EnderunAI.Api.Services.Schedule;

/// <summary>Atanan kaynağın türü.</summary>
public enum ScheduleResourceKind
{
    Personnel = 0,
    Subcontractor = 1
}

/// <param name="ResourceKey">Aynı kaynağı tanıyan anahtar — personel ya
/// da taşeron sözleşmesi kimliği.</param>
public sealed record ResourceWindow(
    ScheduleResourceKind Kind,
    Guid ResourceId,
    string ResourceName,
    Guid ActivityId,
    string ActivityName,
    DateOnly Start,
    DateOnly Finish,
    bool IsCritical);

/// <param name="BothCritical">İki aktivite de kritik yoldaysa çakışma
/// yalnızca yorgunluk değil, doğrudan proje bitişi riskidir.</param>
public sealed record ResourceConflict(
    ScheduleResourceKind Kind,
    Guid ResourceId,
    string ResourceName,
    Guid FirstActivityId,
    string FirstActivityName,
    Guid SecondActivityId,
    string SecondActivityName,
    DateOnly OverlapStart,
    DateOnly OverlapFinish,
    int OverlapWorkDays,
    bool BothCritical,
    string Severity);

/// <summary>
/// Kaynak çakışması: aynı ekip/personel/taşeron aynı tarihlerde birden
/// fazla aktivitede.
///
/// Saf ve veritabanısız.
///
/// Çakışma HATA DEĞİL, uyarıdır: bir ustabaşı gerçekten iki işi birden
/// yürütebilir. Engellenmesi değil görünür olması gerekiyor — özellikle
/// iki aktivite de kritik yoldaysa, çünkü orada tek kişinin
/// bölünmesi doğrudan proje bitişini öteler.
/// </summary>
public static class ResourceConflictDetector
{
    public static IReadOnlyList<ResourceConflict> Detect(
        ScheduleCalendar calendar,
        IReadOnlyCollection<ResourceWindow> windows)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        var conflicts = new List<ResourceConflict>();

        var groups = windows.GroupBy(x => (x.Kind, x.ResourceId));

        foreach (var group in groups)
        {
            var ordered = group
                .OrderBy(x => x.Start)
                .ThenBy(x => x.Finish)
                .ToList();

            for (var i = 0; i < ordered.Count; i++)
            {
                for (var j = i + 1; j < ordered.Count; j++)
                {
                    var left = ordered[i];
                    var right = ordered[j];

                    // Sıralı olduğu için ardıl, öncülün bitişinden sonra
                    // başlıyorsa geri kalanların hepsi de öyledir.
                    if (right.Start > left.Finish)
                        break;

                    var overlapStart = Max(left.Start, right.Start);
                    var overlapFinish = Min(left.Finish, right.Finish);

                    var days = calendar.WorkDaysBetween(overlapStart, overlapFinish);

                    // Çakışma yalnızca tatile denk geliyorsa gerçek bir
                    // çakışma değildir.
                    if (days <= 0)
                        continue;

                    var bothCritical = left.IsCritical && right.IsCritical;

                    conflicts.Add(new ResourceConflict(
                        Kind: left.Kind,
                        ResourceId: left.ResourceId,
                        ResourceName: left.ResourceName,
                        FirstActivityId: left.ActivityId,
                        FirstActivityName: left.ActivityName,
                        SecondActivityId: right.ActivityId,
                        SecondActivityName: right.ActivityName,
                        OverlapStart: overlapStart,
                        OverlapFinish: overlapFinish,
                        OverlapWorkDays: days,
                        BothCritical: bothCritical,
                        Severity: bothCritical ? "Kritik" : "Uyarı"));
                }
            }
        }

        return conflicts
            .OrderByDescending(x => x.BothCritical)
            .ThenByDescending(x => x.OverlapWorkDays)
            .ThenBy(x => x.ResourceName, StringComparer.CurrentCulture)
            .ToList();
    }

    private static DateOnly Max(DateOnly left, DateOnly right) =>
        left >= right ? left : right;

    private static DateOnly Min(DateOnly left, DateOnly right) =>
        left <= right ? left : right;
}
