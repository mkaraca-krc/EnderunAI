using EnderunAI.Api.Contracts.Secretariat;
using EnderunAI.Api.Models.Secretariat;

namespace EnderunAI.Api.Services.Secretariat;

public interface ISecretariatService
{
    Task<IReadOnlyCollection<CorrespondenceResponse>> GetCorrespondenceAsync(
        Guid? companyId,
        Guid? projectId,
        SecretariatDocumentDirection? direction,
        SecretariatDocumentStatus? status,
        string? search,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default);

    Task<CorrespondenceDetailResponse?> GetCorrespondenceAsync(
        SecretariatDocumentDirection direction,
        Guid id,
        CancellationToken cancellationToken = default);

    Task<CorrespondenceDetailResponse> CreateCorrespondenceAsync(
        CreateCorrespondenceRequest request,
        Guid? userId,
        string? userName,
        CancellationToken cancellationToken = default);

    Task<CorrespondenceDetailResponse?> UpdateCorrespondenceAsync(
        SecretariatDocumentDirection direction,
        Guid id,
        UpdateCorrespondenceRequest request,
        Guid? userId,
        string? userName,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteCorrespondenceAsync(
        SecretariatDocumentDirection direction,
        Guid id,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<bool> AddWorkflowAsync(
        SecretariatDocumentDirection direction,
        Guid documentId,
        DocumentWorkflowRequest request,
        Guid? userId,
        string? userName,
        CancellationToken cancellationToken = default);

    Task<bool> ArchiveCorrespondenceAsync(
        SecretariatDocumentDirection direction,
        Guid documentId,
        Guid? userId,
        string? userName,
        CancellationToken cancellationToken = default);

    Task<DocumentAttachmentResponse?> AddAttachmentAsync(
        SecretariatDocumentDirection direction,
        Guid documentId,
        string fileName,
        string storedFileName,
        string filePath,
        string? contentType,
        long fileSize,
        string? description,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<DocumentAttachmentResponse?> GetAttachmentAsync(
        Guid attachmentId,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAttachmentAsync(
        Guid attachmentId,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<DocumentCategoryResponse>> GetCategoriesAsync(
        Guid? companyId,
        CancellationToken cancellationToken = default);

    Task<DocumentCategoryResponse> CreateCategoryAsync(
        CreateDocumentCategoryRequest request,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<DocumentCategoryResponse?> UpdateCategoryAsync(
        Guid id,
        UpdateDocumentCategoryRequest request,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<CargoResponse>> GetCargoAsync(
        Guid? companyId,
        Guid? projectId,
        CargoDirection? direction,
        CargoStatus? status,
        string? search,
        CancellationToken cancellationToken = default);

    Task<CargoResponse?> GetCargoAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<CargoResponse> CreateCargoAsync(
        CreateCargoRequest request,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<CargoResponse?> UpdateCargoAsync(
        Guid id,
        UpdateCargoRequest request,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteCargoAsync(
        Guid id,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<VisitorResponse>> GetVisitorsAsync(
        Guid? companyId,
        Guid? projectId,
        VisitorStatus? status,
        DateTime? startDate,
        DateTime? endDate,
        string? search,
        CancellationToken cancellationToken = default);

    Task<VisitorResponse> CreateVisitorAsync(
        CreateVisitorRequest request,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<VisitorResponse?> CheckInVisitorAsync(
        Guid id,
        string? receivedByName,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<VisitorResponse?> CheckOutVisitorAsync(
        Guid id,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteVisitorAsync(
        Guid id,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<PhoneNoteResponse>> GetPhoneNotesAsync(
        Guid? companyId,
        Guid? projectId,
        PhoneNoteStatus? status,
        string? search,
        CancellationToken cancellationToken = default);

    Task<PhoneNoteResponse> CreatePhoneNoteAsync(
        CreatePhoneNoteRequest request,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<PhoneNoteResponse?> UpdatePhoneNoteAsync(
        Guid id,
        UpdatePhoneNoteRequest request,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<PhoneNoteResponse?> UpdatePhoneNoteStatusAsync(
        Guid id,
        PhoneNoteStatus status,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<bool> DeletePhoneNoteAsync(
        Guid id,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ScheduleResponse>> GetSchedulesAsync(
        SecretariatScheduleType type,
        Guid? companyId,
        Guid? projectId,
        SecretariatScheduleStatus? status,
        DateTime? startDate,
        DateTime? endDate,
        string? search,
        CancellationToken cancellationToken = default);

    Task<ScheduleResponse> CreateScheduleAsync(
        SecretariatScheduleType type,
        CreateScheduleRequest request,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<ScheduleResponse?> UpdateScheduleAsync(
        SecretariatScheduleType type,
        Guid id,
        UpdateScheduleRequest request,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<ScheduleResponse?> UpdateScheduleStatusAsync(
        SecretariatScheduleType type,
        Guid id,
        SecretariatScheduleStatus status,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteScheduleAsync(
        SecretariatScheduleType type,
        Guid id,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<SecretariatDashboardResponse> GetDashboardAsync(
        Guid? companyId,
        CancellationToken cancellationToken = default);
}
