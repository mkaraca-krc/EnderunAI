using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Schedule;
using EnderunAI.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Schedule;

/// <param name="BaselineSlipWorkDays">Planın baseline'dan kaç iş günü
/// kaydığı. Baseline yoksa null — kıyas için referans yok demektir.</param>
/// <param name="ProgressRate">Gerçekleşen yüzde. Kaynağı
/// <paramref name="ProgressSourceName"/> içinde yazılı; ölçülmüş bir
/// oranla elle girilmiş bir oran aynı görünmemeli.</param>
/// <param name="ExpectedRate">Plana göre bugün olması gereken yüzde.</param>
/// <param name="EmployerRate">İşverenin hakedişte kabul ettiği yüzde;
/// saha ile arasındaki fark devreden iştir.</param>
/// <param name="ProjectImpactWorkDays">Bolluk düşüldükten sonra proje
/// bitişine yansıyan gecikme.</param>
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
    decimal ProgressRate,
    int ProgressSource,
    string ProgressSourceName,
    decimal? EmployerRate,
    decimal ExpectedRate,
    DateOnly? ForecastFinish,
    int SlipWorkDays,
    int ProjectImpactWorkDays,
    bool IsBehind,
    bool IsCompleted,
    string? ForecastNote,
    IReadOnlyList<ScheduleResourceView> Resources,
    string? Notes);

