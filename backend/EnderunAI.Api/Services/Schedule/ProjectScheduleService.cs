using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Schedule;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Schedule;

/// <param name="BaselineSlipWorkDays">Planın baseline'dan kaç iş günü
/// kaydığı. Baseline yoksa null — kıyas için referans yok demektir.</param>
public sealed record ScheduleActivityView(
    Guid Id,
    Guid? ParentActivityId,
    string Name,
    int Order,
    Guid? SectionId,
    string? SectionName,
    Guid? BoqItemId,
    string? BoqItemCode,
    string? BoqItemDescription,
    DateOnly PlannedStart,
    DateOnly PlannedEnd,
    DateOnly? BaselineStart,
    DateOnly? BaselineEnd,
    int DurationWorkDays,
    int TotalFloatWorkDays,
    bool IsCritical,
    int ShiftedWorkDays,
    int? BaselineSlipWorkDays,
    decimal? ManualProgressRate,
    string? Notes);

public sealed record ScheduleDependencyView(
    Guid Id,
    Guid PredecessorActivityId,
    string PredecessorName,
    Guid SuccessorActivityId,
    string SuccessorName,
    int Type,
    string TypeName,
    int LagWorkDays);

/// <param name="Deadline">Termin. Şimdilik projenin planlanan bitişi;
/// sözleşmeden gelen ayrı termin alanı ileriki fazda bunun yerine
/// geçecek.</param>
public sealed record ProjectScheduleView(
    Guid Id,
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    string Name,
    int Status,
    string StatusName,
    int WorkWeek,
    string WorkWeekName,
    IReadOnlyList<DateOnly> Holidays,
    int BaselineRevisionNumber,
    DateTime? BaselineSetAtUtc,
    DateOnly? ProjectStart,
    DateOnly? ProjectFinish,
    DateOnly? Deadline,
    int? DeadlineFloatWorkDays,
    IReadOnlyList<ScheduleActivityView> Activities,
    IReadOnlyList<ScheduleDependencyView> Dependencies,
    IReadOnlyList<Guid> CriticalActivityIds,
    IReadOnlyList<string> Warnings);

public interface IProjectScheduleService
{
    /// <summary>Projenin yürürlükteki programı; yoksa null.</summary>
    Task<ProjectSchedule?> FindAsync(Guid projectId, CancellationToken cancellationToken);

    Task<ScheduleCalendar> BuildCalendarAsync(
        Guid scheduleId, CancellationToken cancellationToken);

    /// <summary>Hesaplanmış program görünümü.</summary>
    Task<ProjectScheduleView> BuildAsync(
        Guid scheduleId, CancellationToken cancellationToken);

    /// <summary>
    /// Bir bağ eklendiğinde döngü oluşup oluşmayacağını sınar.
    /// Türkçe hata mesajı döner, sorun yoksa null.
    /// </summary>
    Task<string?> ValidateNewDependencyAsync(
        Guid scheduleId,
        Guid predecessorId,
        Guid successorId,
        CancellationToken cancellationToken);
}

/// <summary>
/// İş programını veritabanından toplayıp saf motora veren katman.
/// Hesabın kendisi <see cref="SchedulePlanner"/> içinde; burada sorgu
/// ve eşleme var.
/// </summary>
public sealed class ProjectScheduleService(AppDbContext db) : IProjectScheduleService
{
    public Task<ProjectSchedule?> FindAsync(
        Guid projectId, CancellationToken cancellationToken) =>
        db.ProjectSchedules
            .Where(x => x.ProjectId == projectId &&
                        x.Status != ProjectScheduleStatus.Archived)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<ScheduleCalendar> BuildCalendarAsync(
        Guid scheduleId, CancellationToken cancellationToken)
    {
        var workWeek = await db.ProjectSchedules
            .AsNoTracking()
            .Where(x => x.Id == scheduleId)
            .Select(x => x.WorkWeek)
            .SingleOrDefaultAsync(cancellationToken);

        var holidays = await db.ScheduleHolidays
            .AsNoTracking()
            .Where(x => x.ProjectScheduleId == scheduleId)
            .Select(x => x.Date)
            .ToListAsync(cancellationToken);

        return new ScheduleCalendar(
            workWeek == WorkWeekDays.None ? WorkWeekDays.MondayToSaturday : workWeek,
            holidays);
    }

