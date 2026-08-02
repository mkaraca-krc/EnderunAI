namespace EnderunAI.Api.Models;

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

    public ICollection<ProjectSiteDailyReportWorkItem> WorkItems { get; set; }
        = new List<ProjectSiteDailyReportWorkItem>();

    public ICollection<ProjectSiteDailyReportPhoto> Photos { get; set; }
        = new List<ProjectSiteDailyReportPhoto>();
}

public sealed class ProjectSiteDailyReportWorkItem : BaseEntity
{
    public Guid DailyReportId { get; set; }
    public ProjectSiteDailyReport DailyReport { get; set; } = null!;

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

    public DateTime? RevokedAtUtc { get; set; }
    public Guid? RevokedByUserId { get; set; }
}