/// <param name="Name">Personelin adı ya da taşeronun cari unvanı.</param>
public sealed record ScheduleResourceView(
    Guid Id,
    int Kind,
    string KindName,
    Guid? PersonnelId,
    Guid? SubcontractorContractId,
    string Name,
    string? Role,
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
/// <param name="HasContractSummary">Projede sözleşme icmali var mı.
/// Yoksa gerçekleşme saha raporundan gelemez ve yüzdeler yalnızca elle
/// girilenlerden ibarettir.</param>
/// <param name="ForecastFinish">Bu gidişle tahmini bitiş. Plan
/// DEĞİŞMEZ; bu ondan ayrı bir hesaptır.</param>
/// <param name="DrivingActivityIds">Proje bitişini öteleyen
/// aktiviteler.</param>
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
    bool HasContractDeadline,
    int? DeadlineFloatWorkDays,
    DateOnly AsOf,
    bool HasContractSummary,
    decimal ProgressRate,
    decimal? EmployerRate,
    DateOnly? ForecastFinish,
    /// <summary>Bitiş tarihi tahmin değil, gerçekleşen mi.</summary>
    bool ForecastIsActual,
    DateOnly? ActualStart,
    DateOnly? ActualFinish,
    /// <summary>Fiili başlangıcın plandan kaç iş günü sonra olduğu.</summary>
    int StartSlipWorkDays,
    int DelayWorkDays,
    IReadOnlyList<Guid> DrivingActivityIds,
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
    /// Kaynak çakışmaları: aynı personel/taşeron çakışan tarihli
    /// aktivitelerde.
    /// </summary>
    Task<IReadOnlyList<ResourceConflict>> GetConflictsAsync(
        Guid scheduleId, CancellationToken cancellationToken);

    /// <summary>
    /// Kullanıcının görebileceği proje kimlikleri; sınırsız erişimde
    /// null.
    ///
    /// <see cref="CurrentDataScopeSnapshot.Apply(IQueryable{Project})"/>
    /// yalnızca şirket/şube/proje kapsamına bakar; ŞANTİYE kapsamlı
    /// kullanıcı (Şantiye Şefi, Formen) orada hiçbir proje görmez.
    /// İş programını sahanın okuması gerektiği için burada şantiyeden
    /// projeye çıkılıyor.
    /// </summary>
    Task<IReadOnlyCollection<Guid>?> ResolveVisibleProjectIdsAsync(
        CurrentDataScopeSnapshot? scope, CancellationToken cancellationToken);

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
public sealed class ProjectScheduleService(
    AppDbContext db,
    Hakedis.IContractSummaryProgressService progress) : IProjectScheduleService
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
                // Termin ÖNCE sözleşmeden gelir. Yoksa planlanan bitiş
                // kullanılır — ama o bizim takvimimizdir ve program
                // düzenlendikçe kayar; sözleşme termini kaymaz.
                Deadline = x.Project.ContractDeadlineDate ?? x.Project.PlannedEndDate,
                HasContractDeadline = x.Project.ContractDeadlineDate != null,
                // Gerçekleşen tarihler: proje fiilen bittiyse gecikme
                // tahminden değil buradan okunur.
                x.Project.PlannedStartDate,
                x.Project.ActualStartDate,
                x.Project.ActualEndDate
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
        var warnings = plan.Warnings.ToList();
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow);

        // --- Gerçekleşen: saha raporundan, icmal üzerinden ---
        var summary = await progress.BuildAsync(
            schedule.ProjectId, cancellationToken);

        var sectionField = summary.Sections
            .Where(x => x.SectionId is not null)
            .ToDictionary(x => x.SectionId!.Value, x => x.FieldRate);

        var sectionEmployer = summary.Sections
            .Where(x => x.SectionId is not null)
            .ToDictionary(x => x.SectionId!.Value, x => x.EmployerRate);

        var itemField = summary.Sections
            .SelectMany(x => x.Items)
            .ToDictionary(x => x.BoqItemId, x => x.FieldRate);

        var itemEmployer = summary.Sections
            .SelectMany(x => x.Items)
            .ToDictionary(x => x.BoqItemId, x => x.EmployerRate);

        var resolved = ScheduleProgressResolver.Resolve(
            activities
                .Select(x => new ScheduleProgressInput(
                    x.Id,
                    x.ParentActivityId,
                    x.ProjectHakedisSectionId,
                    x.ProjectBoqItemId,
                    x.ManualProgressRate,
                    scheduled.TryGetValue(x.Id, out var line)
                        ? line.DurationWorkDays
                        : 1))
                .ToList(),
            sectionField, sectionEmployer, itemField, itemEmployer);

        var forecast = ScheduleForecastCalculator.ForProject(
            calendar,
            activities
                .Where(x => scheduled.ContainsKey(x.Id))
                .Select(x => new ActivityForecastInput(
                    x.Id,
                    x.Name,
                    scheduled[x.Id].Start,
                    scheduled[x.Id].Finish,
                    resolved[x.Id].Rate,
                    scheduled[x.Id].TotalFloatWorkDays))
                .ToList(),
            activities.Count == 0
                ? asOf
                : plan.ProjectFinish,
            asOf);

        forecast = ScheduleForecastCalculator.ApplyActuals(
            calendar,
            forecast,
            plannedStart: ToDateOnly(schedule.PlannedStartDate),
            actualStart: ToDateOnly(schedule.ActualStartDate),
            actualFinish: ToDateOnly(schedule.ActualEndDate),
            deadline: deadline);

        var forecasts = forecast.Activities.ToDictionary(x => x.Id);

        var resources = await LoadResourcesAsync(scheduleId, cancellationToken);

        AddProgressWarnings(
            warnings, summary, activities.Count, sectionField, activities
                .Select(x => (x.Id, x.Name, x.ProjectHakedisSectionId))
                .ToList());

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

            var actual = resolved[activity.Id];
            var line = forecasts[activity.Id];

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
                ProgressRate: actual.Rate,
                ProgressSource: (int)actual.Source,
                ProgressSourceName: actual.SourceName,
                EmployerRate: actual.EmployerRate,
                ExpectedRate: line.ExpectedRate,
                ForecastFinish: line.ForecastFinish,
                SlipWorkDays: line.SlipWorkDays,
                ProjectImpactWorkDays: line.ProjectImpactWorkDays,
                IsBehind: line.IsBehind,
                IsCompleted: line.IsCompleted,
                ForecastNote: line.Note,
                Resources: resources.TryGetValue(activity.Id, out var assigned)
                    ? assigned
                    : [],
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
            HasContractDeadline: schedule.HasContractDeadline,
            DeadlineFloatWorkDays: plan.DeadlineFloatWorkDays,
            AsOf: asOf,
            HasContractSummary: summary.HasContractSummary,
            // Projenin bütünündeki yüzde: icmal varsa ONUN tutar
            // ağırlıklı oranı esastır. Çubukların süre ağırlıklı
            // ortalaması yalnızca icmalsiz projede kullanılıyor —
            // orada başka ölçü yok.
            ProgressRate: summary.HasContractSummary
                ? summary.FieldRate
                : WeightedProgress(ordered),
            EmployerRate: summary.HasContractSummary ? summary.EmployerRate : null,
            ForecastFinish: activities.Count == 0 && !forecast.IsActual
                ? null
                : forecast.ForecastFinish,
            ForecastIsActual: forecast.IsActual,
            ActualStart: ToDateOnly(schedule.ActualStartDate),
            ActualFinish: ToDateOnly(schedule.ActualEndDate),
            StartSlipWorkDays: forecast.StartSlipWorkDays,
            DelayWorkDays: forecast.DelayWorkDays,
            DrivingActivityIds: forecast.DrivingActivityIds,
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
            Warnings: warnings);
    }

    /// <summary>
    /// İcmalsiz projede bütünün yüzdesi: ana çubukların SÜRE ağırlıklı
    /// ortalaması. Alt aktiviteler sayılmaz — zaten ana çubuğun
    /// içindeler, ikinci kez sayılırlardı.
    /// </summary>
    /// <summary>UTC tarih alanını takvim gününe çevirir.</summary>
    private static DateOnly? ToDateOnly(DateTime? value) =>
        value is DateTime date ? DateOnly.FromDateTime(date) : null;

    private static decimal WeightedProgress(
        IReadOnlyList<ScheduleActivityView> activities)
    {
        var top = activities.Where(x => x.ParentActivityId is null).ToList();

        if (top.Count == 0)
            return 0m;

        var totalWeight = top.Sum(x => Math.Max(1, x.DurationWorkDays));

        return decimal.Round(
            top.Sum(x => x.ProgressRate * Math.Max(1, x.DurationWorkDays))
                / totalWeight,
            2);
    }

    /// <summary>
    /// Gerçekleşme neden ölçülemiyor sorusunun cevapları.
    ///
    /// Canlıda bugün hiçbir projede icmal kısmı tanımlı değil ve hiçbir
    /// saha rapor satırı icmale bağlanmamış durumda; ekran boş yüzde
    /// gösterecekse bunun NEDENİNİ de söylemeli, aksi halde hata
    /// sanılır.
    /// </summary>
    private static void AddProgressWarnings(
        List<string> warnings,
        Hakedis.ContractSummaryProgressView summary,
        int activityCount,
        IReadOnlyDictionary<Guid, decimal> sectionRates,
        IReadOnlyList<(Guid Id, string Name, Guid? SectionId)> activities)
    {
        if (activityCount == 0)
            return;

        if (!summary.HasContractSummary)
        {
            warnings.Add(
                "Projede sözleşme icmali tanımlı değil; gerçekleşen ilerleme " +
                "saha raporundan gelemiyor. Yüzdeler yalnızca elle girilen " +
                "değerlerden oluşuyor.");

            return;
        }

        var unmeasured = activities
            .Where(x => x.SectionId is Guid id && !sectionRates.ContainsKey(id))
            .Select(x => x.Name)
            .ToList();

        if (unmeasured.Count > 0)
        {
            warnings.Add(
                $"Şu kısımlarda icmal kalemi yok, ilerlemeleri ölçülemiyor: " +
                $"{string.Join(", ", unmeasured)}.");
        }

        var unsectioned = summary.Sections
            .Where(x => x.SectionId is null)
            .Sum(x => x.Items.Count);

        if (unsectioned > 0)
        {
            warnings.Add(
                $"{unsectioned} icmal kalemi hiçbir kısma bağlı değil; bu " +
                "kalemlerin ilerlemesi iş programına yansımıyor.");
        }
    }

    /// <summary>
    /// Aktivite başına atanmış kaynaklar. Personelin adı ile taşeronun
    /// cari unvanı aynı alanda toplanıyor: ekranda ikisi de "kim
    /// yapıyor" sorusunun cevabı.
    /// </summary>
    private async Task<Dictionary<Guid, List<ScheduleResourceView>>>
        LoadResourcesAsync(Guid scheduleId, CancellationToken cancellationToken)
    {
        var rows = await db.ScheduleResourceAssignments
            .AsNoTracking()
            .Where(x => x.ScheduleActivity.ProjectScheduleId == scheduleId)
            .Select(x => new
            {
                x.Id,
                x.ScheduleActivityId,
                x.Kind,
                x.PersonnelId,
                PersonnelName = x.Personnel == null
                    ? null
                    : x.Personnel.FirstName + " " + x.Personnel.LastName,
                x.SubcontractorContractId,
                SubcontractorName = x.SubcontractorContract == null
                    ? null
                    : x.SubcontractorContract.CurrentAccount.Title,
                x.Role,
                x.Notes
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => x.ScheduleActivityId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => new ScheduleResourceView(
                        x.Id,
                        (int)x.Kind,
                        ScheduleLabels.Resource(x.Kind),
                        x.PersonnelId,
                        x.SubcontractorContractId,
                        (x.Kind == ScheduleResourceKind.Subcontractor
                            ? x.SubcontractorName
                            : x.PersonnelName) ?? "—",
                        x.Role,
                        x.Notes))
                    .OrderBy(x => x.Name, StringComparer.CurrentCulture)
                    .ToList());
    }

    public async Task<IReadOnlyCollection<Guid>?> ResolveVisibleProjectIdsAsync(
        CurrentDataScopeSnapshot? scope, CancellationToken cancellationToken)
    {
        // Kapsam çözülemiyorsa hiçbir şey görünmez. Boş küme ile null
        // arasındaki fark burada kritik: null "sınırsız" demek.
        if (scope is null)
            return Array.Empty<Guid>();

        if (scope.HasGlobalAccess)
            return null;

        var direct = await db.Projects
            .AsNoTracking()
            .Where(x => scope.CompanyIds.Contains(x.CompanyId) ||
                        scope.BranchIds.Contains(x.BranchId) ||
                        scope.ProjectIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var fromSites = scope.SiteIds.Count == 0
            ? []
            : await db.ProjectSites
                .AsNoTracking()
                .Where(x => scope.SiteIds.Contains(x.Id))
                .Select(x => x.ProjectId)
                .ToListAsync(cancellationToken);

        return direct.Concat(fromSites).ToHashSet();
    }

    public async Task<IReadOnlyList<ResourceConflict>> GetConflictsAsync(
        Guid scheduleId, CancellationToken cancellationToken)
    {
        var view = await BuildAsync(scheduleId, cancellationToken);
        var calendar = await BuildCalendarAsync(scheduleId, cancellationToken);

        var windows = view.Activities
            .SelectMany(activity => activity.Resources.Select(resource =>
                new ResourceWindow(
                    Kind: (ScheduleResourceKind)resource.Kind,
                    ResourceId: resource.PersonnelId
                        ?? resource.SubcontractorContractId
                        ?? Guid.Empty,
                    ResourceName: resource.Name,
                    ActivityId: activity.Id,
                    ActivityName: activity.Name,
                    Start: activity.PlannedStart,
                    Finish: activity.PlannedEnd,
                    IsCritical: activity.IsCritical)))
            .Where(x => x.ResourceId != Guid.Empty)
            .ToList();

        return ResourceConflictDetector.Detect(calendar, windows);
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
