namespace EnderunAI.Api.Models.Secretariat;

public enum SecretariatDocumentStatus
{
    Draft = 0,
    Registered = 1,
    Assigned = 2,
    InProgress = 3,
    Answered = 4,
    Completed = 5,
    Archived = 6,
    Cancelled = 7
}

public enum SecretariatDocumentPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Urgent = 3
}

public enum SecretariatDocumentDirection
{
    Incoming = 0,
    Outgoing = 1
}

public enum SecretariatWorkflowAction
{
    Created = 0,
    Registered = 1,
    Assigned = 2,
    Read = 3,
    Commented = 4,
    Answered = 5,
    Completed = 6,
    Archived = 7,
    Reopened = 8,
    Cancelled = 9
}

public sealed class DocumentCategory : BaseEntity
{
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsDefault { get; set; }
}

public sealed class IncomingDocument : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? CategoryId { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public string? ExternalDocumentNumber { get; set; }
    public DateTime DocumentDate { get; set; }
    public DateTime RegisteredAtUtc { get; set; } = DateTime.UtcNow;
    public string SenderName { get; set; } = string.Empty;
    public string? SenderOrganization { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string? Description { get; set; }
    public SecretariatDocumentPriority Priority { get; set; } = SecretariatDocumentPriority.Normal;
    public SecretariatDocumentStatus Status { get; set; } = SecretariatDocumentStatus.Registered;
    public Guid? AssignedToUserId { get; set; }
    public string? AssignedToName { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? ArchivedAtUtc { get; set; }
    public string? Notes { get; set; }

    public ICollection<DocumentWorkflow> Workflows { get; set; } = new List<DocumentWorkflow>();
    public ICollection<DocumentAttachment> Attachments { get; set; } = new List<DocumentAttachment>();
}

public sealed class OutgoingDocument : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? CategoryId { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public DateTime DocumentDate { get; set; }
    public DateTime RegisteredAtUtc { get; set; } = DateTime.UtcNow;
    public string RecipientName { get; set; } = string.Empty;
    public string? RecipientOrganization { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? SignedByName { get; set; }
    public SecretariatDocumentPriority Priority { get; set; } = SecretariatDocumentPriority.Normal;
    public SecretariatDocumentStatus Status { get; set; } = SecretariatDocumentStatus.Draft;
    public DateTime? SentAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? ArchivedAtUtc { get; set; }
    public string? Notes { get; set; }

    public ICollection<DocumentWorkflow> Workflows { get; set; } = new List<DocumentWorkflow>();
    public ICollection<DocumentAttachment> Attachments { get; set; } = new List<DocumentAttachment>();
}

public sealed class DocumentWorkflow : BaseEntity
{
    public Guid CompanyId { get; set; }
    public SecretariatDocumentDirection Direction { get; set; }
    public Guid DocumentId { get; set; }
    public SecretariatWorkflowAction Action { get; set; }
    public Guid? FromUserId { get; set; }
    public string? FromUserName { get; set; }
    public Guid? ToUserId { get; set; }
    public string? ToUserName { get; set; }
    public string? Description { get; set; }
    public DateTime ActionAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class DocumentAttachment : BaseEntity
{
    public Guid CompanyId { get; set; }
    public SecretariatDocumentDirection Direction { get; set; }
    public Guid DocumentId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long FileSize { get; set; }
    public string? Description { get; set; }
}
