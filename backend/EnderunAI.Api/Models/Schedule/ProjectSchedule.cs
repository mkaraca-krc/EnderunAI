using EnderunAI.Api.Services.Schedule;

namespace EnderunAI.Api.Models.Schedule;

public enum ProjectScheduleStatus
{
    Draft = 0,
    Active = 1,
    Archived = 2
}

/// <summary>
/// Projenin iş programı (Gantt).
///
/// AYRI BİR İŞ KALEMİ LİSTESİ DEĞİLDİR. Aktiviteler projenin icmal
/// kısımlarına (<see cref="ProjectHakedisSection"/>) bağlanır; iş
/// programının kattığı şey ZAMAN, BAĞIMLILIK ve KAYNAKTIR. Gantt'a
/// kendi iş kalemi listesini açmak, icmalle sessizce ayrışan ikinci bir
/// gerçek üretirdi.
///
/// Kısma bağlanmasının nedeni: kısım PROJEYE aittir, icmale değil.
/// İcmal R2, R3 diye revize olduğunda iş programı bozulmaz.
/// </summary>
public sealed class ProjectSchedule : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public ProjectScheduleStatus Status { get; set; } = ProjectScheduleStatus.Draft;

    /// <summary>
    /// Haftanın çalışılan günleri. Varsayılan pazar hariç her gün —
    /// şantiyede yaygın olan bu. Takvim günüyle çalışmak isteyen
    /// <see cref="WorkWeekDays.AllDays"/> seçer.
    /// </summary>
    public WorkWeekDays WorkWeek { get; set; } = WorkWeekDays.MondayToSaturday;

    /// <summary>
    /// Baseline kaç kez kaydedildi. Sık revizyon kötü yönetim
    /// işaretidir; sayı ekranda görünür ve her revizyon
    /// <see cref="ScheduleBaselineRevision"/> olarak loglanır.
    /// </summary>
    public int BaselineRevisionNumber { get; set; }

    public DateTime? BaselineSetAtUtc { get; set; }
    public Guid? BaselineSetByUserId { get; set; }

    public string? Notes { get; set; }

    public ICollection<ScheduleActivity> Activities { get; set; }
        = new List<ScheduleActivity>();

    public ICollection<ScheduleDependency> Dependencies { get; set; }
        = new List<ScheduleDependency>();

    public ICollection<ScheduleHoliday> Holidays { get; set; }
        = new List<ScheduleHoliday>();

    /// <summary>Baseline hiç kaydedilmemişse plan–gerçek kıyası yapılamaz.</summary>
    public bool HasBaseline => BaselineRevisionNumber > 0;
}

/// <summary>
/// Gantt çubuğu.
///
/// İki seviyeli: üst seviye (ParentActivityId boş) bir icmal KISMIDIR,
/// altındaki opsiyonel alt-aktiviteler ("kablo tavası montajı", "kablo
/// çekimi", "test-devreye alma") isteyen için. Detay girmeyen kullanıcı
/// kısım seviyesinde kalır ve program yine çalışır.
/// </summary>
public sealed class ScheduleActivity : BaseEntity
{
    public Guid ProjectScheduleId { get; set; }
    public ProjectSchedule ProjectSchedule { get; set; } = null!;

    /// <summary>Boşsa ana çubuk; doluysa alt-aktivite.</summary>
    public Guid? ParentActivityId { get; set; }
    public ScheduleActivity? ParentActivity { get; set; }

    /// <summary>
    /// Bağlı olduğu icmal kısmı. Ana çubukta beklenen bağ budur;
    /// gerçekleşen ilerleme buradan gelir.
    /// </summary>
    public Guid? ProjectHakedisSectionId { get; set; }
    public ProjectHakedisSection? ProjectHakedisSection { get; set; }

    /// <summary>
    /// Alt-aktivite tek bir icmal SATIRINA da bağlanabilir; o zaman
    /// ilerleme o satırın saha gerçekleşmesinden gelir.
    /// </summary>
    public Guid? ProjectBoqItemId { get; set; }
    public ProjectBoqItem? ProjectBoqItem { get; set; }

    public int Order { get; set; }
    public string Name { get; set; } = string.Empty;

    public DateOnly PlannedStartDate { get; set; }
    public DateOnly PlannedEndDate { get; set; }

