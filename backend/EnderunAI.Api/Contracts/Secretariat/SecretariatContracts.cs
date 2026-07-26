using EnderunAI.Api.Models.Secretariat;

namespace EnderunAI.Api.Contracts.Secretariat;

public sealed record CreateCorrespondenceRequest(
    Guid CompanyId,
    Guid? ProjectId,
    SecretariatDocumentDirection Direction,
    string? DocumentNumber,
    DateTime DocumentDate,
    DateTime? RegistrationDate,
    string Subject,
    string? SenderName,
    string? RecipientName,
    string? InstitutionName,
    string? DeliveryMethod,
    string? ReferenceNumber,
    string? Description,
    Guid? CategoryId,
    SecretariatDocumentPriority Priority,
    Guid? AssignedToUserId,
    string? AssignedToName,
    DateTime? DueDate,
    string? SignedByName,
    string? Notes);

public sealed record UpdateCorrespondenceRequest(
    Guid? ProjectId,
    Guid? CategoryId,
    DateTime DocumentDate,
    string Subject,
    string? SenderName,
    string? RecipientName,
    string? InstitutionName,
    string? DeliveryMethod,
    string? ReferenceNumber,
    string? Description,
    SecretariatDocumentPriority Priority,
    SecretariatDocumentStatus Status,
    Guid? AssignedToUserId,
    string? AssignedToName,
    DateTime? DueDate,
    string? SignedByName,
    DateTime? SentAtUtc,
    string? Notes);

public sealed record CorrespondenceResponse(
    Guid Id,
    Guid CompanyId,
    Guid? ProjectId,
    Guid? CategoryId,
    SecretariatDocumentDirection Direction,
    string DirectionName,
    string DocumentNumber,
    string? ExternalDocumentNumber,
    DateTime DocumentDate,
    DateTime RegistrationDate,
    string Subject,
    string? SenderName,
    string? RecipientName,
    string? InstitutionName,
    string? DeliveryMethod,
    string? ReferenceNumber,
    string? Description,
    string? SignedByName,
    SecretariatDocumentPriority Priority,
    string PriorityName,
    SecretariatDocumentStatus Status,
    string StatusName,
    Guid? AssignedToUserId,
    string? AssignedToName,
    DateTime? DueDate,
    DateTime? SentAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? ArchivedAtUtc,
    string? Notes,
    int AttachmentCount,
    DateTime CreatedAtUtc);

public sealed record DocumentWorkflowRequest(
    SecretariatWorkflowAction Action,
    Guid? ToUserId,
    string? ToUserName,
    string? Description);

public sealed record DocumentWorkflowResponse(
    Guid Id,
    SecretariatWorkflowAction Action,
    string ActionName,
    string? FromUserName,
    string? ToUserName,
    string? Description,
    DateTime ActionAtUtc);

public sealed record DocumentAttachmentResponse(
    Guid Id,
    SecretariatDocumentDirection Direction,
    Guid DocumentId,
    string FileName,
    string StoredFileName,
    string FilePath,
    string? ContentType,
    long FileSize,
    string? Description,
    DateTime CreatedAtUtc);

public sealed record CorrespondenceDetailResponse(
    CorrespondenceResponse Document,
    IReadOnlyCollection<DocumentAttachmentResponse> Attachments,
    IReadOnlyCollection<DocumentWorkflowResponse> Workflow);

public sealed record CreateDocumentCategoryRequest(
    Guid CompanyId,
    string Code,
    string Name,
    string? Description,
    bool IsDefault);

public sealed record UpdateDocumentCategoryRequest(
    string Name,
    string? Description,
    bool IsDefault,
    bool IsActive);

public sealed record DocumentCategoryResponse(
    Guid Id,
    Guid CompanyId,
    string Code,
    string Name,
    string? Description,
    bool IsDefault,
    bool IsActive);

public sealed record CreateCargoRequest(
    Guid CompanyId,
    Guid? ProjectId,
    CargoDirection Direction,
    string TrackingNumber,
    string CargoCompany,
    string? SenderName,
    string? RecipientName,
    string? InstitutionName,
    DateTime CargoDate,
    DateTime? ExpectedDeliveryDate,
    string? Description);

public sealed record UpdateCargoRequest(
    Guid? ProjectId,
    string CargoCompany,
    string? SenderName,
    string? RecipientName,
    string? InstitutionName,
    DateTime CargoDate,
    DateTime? ExpectedDeliveryDate,
    DateTime? DeliveredAtUtc,
    string? DeliveredToName,
    string? Description,
    CargoStatus Status);

