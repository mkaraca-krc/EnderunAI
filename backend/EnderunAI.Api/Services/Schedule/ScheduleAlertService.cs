using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Schedule;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Schedule;

/// <param name="DaysToDeadline">Termine kalan TAKVİM günü; geçmişse
/// negatif.</param>
/// <param name="CriticalRiskCount">Kritik yolda geride kalan aktivite
/// sayısı. Kritik yoldaki gecikme doğrudan proje bitişini öteler.</param>
/// <param name="Penalty">Tahmini gecikme cezası. Yalnızca tutar görme
/// yetkisi olan çağırana doldurulur; iş programını okuma yetkisi tutar
/// görme yetkisi değildir.</param>
public sealed record ScheduleAlert(
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    Guid ScheduleId,
    DateOnly? Deadline,
    bool HasContractDeadline,
    DateOnly? PlannedFinish,
    DateOnly? ForecastFinish,
    int DelayWorkDays,
    int? DeadlineFloatWorkDays,
    int? DaysToDeadline,
    int CriticalRiskCount,
    bool DeadlineAtRisk,
    decimal ProgressRate,
    DelayPenaltyResult? Penalty);

public interface IScheduleAlertService
{
    /// <summary>
    /// Uyarı üreten iş programları.
    /// </summary>
    /// <param name="includePenalty">Tahmini ceza tutarı eklensin mi.
    /// Çağıran, kullanıcının tutar görme yetkisini kontrol edip
    /// geçirir.</param>
    Task<IReadOnlyList<ScheduleAlert>> GetAsync(
        IReadOnlyCollection<Guid>? projectIds,
        bool includePenalty,
        CancellationToken cancellationToken);

    /// <summary>Tek projenin gecikme cezası tahmini.</summary>
    Task<(DelayPenaltyResult Penalty, int DelayCalendarDays, DateOnly? Deadline,
        DateOnly? ForecastFinish)> EstimatePenaltyAsync(
        Guid projectId, CancellationToken cancellationToken);
}

