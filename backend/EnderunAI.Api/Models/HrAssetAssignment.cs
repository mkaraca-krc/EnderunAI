namespace EnderunAI.Api.Models;

public enum HrAssetAssignmentStatus
{
    Assigned = 0,
    Returned = 1,
    Lost = 2,
    Damaged = 3,
    Cancelled = 4
}

public sealed class HrAssetAssignment : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid PersonnelId { get; set; }
    public Guid? ProjectId { get; set; }

    /// <summary>
    /// Zimmetlenen demirbaş kartı. OPSİYONEL ve bilinçli olarak
    /// zorunlu değil: alet kartı sonradan geldi ve mevcut serbest
    /// metinli zimmetler bozulmadan çalışmaya devam ediyor.
    ///
    /// Doluysa alet adı/seri numarası karttan gelir ve servis geçmişi
    /// bu bağ üzerinden birikir. Boşsa aşağıdaki serbest metin
    /// alanları geçerlidir.
    /// </summary>
    public Guid? ToolAssetId { get; set; }
    public ToolAsset? ToolAsset { get; set; }

    public string AssetType { get; set; } = string.Empty;
    public string AssetCode { get; set; } = string.Empty;
    public string AssetName { get; set; } = string.Empty;
    public string? SerialNumber { get; set; }

    public DateTime AssignmentDate { get; set; }
    public DateTime? PlannedReturnDate { get; set; }
    public DateTime? ActualReturnDate { get; set; }

    public string? ConditionAtAssignment { get; set; }
    public string? ConditionAtReturn { get; set; }
    public string? DocumentPath { get; set; }

    public HrAssetAssignmentStatus Status { get; set; } = HrAssetAssignmentStatus.Assigned;
    public string? Notes { get; set; }

    public Guid? InventoryItemId { get; set; }
    public Guid? IssueStockMovementId { get; set; }
    public Guid? ReturnStockMovementId { get; set; }
    public Guid? WarehouseId { get; set; }
}
