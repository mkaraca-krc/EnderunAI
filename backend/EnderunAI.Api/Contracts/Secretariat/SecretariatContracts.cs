using EnderunAI.Api.Models.Secretariat;

namespace EnderunAI.Api.Contracts.Secretariat;

public sealed record CreateIncomingDocumentRequest(
    Guid CompanyId,
    Guid? ProjectId,
    Guid? CategoryId,
    string? ExternalDocumentNumber,
    DateTime DocumentDate,
    string SenderName,
    string? SenderOrganization,
    string Subject,
    string? Description,
    SecretariatDocumentPriority Priority,
    Guid? AssignedToUserId,
    string? AssignedToName,
    DateTime? DueDate,
    string? Notes);

public sealed record UpdateIncomingDocumentRequest(
    Guid? ProjectId,
    Guid? CategoryId,
    string? ExternalDocumentNumber,
    DateTime DocumentDate,
    string SenderName,
    string? SenderOrganization,
    string Subject,
    string? Description,
    SecretariatDocumentPriority Priority,
    Guid? AssignedToUserId,
    string? AssignedToName,
    DateTime? DueDate,
    SecretariatDocumentStatus Status,
    string? Notes);

public sealed record CreateOutgoingDocumentRequest(
    Guid CompanyId,
    Guid? ProjectId,
    Guid? CategoryId,
    DateTime DocumentDate,
    string RecipientName,
    string? RecipientOrganization,
    string Subject,
    string? Description,
    string? SignedByName,
    SecretariatDocumentPriority Priority,
    string? Notes);

public sealed record UpdateOutgoingDocumentRequest(
    Guid? ProjectId,
    Guid? CategoryId,
    DateTime DocumentDate,
    string RecipientName,
    string? RecipientOrganization,
    string Subject,
    string? Description,
    string? SignedByName,
    SecretariatDocumentPriority Priority,
    SecretariatDocumentStatus Status,
    DateTime? SentAtUtc,
    string? Notes);

public sealed record DocumentWorkflowRequest(
    SecretariatWorkflowAction Action,
    Guid? ToUserId,
    string? ToUserName,
    string? Description);

public sealed record CreateDocumentCategoryRequest(
    Guid CompanyId,
    string Code,
    string Name,
    string? Description,
    bool IsDefault);

public sealed record DocumentCategoryResponse(
    Guid Id,
    Guid CompanyId,
    string Code,
    string Name,
    string? Description,
    bool IsDefault,
    bool IsActive);

public sealed record DocumentAttachmentResponse(
    Guid Id,
    string FileName,
    string FilePath,
    string? ContentType,
    long FileSize,
    string? Description,
    DateTime CreatedAtUtc);

public sealed record DocumentWorkflowResponse(
    Guid Id,
    SecretariatWorkflowAction Action,
    string ActionName,
    string? FromUserName,
    string? ToUserName,
    string? Description,
    DateTime ActionAtUtc);

public sealed record IncomingDocumentListItemResponse(
    Guid Id,
    Guid CompanyId,
    Guid? ProjectId,
    Guid? CategoryId,
    string DocumentNumber,
    string? ExternalDocumentNumber,
    DateTime DocumentDate,
    string SenderName,
    string? SenderOrganization,
    string Subject,
    SecretariatDocumentPriority Priority,
    SecretariatDocumentStatus Status,
    string StatusName,
    string? AssignedToName,
    DateTime? DueDate,
    int AttachmentCount,
    DateTime CreatedAtUtc);

public sealed record OutgoingDocumentListItemResponse(
    Guid Id,
    Guid CompanyId,
    Guid? ProjectId,
    Guid? CategoryId,
    string DocumentNumber,
    DateTime DocumentDate,
    string RecipientName,
    string? RecipientOrganization,
    string Subject,
    string? SignedByName,
    SecretariatDocumentPriority Priority,
    SecretariatDocumentStatus Status,
    string StatusName,
    DateTime? SentAtUtc,
    int AttachmentCount,
    DateTime CreatedAtUtc);

public sealed record IncomingDocumentDetailResponse(
    IncomingDocumentListItemResponse Document,
    string? Description,
    string? Notes,
    IReadOnlyCollection<DocumentAttachmentResponse> Attachments,
    IReadOnlyCollection<DocumentWorkflowResponse> Workflow);

public sealed record OutgoingDocumentDetailResponse(
    OutgoingDocumentListItemResponse Document,
    string? Description,
    string? Notes,
    IReadOnlyCollection<DocumentAttachmentResponse> Attachments,
    IReadOnlyCollection<DocumentWorkflowResponse> Workflow);

public sealed record SecretariatDashboardResponse(
    int TodayIncoming,
    int TodayOutgoing,
    int Pending,
    int InProgress,
    int Archived,
    int Overdue,
    IReadOnlyCollection<SecretariatRecentActivityResponse> RecentActivities);

public sealed record SecretariatRecentActivityResponse(
    Guid DocumentId,
    SecretariatDocumentDirection Direction,
    string DocumentNumber,
    string Subject,
    string Action,
    string? UserName,
    DateTime ActionAtUtc);