/// <summary>
/// İş programı uyarıları: geciken projeler, kritik yolda risk, yaklaşan
/// termin ve tahmini gecikme cezası.
///
/// Ceza her zaman TAHMİNİdir: gerçek kesinti işverenin ihtar pratiğine,
/// mücbir sebebe ve süre uzatımına bağlıdır. Kesinmiş gibi gösterilen
/// bir rakam nakit planını yanlış kurar; ekranın "tahmini" demesi
/// bilinçli.
/// </summary>
public sealed class ScheduleAlertService(
    AppDbContext db,
    IProjectScheduleService schedules) : IScheduleAlertService
{
    /// <summary>Yaklaşan termin ufku (takvim günü).</summary>
    public const int DeadlineHorizonDays = 30;

    public async Task<IReadOnlyList<ScheduleAlert>> GetAsync(
        IReadOnlyCollection<Guid>? projectIds,
        bool includePenalty,
        CancellationToken cancellationToken)
    {
        var query = db.ProjectSchedules
            .AsNoTracking()
            .Where(x => x.Status != ProjectScheduleStatus.Archived &&
                        x.Project.Status != ProjectStatus.Completed &&
                        x.Project.Status != ProjectStatus.Cancelled &&
                        !x.Project.IsArchived);

        if (projectIds is not null && projectIds.Count > 0)
            query = query.Where(x => projectIds.Contains(x.ProjectId));

        var candidates = await query
            .Select(x => new { x.Id, x.ProjectId })
            .ToListAsync(cancellationToken);

        var alerts = new List<ScheduleAlert>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var candidate in candidates)
        {
            ProjectScheduleView view;

            try
            {
                view = await schedules.BuildAsync(candidate.Id, cancellationToken);
            }
            catch (ScheduleCycleException)
            {
                // Döngülü program hesaplanamaz. Uyarı listesinin
                // tamamını düşürmek yerine o programı atlıyoruz;
                // döngü zaten kaydedilirken engelleniyor.
                continue;
            }

            if (view.Activities.Count == 0)
                continue;

            var criticalRisk = view.Activities
                .Count(x => x.IsCritical && x.IsBehind && !x.IsCompleted);

            int? daysToDeadline = view.Deadline is DateOnly due
                ? due.DayNumber - today.DayNumber
                : null;

            var deadlineAtRisk =
                view.Deadline is DateOnly limit &&
                (view.DeadlineFloatWorkDays is < 0 ||
                 (view.ForecastFinish is DateOnly forecast && forecast > limit));

            var upcoming =
                daysToDeadline is >= 0 and <= DeadlineHorizonDays;

            if (view.DelayWorkDays == 0 &&
                criticalRisk == 0 &&
                !deadlineAtRisk &&
                !upcoming)
            {
                continue;
            }

            DelayPenaltyResult? penalty = null;

            if (includePenalty)
            {
                var estimate = await EstimatePenaltyAsync(
                    candidate.ProjectId, cancellationToken);

                penalty = estimate.Penalty;
            }

            alerts.Add(new ScheduleAlert(
                ProjectId: view.ProjectId,
                ProjectCode: view.ProjectCode,
                ProjectName: view.ProjectName,
                ScheduleId: view.Id,
                Deadline: view.Deadline,
                HasContractDeadline: view.HasContractDeadline,
                PlannedFinish: view.ProjectFinish,
                ForecastFinish: view.ForecastFinish,
                DelayWorkDays: view.DelayWorkDays,
                DeadlineFloatWorkDays: view.DeadlineFloatWorkDays,
                DaysToDeadline: daysToDeadline,
                CriticalRiskCount: criticalRisk,
                DeadlineAtRisk: deadlineAtRisk,
                ProgressRate: view.ProgressRate,
                Penalty: penalty));
        }

        return alerts
            .OrderByDescending(x => x.DeadlineAtRisk)
            .ThenByDescending(x => x.DelayWorkDays)
            .ThenBy(x => x.DaysToDeadline ?? int.MaxValue)
            .ToList();
    }

    public async Task<(DelayPenaltyResult Penalty, int DelayCalendarDays,
        DateOnly? Deadline, DateOnly? ForecastFinish)> EstimatePenaltyAsync(
        Guid projectId, CancellationToken cancellationToken)
    {
        var project = await db.Projects
            .AsNoTracking()
            .Where(x => x.Id == projectId)
            .Select(x => new
            {
                x.ContractAmount,
                x.DelayPenaltyKind,
                x.DelayPenaltyValue,
                x.DelayPenaltyCapRate,
                Deadline = x.ContractDeadlineDate ?? x.PlannedEndDate
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Proje bulunamadı.");

        var deadline = project.Deadline is DateTime due
            ? DateOnly.FromDateTime(due)
            : (DateOnly?)null;

        var schedule = await db.ProjectSchedules
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId &&
                        x.Status != ProjectScheduleStatus.Archived)
            .Select(x => x.Id)
            .SingleOrDefaultAsync(cancellationToken);

        DateOnly? forecast = null;

        if (schedule != Guid.Empty)
        {
            try
            {
                var view = await schedules.BuildAsync(schedule, cancellationToken);
                forecast = view.ForecastFinish;
            }
            catch (ScheduleCycleException)
            {
                forecast = null;
            }
        }

        // Ceza TAKVİM günü üzerinden yürür: sözleşme "gecikilen her gün
        // için" der, pazarı istisna tutmaz.
        var delayDays = deadline is DateOnly limit && forecast is DateOnly end
            ? Math.Max(0, end.DayNumber - limit.DayNumber)
            : 0;

        var penalty = DelayPenaltyCalculator.Calculate(new DelayPenaltyInput(
            Kind: project.DelayPenaltyKind,
            Value: project.DelayPenaltyValue,
            CapAmount: DelayPenaltyCalculator.CapFromRate(
                project.ContractAmount ?? 0m, project.DelayPenaltyCapRate),
            ContractAmount: project.ContractAmount ?? 0m,
            DelayDays: delayDays));

        return (penalty, delayDays, deadline, forecast);
    }
}