    /// <summary>
    /// Kilitli referans. Baseline kaydedilince plan tarihlerinden
    /// kopyalanır ve plan sonradan değişse bile OYNAMAZ — gecikmenin
    /// ölçüldüğü yer burasıdır.
    /// </summary>
    public DateOnly? BaselineStartDate { get; set; }
    public DateOnly? BaselineEndDate { get; set; }

    /// <summary>
    /// İcmale bağlı OLMAYAN aktivitede elle girilen ilerleme yüzdesi.
    /// Kısma ya da icmal satırına bağlıysa kullanılmaz: oradaki
    /// gerçekleşme saha raporundan gelir ve elle girilen bir yüzde onu
    /// sessizce ezerdi.
    /// </summary>
    public decimal? ManualProgressRate { get; set; }

    public string? Notes { get; set; }

    public ICollection<ScheduleActivity> Children { get; set; }
        = new List<ScheduleActivity>();

    public ICollection<ScheduleResourceAssignment> Resources { get; set; }
        = new List<ScheduleResourceAssignment>();
}

/// <summary>
/// İki aktivite arasındaki bağ. Döngü oluşturan bağ hiç
/// kaydedilmez — kaydedilen bir döngü bütün programı hesaplanamaz
/// yapardı.
/// </summary>
public sealed class ScheduleDependency : BaseEntity
{
    public Guid ProjectScheduleId { get; set; }
    public ProjectSchedule ProjectSchedule { get; set; } = null!;

    public Guid PredecessorActivityId { get; set; }
    public ScheduleActivity PredecessorActivity { get; set; } = null!;

    public Guid SuccessorActivityId { get; set; }
    public ScheduleActivity SuccessorActivity { get; set; } = null!;

    public ScheduleDependencyType Type { get; set; }
        = ScheduleDependencyType.FinishToStart;

    /// <summary>Gecikme payı, çalışma günü. Negatif = örtüşme.</summary>
    public int LagWorkDays { get; set; }
}

/// <summary>
/// Baseline revizyon kaydı.
///
/// Baseline değiştirilebilir ama iz bırakır: kaç kez, ne zaman, kim,
/// hangi gerekçeyle. Sık revizyon, planın gerçeğe uydurulduğunun
/// işaretidir ve görünmesi gerekir.
/// </summary>
public sealed class ScheduleBaselineRevision : BaseEntity
{
    public Guid ProjectScheduleId { get; set; }
    public ProjectSchedule ProjectSchedule { get; set; } = null!;

    public int RevisionNumber { get; set; }

    public DateTime SetAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? SetByUserId { get; set; }

    /// <summary>Revizyonun gerekçesi — denetimde sorulan budur.</summary>
    public string? Reason { get; set; }

    public int ActivityCount { get; set; }

    /// <summary>Revizyon anındaki plan başlangıcı ve bitişi.</summary>
    public DateOnly? PlannedStartDate { get; set; }
    public DateOnly? PlannedEndDate { get; set; }
}

/// <summary>
/// Programa özel tatil günü. Resmî tatiller ve şantiyeye özgü
/// kapanışlar (bayram tatili uzatması, iş durdurma) süreyi uzatır.
/// </summary>
public sealed class ScheduleHoliday : BaseEntity
{
    public Guid ProjectScheduleId { get; set; }
    public ProjectSchedule ProjectSchedule { get; set; } = null!;

    public DateOnly Date { get; set; }
    public string? Name { get; set; }
}

/// <summary>
/// Aktiviteye atanan kaynak: personel ya da taşeron sözleşmesi.
///
/// Ayrı bir "ekip" tablosu AÇILMADI: taşeron zaten taşeron sözleşmesi,
/// personel zaten personeldir. Üçüncü bir kavram, aynı kişiyi iki yerde
/// tutmayı gerektirirdi.
/// </summary>
public sealed class ScheduleResourceAssignment : BaseEntity
{
    public Guid ScheduleActivityId { get; set; }
    public ScheduleActivity ScheduleActivity { get; set; } = null!;

    public ScheduleResourceKind Kind { get; set; } = ScheduleResourceKind.Personnel;

    public Guid? PersonnelId { get; set; }
    public Personnel? Personnel { get; set; }

    public Guid? SubcontractorContractId { get; set; }
    public SubcontractorContract? SubcontractorContract { get; set; }

    /// <summary>Rol / görev tanımı ("ekip şefi", "pano montajı").</summary>
    public string? Role { get; set; }

    public string? Notes { get; set; }
}
