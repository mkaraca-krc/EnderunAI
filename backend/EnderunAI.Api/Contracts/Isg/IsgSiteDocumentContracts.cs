namespace EnderunAI.Api.Contracts.Isg;

public sealed record IsgSiteDocumentListItem(
    Guid Id,
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    Guid? ProjectSiteId,
    string? SiteName,
    int DocumentType,
    string DocumentTypeName,
    string Title,
    DateOnly IssueDate,
    DateOnly? ValidUntil,
    string ValidityStatus,
    string ValidityStatusName,
    string ValidityColor,
    int? DaysRemaining,
    string OriginalFileName,
    long SizeBytes,
    string? Notes,
    DateTime CreatedAtUtc);

public sealed record UpdateIsgSiteDocumentRequest(
    int DocumentType,
    string Title,
    DateOnly IssueDate,
    DateOnly? ValidUntil,
    Guid? ProjectSiteId,
    string? Notes);