    public async Task<ProjectScheduleView> BuildAsync(
        Guid scheduleId, CancellationToken cancellationToken)
    {
        var schedule = await db.ProjectSchedules
            .AsNoTracking()
            .Where(x => x.Id == scheduleId)
            .Select(x => new
            {
                x.Id,
                x.ProjectId,
                x.Name,
                x.Status,
                x.WorkWeek,
                x.BaselineRevisionNumber,
                x.BaselineSetAtUtc,
                ProjectCode = x.Project.Code,
                ProjectName = x.Project.Name,
                // Termin: sözleşmeden gelen ayrı alan henüz yok, projenin
                // planlanan bitişi kullanılıyor.
                Deadline = x.Project.PlannedEndDate
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("İş programı bulunamadı.");

        var calendar = await BuildCalendarAsync(scheduleId, cancellationToken);

        var holidays = await db.ScheduleHolidays
            .AsNoTracking()
            .Where(x => x.ProjectScheduleId == scheduleId)
            .OrderBy(x => x.Date)
            .Select(x => x.Date)
            .ToListAsync(cancellationToken);

        var activities = await db.ScheduleActivities
            .AsNoTracking()
            .Where(x => x.ProjectScheduleId == scheduleId)
            .OrderBy(x => x.Order)
            .Select(x => new
            {
                x.Id,
                x.ParentActivityId,
                x.Name,
                x.Order,
                x.ProjectHakedisSectionId,
                SectionName = x.ProjectHakedisSection == null
                    ? null
                    : x.ProjectHakedisSection.Name,
                x.ProjectBoqItemId,
                BoqItemCode = x.ProjectBoqItem == null
                    ? null
                    : x.ProjectBoqItem.PositionCode,
                BoqItemDescription = x.ProjectBoqItem == null
                    ? null
                    : x.ProjectBoqItem.Description,
                x.PlannedStartDate,
                x.PlannedEndDate,
                x.BaselineStartDate,
                x.BaselineEndDate,
                x.ManualProgressRate,
                x.Notes
            })
            .ToListAsync(cancellationToken);

        var dependencies = await db.ScheduleDependencies
            .AsNoTracking()
            .Where(x => x.ProjectScheduleId == scheduleId)
            .Select(x => new
            {
                x.Id,
                x.PredecessorActivityId,
                PredecessorName = x.PredecessorActivity.Name,
                x.SuccessorActivityId,
                SuccessorName = x.SuccessorActivity.Name,
                x.Type,
                x.LagWorkDays
            })
            .ToListAsync(cancellationToken);

        var deadline = schedule.Deadline is DateTime due
            ? DateOnly.FromDateTime(due)
            : (DateOnly?)null;

        var plan = SchedulePlanner.Build(
            calendar,
            activities
                .Select(x => new ScheduleActivityInput(
                    x.Id, x.Name, x.PlannedStartDate, x.PlannedEndDate))
                .ToList(),
            dependencies
                .Select(x => new ScheduleDependencyInput(
                    x.PredecessorActivityId, x.SuccessorActivityId,
                    x.Type, x.LagWorkDays))
                .ToList(),
            deadline);

        var scheduled = plan.Activities.ToDictionary(x => x.Id);
        var views = new List<ScheduleActivityView>(activities.Count);

        foreach (var activity in activities)
        {
            if (!scheduled.TryGetValue(activity.Id, out var computed))
                continue;

            // Baseline sapması: planın kilitli referanstan ne kadar
            // uzaklaştığı. Gerçekleşen gecikme DEĞİLDİR — o ayrı hesap.
            int? baselineSlip = activity.BaselineEndDate is DateOnly baseEnd
                ? calendar.WorkDayOffset(baseEnd, computed.Finish)
                : null;

            views.Add(new ScheduleActivityView(
                Id: activity.Id,
                ParentActivityId: activity.ParentActivityId,
                Name: activity.Name,
                Order: activity.Order,
                SectionId: activity.ProjectHakedisSectionId,
                SectionName: activity.SectionName,
                BoqItemId: activity.ProjectBoqItemId,
                BoqItemCode: activity.BoqItemCode,
                BoqItemDescription: activity.BoqItemDescription,
                PlannedStart: computed.Start,
                PlannedEnd: computed.Finish,
                BaselineStart: activity.BaselineStartDate,
                BaselineEnd: activity.BaselineEndDate,
                DurationWorkDays: computed.DurationWorkDays,
                TotalFloatWorkDays: computed.TotalFloatWorkDays,
                IsCritical: computed.IsCritical,
                ShiftedWorkDays: computed.ShiftedWorkDays,
                BaselineSlipWorkDays: baselineSlip,
                ManualProgressRate: activity.ManualProgressRate,
                Notes: activity.Notes));
        }

        // Sıra: ana çubuklar Order'a göre, alt aktiviteler altlarında.
        var ordered = OrderHierarchically(views);

        return new ProjectScheduleView(
            Id: schedule.Id,
            ProjectId: schedule.ProjectId,
            ProjectCode: schedule.ProjectCode,
            ProjectName: schedule.ProjectName,
            Name: schedule.Name,
            Status: (int)schedule.Status,
            StatusName: ScheduleLabels.Status(schedule.Status),
            WorkWeek: (int)schedule.WorkWeek,
            WorkWeekName: ScheduleLabels.WorkWeek(schedule.WorkWeek),
            Holidays: holidays,
            BaselineRevisionNumber: schedule.BaselineRevisionNumber,
            BaselineSetAtUtc: schedule.BaselineSetAtUtc,
            ProjectStart: activities.Count == 0 ? null : plan.ProjectStart,
            ProjectFinish: activities.Count == 0 ? null : plan.ProjectFinish,
            Deadline: deadline,
            DeadlineFloatWorkDays: plan.DeadlineFloatWorkDays,
            Activities: ordered,
            Dependencies: dependencies
                .Select(x => new ScheduleDependencyView(
                    x.Id,
                    x.PredecessorActivityId,
                    x.PredecessorName,
                    x.SuccessorActivityId,
                    x.SuccessorName,
                    (int)x.Type,
                    ScheduleLabels.Dependency(x.Type),
                    x.LagWorkDays))
                .ToList(),
            CriticalActivityIds: plan.CriticalActivityIds,
            Warnings: plan.Warnings);
    }

    public async Task<string?> ValidateNewDependencyAsync(
        Guid scheduleId,
        Guid predecessorId,
        Guid successorId,
        CancellationToken cancellationToken)
    {
        if (predecessorId == successorId)
            return "Bir aktivite kendisine bağlanamaz.";

        var activities = await db.ScheduleActivities
            .AsNoTracking()
            .Where(x => x.ProjectScheduleId == scheduleId)
            .Select(x => new { x.Id, x.Name, x.PlannedStartDate, x.PlannedEndDate })
            .ToListAsync(cancellationToken);

        if (activities.All(x => x.Id != predecessorId) ||
            activities.All(x => x.Id != successorId))
        {
            return "Bağlanacak aktivite bu iş programında bulunamadı.";
        }

        var existing = await db.ScheduleDependencies
            .AsNoTracking()
            .Where(x => x.ProjectScheduleId == scheduleId)
            .Select(x => new { x.PredecessorActivityId, x.SuccessorActivityId })
            .ToListAsync(cancellationToken);

        if (existing.Any(x => x.PredecessorActivityId == predecessorId &&
                              x.SuccessorActivityId == successorId))
        {
            return "Bu iki aktivite arasında zaten bir bağ var.";
        }

        var inputs = activities
            .Select(x => new ScheduleActivityInput(
                x.Id, x.Name, x.PlannedStartDate, x.PlannedEndDate))
            .ToList();

        var links = existing
            .Select(x => new ScheduleDependencyInput(
                x.PredecessorActivityId, x.SuccessorActivityId))
            .ToList();

        links.Add(new ScheduleDependencyInput(predecessorId, successorId));

        return SchedulePlanner.FindCycle(inputs, links);
    }

    /// <summary>
    /// Ana çubuk → altındaki alt-aktiviteler sırası. Düz Order sıralaması
    /// alt aktiviteleri ana çubuklarından koparırdı.
    /// </summary>
    private static List<ScheduleActivityView> OrderHierarchically(
        IReadOnlyList<ScheduleActivityView> views)
    {
        var children = views
            .Where(x => x.ParentActivityId is not null)
            .GroupBy(x => x.ParentActivityId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Order).ToList());

        var result = new List<ScheduleActivityView>(views.Count);

        foreach (var parent in views
            .Where(x => x.ParentActivityId is null)
            .OrderBy(x => x.Order))
        {
            result.Add(parent);

            if (children.TryGetValue(parent.Id, out var list))
                result.AddRange(list);
        }

        // Ana çubuğu bulunamayan alt aktivite (bozuk veri) kaybolmasın.
        foreach (var orphan in views.Where(x => !result.Contains(x)))
            result.Add(orphan);

        return result;
    }
}
