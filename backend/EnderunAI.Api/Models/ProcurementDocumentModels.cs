namespace EnderunAI.Api.Models;

public enum ProcurementDocumentType
{
    PurchaseRequest = 0,
    Rfq = 1,
    SupplierOffer = 2,
    PurchaseOrder = 3,
    GoodsReceipt = 4
}

public sealed class ProcurementDocumentRevision : BaseEntity
{
    public Guid CompanyId { get; set; }
    public ProcurementDocumentType DocumentType { get; set; }
    public Guid DocumentId { get; set; }
    public int RevisionNumber { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public Guid? CreatedByUserId { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
}

public sealed class ProcurementDocumentAttachment : BaseEntity
{
    public Guid CompanyId { get; set; }
    public ProcurementDocumentType DocumentType { get; set; }
    public Guid DocumentId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long FileSize { get; set; }
    public string? Description { get; set; }
    public Guid? UploadedByUserId { get; set; }
    public string UploadedByName { get; set; } = string.Empty;
}

public sealed class ProcurementDocumentComment : BaseEntity
{
    public Guid CompanyId { get; set; }
    public ProcurementDocumentType DocumentType { get; set; }
    public Guid DocumentId { get; set; }
    public string Comment { get; set; } = string.Empty;
    public Guid? CreatedByUserId { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
}
