using EnderunAI.Api.Models;

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

public enum CargoDirection
{
    Incoming = 0,
    Outgoing = 1
}

public enum CargoStatus
{
    Registered = 0,
    InTransit = 1,
    Delivered = 2,
    Returned = 3,
    Cancelled = 4
}

public enum VisitorStatus
{
    Expected = 0,
    CheckedIn = 1,
    CheckedOut = 2,
    Cancelled = 3,
    Rejected = 4
}

public enum PhoneNoteStatus
{
    New = 0,
    Informed = 1,
    Returned = 2,
    Closed = 3,
    Cancelled = 4
}

public enum SecretariatScheduleType
{
    Meeting = 0,
    Appointment = 1
}

public enum SecretariatScheduleStatus
{
    Planned = 0,
    Confirmed = 1,
    Completed = 2,
    Cancelled = 3
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
    public string? DeliveryMethod { get; set; }
    public SecretariatDocumentPriority Priority { get; set; } = SecretariatDocumentPriority.Normal;
    public SecretariatDocumentStatus Status { get; set; } = SecretariatDocumentStatus.Registered;
    public Guid? AssignedToUserId { get; set; }
    public string? AssignedToName { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? ArchivedAtUtc { get; set; }
    public string? Notes { get; set; }
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
    public string? DeliveryMethod { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? SignedByName { get; set; }
    public SecretariatDocumentPriority Priority { get; set; } = SecretariatDocumentPriority.Normal;
    public SecretariatDocumentStatus Status { get; set; } = SecretariatDocumentStatus.Draft;
    public DateTime? SentAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? ArchivedAtUtc { get; set; }
    public string? Notes { get; set; }
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

public sealed class CargoShipment : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid? ProjectId { get; set; }
    public CargoDirection Direction { get; set; }
    public string TrackingNumber { get; set; } = string.Empty;
    public string CargoCompany { get; set; } = string.Empty;
    public string? SenderName { get; set; }
    public string? RecipientName { get; set; }
    public string? InstitutionName { get; set; }
    public DateTime CargoDate { get; set; }
    public DateTime? ExpectedDeliveryDate { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    public string? DeliveredToName { get; set; }
    public string? Description { get; set; }
    public CargoStatus Status { get; set; } = CargoStatus.Registered;
}

public sealed class VisitorRecord : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid? ProjectId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? IdentityNumber { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? CompanyName { get; set; }
    public string? VehiclePlate { get; set; }
    public string? VisitorCardNumber { get; set; }
    public string PersonToVisit { get; set; } = string.Empty;
    public string? DepartmentName { get; set; }
    public string VisitPurpose { get; set; } = string.Empty;
    public DateTime PlannedVisitAtUtc { get; set; }
    public DateTime? CheckInAtUtc { get; set; }
    public DateTime? CheckOutAtUtc { get; set; }
    public string? ApprovedByName { get; set; }
    public string? ReceivedByName { get; set; }
    public string? Description { get; set; }
    public VisitorStatus Status { get; set; } = VisitorStatus.Expected;
}

public sealed class PhoneNote : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid? ProjectId { get; set; }
    public string CallerName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? InstitutionName { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string ResponsibleName { get; set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? InformedAtUtc { get; set; }
    public DateTime? ReturnedAtUtc { get; set; }
    public PhoneNoteStatus Status { get; set; } = PhoneNoteStatus.New;
    public string? Notes { get; set; }
}

public sealed class SecretariatScheduleEntry : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid? ProjectId { get; set; }
    public SecretariatScheduleType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? CompanyName { get; set; }
    public string? Location { get; set; }
    public DateTime StartAtUtc { get; set; }
    public DateTime? EndAtUtc { get; set; }
    public string? OwnerName { get; set; }
    public string? Participants { get; set; }
    public string? Description { get; set; }
    public DateTime? ReminderAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public SecretariatScheduleStatus Status { get; set; } = SecretariatScheduleStatus.Planned;
    public string? Notes { get; set; }
}
