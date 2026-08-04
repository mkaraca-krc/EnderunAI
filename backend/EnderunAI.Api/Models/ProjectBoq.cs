namespace EnderunAI.Api.Models;

public enum ProjectBoqStatus
{
    Draft = 0,
    Approved = 1,
    Superseded = 2,
    Archived = 3
}

public enum ProjectBoqItemType
{
    Mixed = 0,
    Material = 1,
    Labor = 2
}

public sealed class ProjectBoq : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public string BoqNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int RevisionNumber { get; set; } = 1;
    public ProjectBoqStatus Status { get; set; } = ProjectBoqStatus.Draft;
    public bool IsCurrentRevision { get; set; } = true;
    public string CurrencyCode { get; set; } = "TRY";
    public decimal TotalAmount { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }

    /// <summary>
    /// Bu keşif projenin SÖZLEŞME METRAJI mı — keşif–gerçekleşen
    /// karşılaştırmasının referansı. Proje başına yalnızca bir keşif
    /// baseline olabilir; revizyonlar keşif kaydının kendi
    /// versiyonlamasıyla yürür.
    /// </summary>
    public bool IsContractBaseline { get; set; }

    public string? Description { get; set; }
    public string? Notes { get; set; }

    public ICollection<ProjectBoqItem> Items { get; set; } = new List<ProjectBoqItem>();
}

public sealed class ProjectBoqItem : BaseEntity
{
    public Guid ProjectBoqId { get; set; }
    public ProjectBoq ProjectBoq { get; set; } = null!;

    public Guid? EngineeringPositionId { get; set; }

    /// <summary>
    /// Kalemin ait olduğu imalat bölümü (hakedişteki 12 bölümün
    /// aynısı). Karma sözleşmede sapmanın hangi tiple yorumlanacağı
    /// buradan belirlenir.
    /// </summary>
    public Guid? ProjectHakedisSectionId { get; set; }
    public ProjectHakedisSection? ProjectHakedisSection { get; set; }

    /// <summary>
    /// Kalemin karşılığı olan stok kalemi. Eşlenmişse fiili sarf
    /// (StockMovement, Issue) ayrı bir gösterge olarak takip ekranında
    /// çıkar — hakedişe girmemiş sarf buradan görünür.
    /// </summary>
    public Guid? InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }

    public int LineNumber { get; set; }
    public string PositionCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;

    public decimal ContractQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalAmount { get; set; }

    public ProjectBoqItemType ItemType { get; set; } = ProjectBoqItemType.Mixed;
    public string? Category { get; set; }
    public string? Notes { get; set; }
}
