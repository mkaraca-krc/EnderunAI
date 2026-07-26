namespace EnderunAI.Api.Models;

public enum ProcurementNotificationType
{
    ApprovalPending = 0,
    ApprovalOverdue = 1,
    Approved = 2,
    Rejected = 3,
    RevisionRequested = 4,
    DeliveryReminder = 5,
    System = 6
}

public enum ProcurementNotificationSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

public sealed class ProcurementNotification : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid? UserId { get; set; }
    public string? RoleName { get; set; }
    public ProcurementNotificationType Type { get; set; }
    public ProcurementNotificationSeverity Severity { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public Guid? DocumentId { get; set; }
    public string? DocumentNumber { get; set; }
    public Guid? ApprovalInstanceId { get; set; }
    public Guid? ApprovalStepId { get; set; }
    public string? ActionUrl { get; set; }
    public DateTime? DueAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public DateTime? DismissedAtUtc { get; set; }
    public string DeduplicationKey { get; set; } = string.Empty;
}
