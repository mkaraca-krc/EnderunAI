namespace EnderunAI.Api.Models;

public sealed class ProjectSite : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Location { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// Bu şantiyeye özgü barter oranı (%). Barter oranı şantiyeden
    /// şantiyeye değişir; boşsa projedeki oran kullanılır, hakedişte de
    /// düzeltilebilir.
    /// </summary>
    public decimal? BarterRate { get; set; }

    public ICollection<ProjectSiteAssignment> Assignments { get; set; }
        = new List<ProjectSiteAssignment>();

    public ICollection<Warehouse> Warehouses { get; set; }
        = new List<Warehouse>();
}

public sealed class ProjectSiteAssignment : BaseEntity
{
    public Guid PersonnelId { get; set; }
    public Personnel Personnel { get; set; } = null!;

    public Guid ProjectSiteId { get; set; }
    public ProjectSite ProjectSite { get; set; } = null!;

    public string? Role { get; set; }
    public string? Notes { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}
