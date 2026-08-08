using EnderunAI.Api.Formatting;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Hizir;
using EnderunAI.Api.Services.Hizir.Briefing;

namespace EnderunAI.Api.Services.Schedule;

/// <summary>
/// İş programı brifingi: geciken projeler, kritik yolda risk ve
/// yaklaşan terminler.
///
/// Gecikme cezası tutarı yalnızca TUTAR GÖRME yetkisi olan kullanıcıya
/// yazılır. İş programını okuma yetkisi (schedule.view) neredeyse her
/// rolde var; sözleşme bedeli oradan sızmamalı — cezanın tutarından
/// bedel geri hesaplanabilir.
/// </summary>
public sealed class ScheduleBriefingSource(IScheduleAlertService alerts)
    : IHizirBriefingSource
{
    public string Key => "is_programi";

    public string? RequiredPermission => PermissionCatalog.Keys.ScheduleView;

    public async Task<IReadOnlyList<BriefingItem>> BuildAsync(
        HizirToolContext context, CancellationToken cancellationToken)
    {
        // Kapsam dışı projenin gecikmesi kullanıcıyı ilgilendirmez.
        var projectIds = context.Scope.HasGlobalAccess
            ? null
            : context.Scope.ProjectIds.ToList();

        if (projectIds is { Count: 0 })
            return [];

        var showPenalty = context.Has(PermissionCatalog.Keys.HakedisView);

        var rows = await alerts.GetAsync(projectIds, showPenalty, cancellationToken);

        if (rows.Count == 0)
            return [];

        var items = new List<BriefingItem>();

        foreach (var alert in rows)
        {
            var path = $"/projeler/{alert.ProjectId}/is-programi";

            if (alert.DeadlineAtRisk)
            {
                var detail = alert.ForecastFinish is DateOnly forecast
                    ? $"Bu gidişle tahmini bitiş {forecast:dd.MM.yyyy}, termin " +
                      $"{alert.Deadline:dd.MM.yyyy}."
                    : "Plan bu haliyle bile terminde bitmiyor.";

                if (showPenalty &&
                    alert.Penalty is { Applicable: true } penalty)
                {
                    detail += $" Tahmini gecikme cezası " +
                              $"{TurkishFormat.Amount(penalty.Amount)} TL" +
                              (penalty.CapApplied ? " (tavana dayandı)." : ".");
                }

                items.Add(new BriefingItem(
                    $"{alert.ProjectCode}: termin tehlikede",
                    detail,
                    BriefingSeverity.Critical,
                    path));

                continue;
            }

            if (alert.CriticalRiskCount > 0)
            {
                items.Add(new BriefingItem(
                    $"{alert.ProjectCode}: kritik yolda {alert.CriticalRiskCount} " +
                    "aktivite geride",
                    "Kritik yoldaki gecikme doğrudan proje bitişini öteler.",
                    BriefingSeverity.Warning,
                    path));

                continue;
            }

            if (alert.DelayWorkDays > 0)
            {
                items.Add(new BriefingItem(
                    $"{alert.ProjectCode}: {alert.DelayWorkDays} iş günü gecikme",
                    $"Gerçekleşen ilerleme %{TurkishFormat.Rate(alert.ProgressRate)}. " +
                    "Gecikme bolluğu aşıyor.",
                    BriefingSeverity.Warning,
                    path));

                continue;
            }

            if (alert.DaysToDeadline is int days)
            {
                items.Add(new BriefingItem(
                    $"{alert.ProjectCode}: termine {days} gün",
                    $"Termin {alert.Deadline:dd.MM.yyyy}, gerçekleşen ilerleme " +
                    $"%{TurkishFormat.Rate(alert.ProgressRate)}.",
                    BriefingSeverity.Info,
                    path));
            }
        }

        return items;
    }
}
