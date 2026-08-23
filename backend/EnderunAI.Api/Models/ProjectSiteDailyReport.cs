namespace EnderunAI.Api.Models;

public enum ProjectSiteDailyReportStatus
{
    Draft = 0,
    Approved = 1
}

public sealed class ProjectSiteDailyReport : BaseEntity
{
    public Guid ProjectSiteId { get; set; }
    public ProjectSite ProjectSite { get; set; } = null!;

    public DateTime ReportDate { get; set; }
    public string? WeatherCondition { get; set; }

    public int EngineerCount { get; set; }
    public int ForemanCount { get; set; }
    public int CraftsmanCount { get; set; }
    public int WorkerCount { get; set; }
    public int OtherCount { get; set; }

    public string? Notes { get; set; }

    public ProjectSiteDailyReportStatus Status { get; set; } =
        ProjectSiteDailyReportStatus.Draft;

    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }

    public ICollection<ProjectSiteDailyReportWorkItem> WorkItems { get; set; }
        = new List<ProjectSiteDailyReportWorkItem>();

    public ICollection<ProjectSiteDailyReportPhoto> Photos { get; set; }
        = new List<ProjectSiteDailyReportPhoto>();
}

public sealed class ProjectSiteDailyReportWorkItem : BaseEntity
{
    public Guid DailyReportId { get; set; }
    public ProjectSiteDailyReport DailyReport { get; set; } = null!;

    /// <summary>
    /// Sözleşme icmalindeki kalem. OPSİYONEL: icmalde olmayan iş de
    /// günlük rapora yazılabilmeli, aksi halde saha o günü hiç
    /// kaydedemezdi. Seçilirse onaylı miktarlar bu kalemin iç
    /// gerçekleşmesine birikir.
    /// </summary>
    public Guid? ProjectBoqItemId { get; set; }
    public ProjectBoqItem? ProjectBoqItem { get; set; }

    public string Description { get; set; } = string.Empty;
    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }
}

public sealed class ProjectSiteDailyReportPhoto : BaseEntity
{
    public Guid DailyReportId { get; set; }
    public ProjectSiteDailyReport DailyReport { get; set; } = null!;

    public string StoredFileName { get; set; } = string.Empty;
    public string OriginalName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string? Caption { get; set; }

    public bool IsVisibleToEmployer { get; set; } = false;
}

public sealed class EmployerPortalLink : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public string Token { get; set; } = string.Empty;

    public string? EmployerName { get; set; }
    public string? EmployerEmail { get; set; }

    public DateTime? RevokedAtUtc { get; set; }
    public Guid? RevokedByUserId { get; set; }

    /// <summary>
    /// SON GEÇERLİLİK — ZORUNLU, VARSAYILAN 6 AY.
    ///
    /// Bağlantı e-postayla paylaşılıyor ve kimlik doğrulaması yok:
    /// süresiz bırakıldığında elle iptal edilene kadar kalıcı bir
    /// kapı oluyor. E-posta kutusu yıllar sonra başkasının eline
    /// geçse bile bağlantı çalışmaya devam ederdi.
    ///
    /// Süresi geçen bağlantı 404 dönüyor, 401 DEĞİL: 401 "böyle bir
    /// bağlantı var ama artık geçerli değil" bilgisini verirdi ve
    /// geçerli token aramaya çalışan birine "bu token bir zamanlar
    /// vardı" ipucu olurdu.
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>
    /// Son açılma zamanı ve toplam açılma sayısı — yönetim ekranında
    /// "bu bağlantı kullanılıyor mu" sorusunun cevabı. Kullanılmayan
    /// bir bağlantıyı iptal etmek, kullanılanı uzatmak için gerekli.
    /// </summary>
    public DateTime? LastAccessedAtUtc { get; set; }

    public int AccessCount { get; set; }

    /// <summary>
    /// Uzatma izi: kaç kez, en son ne zaman, en son kim uzattı.
    /// Denetim kaydı ayrıca security_audit_events'e yazılıyor; bu
    /// alanlar ekranda göstermek için kayıt üzerinde duruyor.
    /// </summary>
    public DateTime? LastExtendedAtUtc { get; set; }
    public Guid? LastExtendedByUserId { get; set; }
    public int ExtensionCount { get; set; }
}

public sealed class EmployerPortalEmailLog : BaseEntity
{
    public Guid EmployerPortalLinkId { get; set; }
    public EmployerPortalLink EmployerPortalLink { get; set; } = null!;

    public Guid ProjectId { get; set; }

    public string RecipientEmail { get; set; } = string.Empty;
    public string? RecipientName { get; set; }

    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}
