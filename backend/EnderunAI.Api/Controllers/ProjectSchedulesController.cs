using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Schedule;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Services.Schedule;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

/// <param name="SeedFromSections">İcmal kısımlarından ana çubukları
/// otomatik oluştur.</param>
public sealed record CreateProjectScheduleRequest(
    string? Name,
    int? WorkWeek,
    bool SeedFromSections = true,
    string? Notes = null);

public sealed record UpdateProjectScheduleRequest(
    string? Name,
    int? WorkWeek,
    int? Status,
    string? Notes);

public sealed record ScheduleActivityRequest(
    string Name,
    DateOnly PlannedStartDate,
    DateOnly PlannedEndDate,
    Guid? ParentActivityId,
    Guid? ProjectHakedisSectionId,
    Guid? ProjectBoqItemId,
    decimal? ManualProgressRate,
    int? Order,
    string? Notes);

public sealed record ScheduleDependencyRequest(
    Guid PredecessorActivityId,
    Guid SuccessorActivityId,
    int Type,
    int LagWorkDays);

public sealed record SaveBaselineRequest(string? Reason);

/// <param name="ContractDeadlineDate">İşverenin dayattığı termin.
/// Boş bırakılırsa projenin planlanan bitişi termin sayılır.</param>
/// <param name="DelayPenaltyValue">Oransal cezada günlük YÜZDE
/// (binde 1 için 0,1); sabit cezada günlük tutar.</param>
/// <param name="DelayPenaltyCapRate">Ceza tavanı, sözleşme bedelinin
/// yüzdesi (yaygın: 10).</param>
public sealed record UpdateProjectDeadlineRequest(
    DateTime? ContractDeadlineDate,
    int DelayPenaltyKind,
    decimal DelayPenaltyValue,
    decimal? DelayPenaltyCapRate);

public sealed record ScheduleHolidayRequest(DateOnly Date, string? Name);

public sealed record AssignScheduleResourceRequest(
    int Kind,
    Guid? PersonnelId,
    Guid? SubcontractorContractId,
    string? Role,
    string? Notes);

public sealed record ReplaceScheduleHolidaysRequest(
    IReadOnlyCollection<ScheduleHolidayRequest> Holidays);