public sealed record CargoResponse(
    Guid Id,
    Guid CompanyId,
    Guid? ProjectId,
    CargoDirection Direction,
    string DirectionName,
    string TrackingNumber,
    string CargoCompany,
    string? SenderName,
    string? RecipientName,
    string? InstitutionName,
    DateTime CargoDate,
    DateTime? ExpectedDeliveryDate,
    DateTime? DeliveredAtUtc,
    string? DeliveredToName,
    string? Description,
    CargoStatus Status,
    string StatusName,
    DateTime CreatedAtUtc);

public sealed record CreateVisitorRequest(
    Guid CompanyId,
    Guid? ProjectId,
    string FullName,
    string? IdentityNumber,
    string? PhoneNumber,
    string? Email,
    string? CompanyName,
    string? VehiclePlate,
    string? VisitorCardNumber,
    string PersonToVisit,
    string? DepartmentName,
    string VisitPurpose,
    DateTime PlannedVisitAtUtc,
    string? ApprovedByName,
    string? Description);

public sealed record VisitorCheckInRequest(string? ReceivedByName);

public sealed record VisitorResponse(
    Guid Id,
    Guid CompanyId,
    Guid? ProjectId,
    string FullName,
    string? IdentityNumber,
    string? PhoneNumber,
    string? Email,
    string? CompanyName,
    string? VehiclePlate,
    string? VisitorCardNumber,
    string PersonToVisit,
    string? DepartmentName,
    string VisitPurpose,
    DateTime PlannedVisitAtUtc,
    DateTime? CheckInAtUtc,
    DateTime? CheckOutAtUtc,
    string? ApprovedByName,
    string? ReceivedByName,
    string? Description,
    VisitorStatus Status,
    string StatusName,
    DateTime CreatedAtUtc);

public sealed record CreatePhoneNoteRequest(
    Guid CompanyId,
    Guid? ProjectId,
    string CallerName,
    string? PhoneNumber,
    string? InstitutionName,
    string Subject,
    string Message,
    string ResponsibleName,
    DateTime? ReceivedAtUtc,
    string? Notes);

public sealed record UpdatePhoneNoteRequest(
    Guid? ProjectId,
    string CallerName,
    string? PhoneNumber,
    string? InstitutionName,
    string Subject,
    string Message,
    string ResponsibleName,
    DateTime ReceivedAtUtc,
    PhoneNoteStatus Status,
    string? Notes);

public sealed record UpdatePhoneNoteStatusRequest(PhoneNoteStatus Status);

public sealed record PhoneNoteResponse(
    Guid Id,
    Guid CompanyId,
    Guid? ProjectId,
    string CallerName,
    string? PhoneNumber,
    string? InstitutionName,
    string Subject,
    string Message,
    string ResponsibleName,
    DateTime ReceivedAtUtc,
    DateTime? InformedAtUtc,
    DateTime? ReturnedAtUtc,
    PhoneNoteStatus Status,
    string StatusName,
    string? Notes,
    DateTime CreatedAtUtc);

public sealed record CreateScheduleRequest(
    Guid CompanyId,
    Guid? ProjectId,
    string Title,
    string? ContactName,
    string? CompanyName,
    string? Location,
    DateTime StartAtUtc,
    DateTime? EndAtUtc,
    string? OwnerName,
    string? Participants,
    string? Description,
    DateTime? ReminderAtUtc,
    string? Notes);

public sealed record UpdateScheduleRequest(
    Guid? ProjectId,
    string Title,
    string? ContactName,
    string? CompanyName,
    string? Location,
    DateTime StartAtUtc,
    DateTime? EndAtUtc,
    string? OwnerName,
    string? Participants,
    string? Description,
    DateTime? ReminderAtUtc,
    SecretariatScheduleStatus Status,
    string? Notes);

public sealed record UpdateScheduleStatusRequest(SecretariatScheduleStatus Status);

public sealed record ScheduleResponse(
    Guid Id,
    Guid CompanyId,
    Guid? ProjectId,
    SecretariatScheduleType Type,
    string TypeName,
    string Title,
    string? ContactName,
    string? CompanyName,
    string? Location,
    DateTime StartAtUtc,
    DateTime? EndAtUtc,
    string? OwnerName,
    string? Participants,
    string? Description,
    DateTime? ReminderAtUtc,
    DateTime? CompletedAtUtc,
    SecretariatScheduleStatus Status,
    string StatusName,
    string? Notes,
    DateTime CreatedAtUtc);

public sealed record SecretariatRecentActivityResponse(
    string Module,
    Guid RecordId,
    string Title,
    string Action,
    string? UserName,
    DateTime ActionAtUtc);

public sealed record SecretariatDashboardResponse(
    int TodayIncoming,
    int TodayOutgoing,
    int PendingDocuments,
    int OverdueDocuments,
    int CargoInTransit,
    int VisitorsInside,
    int OpenPhoneNotes,
    int TodayMeetings,
    int TodayAppointments,
    IReadOnlyCollection<SecretariatRecentActivityResponse> RecentActivities);