/// <summary>
/// İş programı (Gantt).
///
/// Aktiviteler icmal KISIMLARINA bağlanır; iş programı ayrı bir iş
/// kalemi listesi tutmaz. Bu denetleyici programın kendisini, çubukları,
/// bağımlılıkları ve baseline'ı yönetir; hesap
/// <see cref="SchedulePlanner"/> içinde.
/// </summary>
[ApiController]
[Authorize]
public sealed class ProjectSchedulesController(
    AppDbContext db,
    IProjectScheduleService schedules,
    IScheduleAlertService alerts) : ControllerBase
{
    // ---------------- Termin, ceza ve uyarılar ----------------

    /// <summary>
    /// Uyarı üreten iş programları: geciken, kritik yolda riskli ve
    /// termini yaklaşan projeler.
    ///
    /// Ceza TUTARI yalnızca hakediş görüntüleme yetkisi olana yazılır.
    /// schedule.view neredeyse her rolde var; ceza tutarından sözleşme
    /// bedeli geri hesaplanabildiği için o kapıdan sızmamalı.
    /// </summary>
    [HttpGet("api/is-programi/uyarilar")]
    [RequirePermission(PermissionCatalog.Keys.ScheduleView)]
    public async Task<IActionResult> Alerts(
        [FromQuery] Guid? projectId,
        [FromServices] ICurrentUserService currentUser,
        [FromServices] IUserAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        var showPenalty = await HasPermissionAsync(
            currentUser, authorization,
            PermissionCatalog.Keys.HakedisView, cancellationToken);

        var rows = await alerts.GetAsync(
            projectId is Guid id ? [id] : null, showPenalty, cancellationToken);

        return Ok(new
        {
            horizonDays = ScheduleAlertService.DeadlineHorizonDays,
            showsPenalty = showPenalty,
            items = rows
        });
    }

    /// <summary>
    /// Projenin gecikme cezası ayarları ve TAHMİNİ ceza.
    ///
    /// Tutar içerdiği için hakediş görüntüleme izniyle korunuyor —
    /// iş programı ekranının geri kalanından farklı bir kapı.
    /// </summary>
    [HttpGet("api/projects/{projectId:guid}/gecikme-cezasi")]
    [RequirePermission(PermissionCatalog.Keys.HakedisView)]
    public async Task<IActionResult> DelayPenalty(
        Guid projectId, CancellationToken cancellationToken)
    {
        var settings = await db.Projects
            .AsNoTracking()
            .Where(x => x.Id == projectId)
            .Select(x => new
            {
                x.ContractAmount,
                x.CurrencyCode,
                x.ContractDeadlineDate,
                x.PlannedEndDate,
                Kind = (int)x.DelayPenaltyKind,
                x.DelayPenaltyValue,
                x.DelayPenaltyCapRate
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (settings is null)
            return NotFound(new { message = "Proje bulunamadı." });

        var estimate = await alerts.EstimatePenaltyAsync(projectId, cancellationToken);

        return Ok(new
        {
            settings.ContractAmount,
            settings.CurrencyCode,
            settings.ContractDeadlineDate,
            settings.PlannedEndDate,
            hasContractDeadline = settings.ContractDeadlineDate is not null,
            delayPenaltyKind = settings.Kind,
            settings.DelayPenaltyValue,
            settings.DelayPenaltyCapRate,
            deadline = estimate.Deadline,
            forecastFinish = estimate.ForecastFinish,
            delayCalendarDays = estimate.DelayCalendarDays,
            penalty = estimate.Penalty,
            // Hesap her zaman tahmindir: gerçek kesinti işverenin ihtar
            // pratiğine, mücbir sebebe ve süre uzatımına bağlıdır.
            disclaimer = "Tahmini tutardır; mücbir sebep ve süre uzatımı " +
                         "hesaba katılmaz."
        });
    }

    [HttpPut("api/projects/{projectId:guid}/termin")]
    [RequirePermission(PermissionCatalog.Keys.ScheduleManage)]
    public async Task<IActionResult> UpdateDeadline(
        Guid projectId,
        UpdateProjectDeadlineRequest request,
        CancellationToken cancellationToken)
    {
        var project = await db.Projects
            .SingleOrDefaultAsync(x => x.Id == projectId, cancellationToken);

        if (project is null)
            return NotFound(new { message = "Proje bulunamadı." });

        if (!Enum.IsDefined(typeof(DelayPenaltyKind), request.DelayPenaltyKind))
            return BadRequest(new { message = "Geçersiz gecikme cezası biçimi." });

        var kind = (DelayPenaltyKind)request.DelayPenaltyKind;

        if (kind != DelayPenaltyKind.None && request.DelayPenaltyValue <= 0m)
        {
            return BadRequest(new
            {
                message = "Gecikme cezası seçildiyse günlük oran veya tutar " +
                          "girilmelidir."
            });
        }

        if (request.DelayPenaltyCapRate is decimal cap && (cap < 0m || cap > 100m))
        {
            return BadRequest(new
            {
                message = "Ceza tavanı sözleşme bedelinin yüzdesidir; 0 ile 100 " +
                          "arasında olmalıdır."
            });
        }

        var deadline = request.ContractDeadlineDate is DateTime value
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : (DateTime?)null;

        if (deadline is DateTime due &&
            project.PlannedStartDate is DateTime start &&
            due < start)
        {
            return BadRequest(new
            {
                message = "Termin, işe başlama tarihinden önce olamaz."
            });
        }

        project.ContractDeadlineDate = deadline;
        project.DelayPenaltyKind = kind;
        project.DelayPenaltyValue = kind == DelayPenaltyKind.None
            ? 0m
            : request.DelayPenaltyValue;
        project.DelayPenaltyCapRate = kind == DelayPenaltyKind.None
            ? null
            : request.DelayPenaltyCapRate;
        project.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Termin ve gecikme cezası kaydedildi." });
    }

    /// <summary>
    /// Oturumdaki kullanıcının belirli bir izne sahip olup olmadığı.
    /// Attribute'lar VEYA mantığıyla birleştiği için ikinci bir izin
    /// koşulu ancak burada zorlanabiliyor.
    /// </summary>
    private static async Task<bool> HasPermissionAsync(
        ICurrentUserService currentUser,
        IUserAuthorizationService authorization,
        string permissionKey,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
            return false;

        var snapshot = await authorization.GetAsync(userId, cancellationToken);

        if (snapshot is null || !snapshot.IsActive)
            return false;

        if (snapshot.RoleNames.Contains("Admin", StringComparer.OrdinalIgnoreCase))
            return true;

        return snapshot.Permissions.Contains(
            permissionKey, StringComparer.OrdinalIgnoreCase);
    }

    // ---------------- Program ----------------

    /// <summary>
    /// Projenin iş programı. Program henüz açılmamışsa 404 yerine
    /// "yok" bilgisi döner: ekranın kullanıcıyı yönlendirebilmesi için
    /// kısım sayısını da taşır — kısım tanımlı değilse iş programı boş
    /// görünür ve bunun nedeni söylenmelidir.
    /// </summary>
    [HttpGet("api/projects/{projectId:guid}/is-programi")]
    [RequirePermission(PermissionCatalog.Keys.ScheduleView)]
    public async Task<IActionResult> Get(
        Guid projectId, CancellationToken cancellationToken)
    {
        var project = await db.Projects
            .AsNoTracking()
            .Where(x => x.Id == projectId)
            .Select(x => new { x.Id, x.Code, x.Name })
            .SingleOrDefaultAsync(cancellationToken);

        if (project is null)
            return NotFound(new { message = "Proje bulunamadı." });

        var schedule = await db.ProjectSchedules
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId &&
                        x.Status != ProjectScheduleStatus.Archived)
            .Select(x => x.Id)
            .SingleOrDefaultAsync(cancellationToken);

        var sectionCount = await db.ProjectHakedisSections
            .CountAsync(x => x.ProjectId == projectId && x.IsActive, cancellationToken);

        if (schedule == Guid.Empty)
        {
            return Ok(new
            {
                hasSchedule = false,
                projectId = project.Id,
                projectCode = project.Code,
                projectName = project.Name,
                sectionCount,
                message = sectionCount == 0
                    ? "Bu projede icmal kısmı tanımlı değil. İş programı " +
                      "kısımlardan doğar — önce İcmal Kısımları ekranından " +
                      "kısımları tanımlayın."
                    : "Bu proje için henüz iş programı açılmamış."
            });
        }

        var view = await schedules.BuildAsync(schedule, cancellationToken);

        return Ok(new { hasSchedule = true, sectionCount, schedule = view });
    }

    [HttpPost("api/projects/{projectId:guid}/is-programi")]
    [RequirePermission(PermissionCatalog.Keys.ScheduleManage)]
    public async Task<IActionResult> Create(
        Guid projectId,
        CreateProjectScheduleRequest request,
        CancellationToken cancellationToken)
    {
        var project = await db.Projects
            .AsNoTracking()
            .Where(x => x.Id == projectId)
            .Select(x => new { x.Id, x.Name, x.PlannedStartDate, x.PlannedEndDate })
            .SingleOrDefaultAsync(cancellationToken);

        if (project is null)
            return NotFound(new { message = "Proje bulunamadı." });

        var exists = await db.ProjectSchedules.AnyAsync(
            x => x.ProjectId == projectId &&
                 x.Status != ProjectScheduleStatus.Archived,
            cancellationToken);

        if (exists)
        {
            return BadRequest(new
            {
                message = "Bu projenin zaten bir iş programı var. İkinci bir " +
                          "program açmak için önce mevcut programı arşivleyin."
            });
        }

        if (!TryReadWorkWeek(request.WorkWeek, out var workWeek, out var problem))
            return BadRequest(new { message = problem });

        var schedule = new ProjectSchedule
        {
            ProjectId = projectId,
            Name = string.IsNullOrWhiteSpace(request.Name)
                ? $"{project.Name} İş Programı"
                : request.Name.Trim(),
            WorkWeek = workWeek,
            Status = ProjectScheduleStatus.Draft,
            Notes = request.Notes?.Trim()
        };

        db.ProjectSchedules.Add(schedule);
        await db.SaveChangesAsync(cancellationToken);

        var seeded = 0;

        if (request.SeedFromSections)
        {
            seeded = await SeedFromSectionsAsync(
                schedule, project.PlannedStartDate, cancellationToken);
        }

        return Ok(new
        {
            id = schedule.Id,
            seededActivityCount = seeded,
            message = seeded > 0
                ? $"İş programı açıldı; {seeded} icmal kısmı ana çubuk olarak eklendi."
                : "İş programı açıldı. Aktivite eklemek için icmal kısımlarını " +
                  "kullanabilir ya da elle çubuk tanımlayabilirsiniz."
        });
    }

    [HttpPut("api/is-programi/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.ScheduleManage)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateProjectScheduleRequest request,
        CancellationToken cancellationToken)
    {
        var schedule = await db.ProjectSchedules
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (schedule is null)
            return NotFound(new { message = "İş programı bulunamadı." });

        if (request.WorkWeek is not null)
        {
            if (!TryReadWorkWeek(request.WorkWeek, out var workWeek, out var problem))
                return BadRequest(new { message = problem });

            schedule.WorkWeek = workWeek;
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
            schedule.Name = request.Name.Trim();

        if (request.Status is int status)
        {
            if (!Enum.IsDefined(typeof(ProjectScheduleStatus), status))
                return BadRequest(new { message = "Geçersiz program durumu." });

            schedule.Status = (ProjectScheduleStatus)status;
        }

        schedule.Notes = request.Notes?.Trim();
        schedule.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "İş programı güncellendi." });
    }

    /// <summary>
    /// İcmal kısımlarından eksik ana çubukları ekler. Zaten bağlı olan
    /// kısım TEKRAR eklenmez — aynı kısmın iki çubuğu, ilerlemeyi iki
    /// kez sayılmış gibi gösterirdi.
    /// </summary>
    [HttpPost("api/is-programi/{id:guid}/kisimlardan-olustur")]
    [RequirePermission(PermissionCatalog.Keys.ScheduleManage)]
    public async Task<IActionResult> SeedFromSections(
        Guid id, CancellationToken cancellationToken)
    {
        var schedule = await db.ProjectSchedules
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (schedule is null)
            return NotFound(new { message = "İş programı bulunamadı." });

        var plannedStart = await db.Projects
            .AsNoTracking()
            .Where(x => x.Id == schedule.ProjectId)
            .Select(x => x.PlannedStartDate)
            .SingleOrDefaultAsync(cancellationToken);

        var added = await SeedFromSectionsAsync(
            schedule, plannedStart, cancellationToken);

        return Ok(new
        {
            addedActivityCount = added,
            message = added == 0
                ? "Eklenecek yeni kısım yok — bütün kısımların çubuğu zaten var."
                : $"{added} kısım ana çubuk olarak eklendi."
        });
    }

    // ---------------- Aktiviteler ----------------

    [HttpPost("api/is-programi/{id:guid}/aktiviteler")]
    [RequirePermission(PermissionCatalog.Keys.ScheduleManage)]
    public async Task<IActionResult> CreateActivity(
        Guid id,
        ScheduleActivityRequest request,
        CancellationToken cancellationToken)
    {
        var schedule = await db.ProjectSchedules
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new { x.Id, x.ProjectId })
            .SingleOrDefaultAsync(cancellationToken);

        if (schedule is null)
            return NotFound(new { message = "İş programı bulunamadı." });

        var problem = await ValidateActivityAsync(
            schedule.Id, schedule.ProjectId, request, null, cancellationToken);

        if (problem is not null)
            return BadRequest(new { message = problem });

        var order = request.Order ?? await NextOrderAsync(
            schedule.Id, request.ParentActivityId, cancellationToken);

        var activity = new ScheduleActivity
        {
            ProjectScheduleId = schedule.Id,
            ParentActivityId = request.ParentActivityId,
            ProjectHakedisSectionId = request.ProjectHakedisSectionId,
            ProjectBoqItemId = request.ProjectBoqItemId,
            Name = request.Name.Trim(),
            Order = order,
            PlannedStartDate = request.PlannedStartDate,
            PlannedEndDate = request.PlannedEndDate,
            ManualProgressRate = request.ManualProgressRate,
            Notes = request.Notes?.Trim()
        };

        db.ScheduleActivities.Add(activity);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { id = activity.Id, message = "Aktivite eklendi." });
    }

    [HttpPut("api/is-programi/aktiviteler/{activityId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.ScheduleManage)]
    public async Task<IActionResult> UpdateActivity(
        Guid activityId,
        ScheduleActivityRequest request,
        CancellationToken cancellationToken)
    {
        var activity = await db.ScheduleActivities
            .SingleOrDefaultAsync(x => x.Id == activityId, cancellationToken);

        if (activity is null)
            return NotFound(new { message = "Aktivite bulunamadı." });

        var projectId = await db.ProjectSchedules
            .AsNoTracking()
            .Where(x => x.Id == activity.ProjectScheduleId)
            .Select(x => x.ProjectId)
            .SingleAsync(cancellationToken);

        var problem = await ValidateActivityAsync(
            activity.ProjectScheduleId, projectId, request, activityId,
            cancellationToken);

        if (problem is not null)
            return BadRequest(new { message = problem });

        activity.Name = request.Name.Trim();
        activity.ParentActivityId = request.ParentActivityId;
        activity.ProjectHakedisSectionId = request.ProjectHakedisSectionId;
        activity.ProjectBoqItemId = request.ProjectBoqItemId;
        activity.PlannedStartDate = request.PlannedStartDate;
        activity.PlannedEndDate = request.PlannedEndDate;
        activity.ManualProgressRate = request.ManualProgressRate;
        activity.Notes = request.Notes?.Trim();
        activity.UpdatedAtUtc = DateTime.UtcNow;

        if (request.Order is int order)
            activity.Order = order;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Aktivite güncellendi." });
    }

    /// <summary>
    /// Aktiviteyi ve ona bağlı bağımlılıkları siler.
    ///
    /// Alt aktivitesi olan ana çubuk silinemez: sessizce onları da
    /// silmek, kullanıcının görmediği bir veri kaybı olurdu.
    /// </summary>
    [HttpDelete("api/is-programi/aktiviteler/{activityId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.ScheduleManage)]
    public async Task<IActionResult> DeleteActivity(
        Guid activityId, CancellationToken cancellationToken)
    {
        var activity = await db.ScheduleActivities
            .SingleOrDefaultAsync(x => x.Id == activityId, cancellationToken);

        if (activity is null)
            return NotFound(new { message = "Aktivite bulunamadı." });

        var childCount = await db.ScheduleActivities.CountAsync(
            x => x.ParentActivityId == activityId, cancellationToken);

        if (childCount > 0)
        {
            return BadRequest(new
            {
                message = $"Bu aktivitenin {childCount} alt aktivitesi var; " +
                          "önce onları silin."
            });
        }

        var links = await db.ScheduleDependencies
            .Where(x => x.PredecessorActivityId == activityId ||
                        x.SuccessorActivityId == activityId)
            .ToListAsync(cancellationToken);

        db.ScheduleDependencies.RemoveRange(links);
        db.ScheduleActivities.Remove(activity);

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = links.Count == 0
                ? "Aktivite silindi."
                : $"Aktivite ve {links.Count} bağımlılığı silindi."
        });
    }

    // ---------------- Bağımlılıklar ----------------

    [HttpPost("api/is-programi/{id:guid}/bagimliliklar")]
    [RequirePermission(PermissionCatalog.Keys.ScheduleManage)]
    public async Task<IActionResult> CreateDependency(
        Guid id,
        ScheduleDependencyRequest request,
        CancellationToken cancellationToken)
    {
        var exists = await db.ProjectSchedules.AnyAsync(
            x => x.Id == id, cancellationToken);

        if (!exists)
            return NotFound(new { message = "İş programı bulunamadı." });

        if (!Enum.IsDefined(typeof(ScheduleDependencyType), request.Type))
            return BadRequest(new { message = "Geçersiz bağımlılık türü." });

        var problem = await schedules.ValidateNewDependencyAsync(
            id, request.PredecessorActivityId, request.SuccessorActivityId,
            cancellationToken);

        if (problem is not null)
            return BadRequest(new { message = problem });

        var dependency = new ScheduleDependency
        {
            ProjectScheduleId = id,
            PredecessorActivityId = request.PredecessorActivityId,
            SuccessorActivityId = request.SuccessorActivityId,
            Type = (ScheduleDependencyType)request.Type,
            LagWorkDays = request.LagWorkDays
        };

        db.ScheduleDependencies.Add(dependency);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { id = dependency.Id, message = "Bağımlılık eklendi." });
    }

    [HttpDelete("api/is-programi/bagimliliklar/{dependencyId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.ScheduleManage)]
    public async Task<IActionResult> DeleteDependency(
        Guid dependencyId, CancellationToken cancellationToken)
    {
        var dependency = await db.ScheduleDependencies
            .SingleOrDefaultAsync(x => x.Id == dependencyId, cancellationToken);

        if (dependency is null)
            return NotFound(new { message = "Bağımlılık bulunamadı." });

        db.ScheduleDependencies.Remove(dependency);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Bağımlılık silindi." });
    }

    // ---------------- Baseline ----------------

    /// <summary>
    /// Baseline kaydeder: o anki HESAPLANMIŞ plan tarihleri kilitli
    /// referans olarak kopyalanır.
    ///
    /// Baseline değiştirilebilir ama iz bırakır. Sık revizyon, planın
    /// gerçeğe uydurulduğunun işaretidir ve görünmesi gerekir; bu yüzden
    /// ikinci ve sonraki revizyonlarda GEREKÇE zorunludur.
    /// </summary>
    [HttpPost("api/is-programi/{id:guid}/baseline")]
    [RequirePermission(PermissionCatalog.Keys.ScheduleManage)]
    public async Task<IActionResult> SaveBaseline(
        Guid id,
        SaveBaselineRequest request,
        CancellationToken cancellationToken)
    {
        var schedule = await db.ProjectSchedules
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (schedule is null)
            return NotFound(new { message = "İş programı bulunamadı." });

        var reason = request.Reason?.Trim();

        if (schedule.BaselineRevisionNumber > 0 && string.IsNullOrWhiteSpace(reason))
        {
            return BadRequest(new
            {
                message = "Baseline'ı yeniden kaydetmek için gerekçe zorunludur; " +
                          "referans tarih değiştiğinde gecikme ölçüsü de değişir."
            });
        }

        var view = await schedules.BuildAsync(id, cancellationToken);

        if (view.Activities.Count == 0)
        {
            return BadRequest(new
            {
                message = "Aktivitesi olmayan programda baseline kaydedilemez."
            });
        }

        var computed = view.Activities.ToDictionary(x => x.Id);

        var activities = await db.ScheduleActivities
            .Where(x => x.ProjectScheduleId == id)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;

        foreach (var activity in activities)
        {
            if (!computed.TryGetValue(activity.Id, out var line))
                continue;

            activity.BaselineStartDate = line.PlannedStart;
            activity.BaselineEndDate = line.PlannedEnd;
            activity.UpdatedAtUtc = now;
        }

        schedule.BaselineRevisionNumber++;
        schedule.BaselineSetAtUtc = now;
        schedule.BaselineSetByUserId = ActorId();
        schedule.UpdatedAtUtc = now;

        db.ScheduleBaselineRevisions.Add(new ScheduleBaselineRevision
        {
            ProjectScheduleId = id,
            RevisionNumber = schedule.BaselineRevisionNumber,
            SetAtUtc = now,
            SetByUserId = ActorId(),
            Reason = reason,
            ActivityCount = activities.Count,
            PlannedStartDate = view.ProjectStart,
            PlannedEndDate = view.ProjectFinish
        });

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            revisionNumber = schedule.BaselineRevisionNumber,
            message = schedule.BaselineRevisionNumber == 1
                ? "Baseline kaydedildi; gecikme bundan sonra bu referansa göre ölçülür."
                : $"Baseline {schedule.BaselineRevisionNumber}. kez kaydedildi."
        });
    }

    [HttpGet("api/is-programi/{id:guid}/baseline-gecmisi")]
    [RequirePermission(PermissionCatalog.Keys.ScheduleView)]
    public async Task<IActionResult> BaselineHistory(
        Guid id, CancellationToken cancellationToken) =>
        Ok(await db.ScheduleBaselineRevisions
            .AsNoTracking()
            .Where(x => x.ProjectScheduleId == id)
            .OrderByDescending(x => x.RevisionNumber)
            .Select(x => new
            {
                x.Id,
                x.RevisionNumber,
                x.SetAtUtc,
                x.SetByUserId,
                x.Reason,
                x.ActivityCount,
                x.PlannedStartDate,
                x.PlannedEndDate
            })
            .ToListAsync(cancellationToken));

    // ---------------- Tatiller ----------------

    [HttpPut("api/is-programi/{id:guid}/tatiller")]
    [RequirePermission(PermissionCatalog.Keys.ScheduleManage)]
    public async Task<IActionResult> ReplaceHolidays(
        Guid id,
        ReplaceScheduleHolidaysRequest request,
        CancellationToken cancellationToken)
    {
        var exists = await db.ProjectSchedules.AnyAsync(
            x => x.Id == id, cancellationToken);

        if (!exists)
            return NotFound(new { message = "İş programı bulunamadı." });

        var incoming = request.Holidays
            .GroupBy(x => x.Date)
            .Select(g => g.First())
            .ToList();

        var current = await db.ScheduleHolidays
            .Where(x => x.ProjectScheduleId == id)
            .ToListAsync(cancellationToken);

        db.ScheduleHolidays.RemoveRange(
            current.Where(x => incoming.All(i => i.Date != x.Date)));

        foreach (var holiday in incoming)
        {
            var existing = current.SingleOrDefault(x => x.Date == holiday.Date);

            if (existing is null)
            {
                db.ScheduleHolidays.Add(new ScheduleHoliday
                {
                    ProjectScheduleId = id,
                    Date = holiday.Date,
                    Name = holiday.Name?.Trim()
                });

                continue;
            }

            existing.Name = holiday.Name?.Trim();
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            count = incoming.Count,
            message = $"{incoming.Count} tatil günü kaydedildi."
        });
    }

    // ---------------- Kaynaklar ----------------

    /// <summary>
    /// Aktiviteye personel ya da taşeron atar.
    ///
    /// Ayrı bir "ekip" kavramı AÇILMADI: taşeron zaten taşeron
    /// sözleşmesi, personel zaten personeldir. Üçüncü bir kavram aynı
    /// kişiyi iki yerde tutmayı gerektirirdi.
    /// </summary>
    [HttpPost("api/is-programi/aktiviteler/{activityId:guid}/kaynaklar")]
    [RequirePermission(PermissionCatalog.Keys.ScheduleManage)]
    public async Task<IActionResult> AssignResource(
        Guid activityId,
        AssignScheduleResourceRequest request,
        CancellationToken cancellationToken)
    {
        var activity = await db.ScheduleActivities
            .AsNoTracking()
            .Where(x => x.Id == activityId)
            .Select(x => new
            {
                x.Id,
                x.ProjectScheduleId,
                ProjectId = x.ProjectSchedule.ProjectId,
                CompanyId = x.ProjectSchedule.Project.CompanyId
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (activity is null)
            return NotFound(new { message = "Aktivite bulunamadı." });

        if (!Enum.IsDefined(typeof(ScheduleResourceKind), request.Kind))
            return BadRequest(new { message = "Geçersiz kaynak türü." });

        var kind = (ScheduleResourceKind)request.Kind;

        if (kind == ScheduleResourceKind.Personnel)
        {
            if (request.PersonnelId is not Guid personnelId)
                return BadRequest(new { message = "Personel seçilmelidir." });

            var belongs = await db.Personnel.AnyAsync(
                x => x.Id == personnelId && x.CompanyId == activity.CompanyId,
                cancellationToken);

            if (!belongs)
            {
                return BadRequest(new
                {
                    message = "Seçilen personel projenin şirketine ait değil."
                });
            }
        }
        else
        {
            if (request.SubcontractorContractId is not Guid contractId)
            {
                return BadRequest(new
                {
                    message = "Taşeron sözleşmesi seçilmelidir."
                });
            }

            var belongs = await db.SubcontractorContracts.AnyAsync(
                x => x.Id == contractId && x.ProjectId == activity.ProjectId,
                cancellationToken);

            if (!belongs)
            {
                return BadRequest(new
                {
                    message = "Seçilen taşeron sözleşmesi bu projeye ait değil."
                });
            }
        }

        var personnelKey = kind == ScheduleResourceKind.Personnel
            ? request.PersonnelId
            : null;

        var contractKey = kind == ScheduleResourceKind.Subcontractor
            ? request.SubcontractorContractId
            : null;

        var duplicate = await db.ScheduleResourceAssignments.AnyAsync(
            x => x.ScheduleActivityId == activityId &&
                 x.Kind == kind &&
                 x.PersonnelId == personnelKey &&
                 x.SubcontractorContractId == contractKey,
            cancellationToken);

        if (duplicate)
        {
            return BadRequest(new
            {
                message = "Bu kaynak zaten bu aktiviteye atanmış."
            });
        }

        var assignment = new ScheduleResourceAssignment
        {
            ScheduleActivityId = activityId,
            Kind = kind,
            PersonnelId = personnelKey,
            SubcontractorContractId = contractKey,
            Role = request.Role?.Trim(),
            Notes = request.Notes?.Trim()
        };

        db.ScheduleResourceAssignments.Add(assignment);
        await db.SaveChangesAsync(cancellationToken);

        // Atamadan hemen sonra çakışma söylenmeli: kullanıcı ekranı
        // kapattıktan sonra fark ederse geç kalır.
        var conflicts = await schedules.GetConflictsAsync(
            activity.ProjectScheduleId, cancellationToken);

        var related = conflicts
            .Where(x => x.FirstActivityId == activityId ||
                        x.SecondActivityId == activityId)
            .ToList();

        return Ok(new
        {
            id = assignment.Id,
            message = related.Count == 0
                ? "Kaynak atandı."
                : $"Kaynak atandı; {related.Count} çakışma tespit edildi.",
            conflicts = related
        });
    }

    [HttpDelete("api/is-programi/kaynaklar/{assignmentId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.ScheduleManage)]
    public async Task<IActionResult> RemoveResource(
        Guid assignmentId, CancellationToken cancellationToken)
    {
        var assignment = await db.ScheduleResourceAssignments
            .SingleOrDefaultAsync(x => x.Id == assignmentId, cancellationToken);

        if (assignment is null)
            return NotFound(new { message = "Kaynak ataması bulunamadı." });

        db.ScheduleResourceAssignments.Remove(assignment);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Kaynak ataması kaldırıldı." });
    }

    /// <summary>
    /// Programdaki bütün kaynak çakışmaları.
    ///
    /// Çakışma HATA DEĞİL, uyarıdır: bir ustabaşı gerçekten iki işi
    /// birden yürütebilir. Engellenmesi değil görünür olması gerekiyor —
    /// özellikle iki aktivite de kritik yoldaysa.
    /// </summary>
    [HttpGet("api/is-programi/{id:guid}/kaynak-cakismalari")]
    [RequirePermission(PermissionCatalog.Keys.ScheduleView)]
    public async Task<IActionResult> ResourceConflicts(
        Guid id, CancellationToken cancellationToken)
    {
        var exists = await db.ProjectSchedules.AnyAsync(
            x => x.Id == id, cancellationToken);

        if (!exists)
            return NotFound(new { message = "İş programı bulunamadı." });

        var conflicts = await schedules.GetConflictsAsync(id, cancellationToken);

        return Ok(new
        {
            criticalCount = conflicts.Count(x => x.BothCritical),
            items = conflicts
        });
    }

    /// <summary>
    /// Aktiviteye atanabilecek kaynak önerileri.
    ///
    /// Taşeron tarafı mevcut sözleşme–kısım bağını okur: bir kısım
    /// zaten bir taşerondaysa öneri listesinin başında o çıkar. "Hangi
    /// kısım hangi taşeronda" bilgisi sistemde zaten vardı; iş programı
    /// onu tekrar sormuyor.
    /// </summary>
    [HttpGet("api/is-programi/aktiviteler/{activityId:guid}/kaynak-onerileri")]
    [RequirePermission(PermissionCatalog.Keys.ScheduleView)]
    public async Task<IActionResult> ResourceSuggestions(
        Guid activityId, CancellationToken cancellationToken)
    {
        var activity = await db.ScheduleActivities
            .AsNoTracking()
            .Where(x => x.Id == activityId)
            .Select(x => new
            {
                x.Id,
                x.ProjectHakedisSectionId,
                ParentSectionId = x.ParentActivity == null
                    ? null
                    : x.ParentActivity.ProjectHakedisSectionId,
                ProjectId = x.ProjectSchedule.ProjectId,
                CompanyId = x.ProjectSchedule.Project.CompanyId
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (activity is null)
            return NotFound(new { message = "Aktivite bulunamadı." });

        // Alt aktivitenin kendi kısmı olmaz; ana çubuğunun kısmı geçerli.
        var sectionId = activity.ProjectHakedisSectionId ?? activity.ParentSectionId;

        var contracts = await db.SubcontractorContracts
            .AsNoTracking()
            .Where(x => x.ProjectId == activity.ProjectId &&
                        x.Status != SubcontractorContractStatus.Cancelled)
            .Select(x => new
            {
                x.Id,
                x.ContractNumber,
                x.WorkDescription,
                Name = x.CurrentAccount.Title,
                CoversSection = sectionId != null &&
                    x.Sections.Any(s => s.ProjectHakedisSectionId == sectionId)
            })
            .ToListAsync(cancellationToken);

        // Projeye/şantiyelerine atanmış personel önce gelir: sahada
        // olmayan birini önermek kullanıcıya iş çıkarır.
        var assignedPersonnel = await db.ProjectSiteAssignments
            .AsNoTracking()
            .Where(x => x.ProjectSite.ProjectId == activity.ProjectId &&
                        x.EndDate == null)
            .Select(x => x.PersonnelId)
            .ToListAsync(cancellationToken);

        var personnel = await db.Personnel
            .AsNoTracking()
            .Where(x => x.CompanyId == activity.CompanyId &&
                        x.Status == PersonnelStatus.Active)
            .Select(x => new
            {
                x.Id,
                x.EmployeeNumber,
                Name = x.FirstName + " " + x.LastName
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            sectionId,
            subcontractors = contracts
                .OrderByDescending(x => x.CoversSection)
                .ThenBy(x => x.Name, StringComparer.CurrentCulture)
                .ToList(),
            personnel = personnel
                .Select(x => new
                {
                    x.Id,
                    x.EmployeeNumber,
                    x.Name,
                    OnThisProject = assignedPersonnel.Contains(x.Id)
                })
                .OrderByDescending(x => x.OnThisProject)
                .ThenBy(x => x.Name, StringComparer.CurrentCulture)
                .ToList()
        });
    }

    // ---------------- Yardımcılar ----------------

    /// <summary>
    /// İcmal kısımlarını ana çubuğa çevirir. Tarih verisi yoksa çubuk
    /// projenin başlangıcından itibaren bir haftalık varsayılan süreyle
    /// açılır — kullanıcı sonra düzeltir. Tarihsiz çubuk çizilemezdi.
    /// </summary>
    private async Task<int> SeedFromSectionsAsync(
        ProjectSchedule schedule,
        DateTime? projectStart,
        CancellationToken cancellationToken)
    {
        var linked = await db.ScheduleActivities
            .AsNoTracking()
            .Where(x => x.ProjectScheduleId == schedule.Id &&
                        x.ProjectHakedisSectionId != null)
            .Select(x => x.ProjectHakedisSectionId!.Value)
            .ToListAsync(cancellationToken);

        var sections = await db.ProjectHakedisSections
            .AsNoTracking()
            .Where(x => x.ProjectId == schedule.ProjectId &&
                        x.IsActive &&
                        !linked.Contains(x.Id))
            .OrderBy(x => x.Order)
            .Select(x => new { x.Id, x.Name, x.Order })
            .ToListAsync(cancellationToken);

        if (sections.Count == 0)
            return 0;

        var calendar = await schedules.BuildCalendarAsync(
            schedule.Id, cancellationToken);

        var start = calendar.NextWorkDay(projectStart is DateTime value
            ? DateOnly.FromDateTime(value)
            : DateOnly.FromDateTime(DateTime.UtcNow));

        var nextOrder = await NextOrderAsync(schedule.Id, null, cancellationToken);

        foreach (var section in sections)
        {
            db.ScheduleActivities.Add(new ScheduleActivity
            {
                ProjectScheduleId = schedule.Id,
                ProjectHakedisSectionId = section.Id,
                Name = section.Name,
                Order = nextOrder++,
                PlannedStartDate = start,
                PlannedEndDate = calendar.FinishFromStart(start, 6)
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        return sections.Count;
    }

    private async Task<int> NextOrderAsync(
        Guid scheduleId, Guid? parentId, CancellationToken cancellationToken)
    {
        var max = await db.ScheduleActivities
            .Where(x => x.ProjectScheduleId == scheduleId &&
                        x.ParentActivityId == parentId)
            .MaxAsync(x => (int?)x.Order, cancellationToken);

        return (max ?? 0) + 1;
    }

    private async Task<string?> ValidateActivityAsync(
        Guid scheduleId,
        Guid projectId,
        ScheduleActivityRequest request,
        Guid? activityId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return "Aktivite adı zorunludur.";

        if (request.PlannedEndDate < request.PlannedStartDate)
            return "Bitiş tarihi başlangıç tarihinden önce olamaz.";

        if (request.ParentActivityId is Guid parentId)
        {
            if (parentId == activityId)
                return "Bir aktivite kendisinin alt aktivitesi olamaz.";

            var parent = await db.ScheduleActivities
                .AsNoTracking()
                .Where(x => x.Id == parentId)
                .Select(x => new { x.ProjectScheduleId, x.ParentActivityId })
                .SingleOrDefaultAsync(cancellationToken);

            if (parent is null || parent.ProjectScheduleId != scheduleId)
                return "Üst aktivite bu iş programında bulunamadı.";

            // İki seviye yeter: kısım → alt aktivite. Derinleşen ağaç
            // Gantt'ı okunmaz yapar ve ilerleme toplamayı belirsizleştirir.
            if (parent.ParentActivityId is not null)
                return "Alt aktivitenin altına yeni bir seviye açılamaz.";

            if (activityId is Guid current)
            {
                var hasChildren = await db.ScheduleActivities.AnyAsync(
                    x => x.ParentActivityId == current, cancellationToken);

                if (hasChildren)
                {
                    return "Alt aktivitesi olan bir çubuk başka bir çubuğun " +
                           "altına taşınamaz.";
                }
            }
        }

        if (request.ProjectHakedisSectionId is Guid sectionId)
        {
            if (request.ParentActivityId is not null)
            {
                return "İcmal kısmı yalnızca ana çubuğa bağlanır; alt aktivite " +
                       "icmal satırına bağlanabilir.";
            }

            var belongs = await db.ProjectHakedisSections.AnyAsync(
                x => x.Id == sectionId && x.ProjectId == projectId,
                cancellationToken);

            if (!belongs)
                return "Seçilen icmal kısmı bu projeye ait değil.";

            // Aynı kısmın iki çubuğu, ilerlemeyi iki kez sayılmış gibi
            // gösterirdi.
            var duplicate = await db.ScheduleActivities.AnyAsync(
                x => x.ProjectScheduleId == scheduleId &&
                     x.ProjectHakedisSectionId == sectionId &&
                     (activityId == null || x.Id != activityId),
                cancellationToken);

            if (duplicate)
                return "Bu icmal kısmı için zaten bir çubuk var.";
        }

        if (request.ProjectBoqItemId is Guid boqItemId)
        {
            var belongs = await db.ProjectBoqItems.AnyAsync(
                x => x.Id == boqItemId && x.ProjectBoq.ProjectId == projectId,
                cancellationToken);

            if (!belongs)
                return "Seçilen icmal satırı bu projeye ait değil.";
        }

        if (request.ManualProgressRate is decimal rate)
        {
            if (rate < 0m || rate > 100m)
                return "İlerleme yüzdesi 0 ile 100 arasında olmalıdır.";

            // İcmale bağlı çubukta gerçekleşme saha raporundan gelir;
            // elle girilen yüzde onu sessizce ezerdi.
            if (request.ProjectHakedisSectionId is not null ||
                request.ProjectBoqItemId is not null)
            {
                return "İcmale bağlı aktivitede ilerleme elle girilemez; " +
                       "gerçekleşme saha raporundan gelir.";
            }
        }

        return null;
    }

    private static bool TryReadWorkWeek(
        int? value, out WorkWeekDays workWeek, out string? problem)
    {
        workWeek = WorkWeekDays.MondayToSaturday;
        problem = null;

        if (value is not int raw)
            return true;

        if (raw is <= 0 or > 127)
        {
            problem = "Çalışma haftasında en az bir gün seçilmelidir.";
            return false;
        }

        workWeek = (WorkWeekDays)raw;
        return true;
    }

    private Guid? ActorId()
    {
        var raw = User.FindFirst("sub")?.Value
            ?? User.FindFirst(
                System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(raw, out var parsed) ? parsed : null;
    }
}
