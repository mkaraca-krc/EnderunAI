using EnderunAI.Api.Contracts.Secretariat;
using EnderunAI.Api.Models.Secretariat;

namespace EnderunAI.Api.Services.Secretariat;

public interface ISecretariatService
{
    Task<IReadOnlyCollection<IncomingDocumentListItemResponse>> GetIncomingAsync(
        Guid? companyId,
        Guid? projectId,
        SecretariatDocumentStatus? status,
        string? search,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default);

    Task<IncomingDocumentDetailResponse> GetIncomingByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IncomingDocumentDetailResponse> CreateIncomingAsync(
        CreateIncomingDocumentRequest request,
        Guid? userId,
        string? userName,
        CancellationToken cancellationToken = default);

    Task<IncomingDocumentDetailResponse> UpdateIncomingAsync(
        Guid id,
        UpdateIncomingDocumentRequest request,
        Guid? userId,
        string? userName,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<OutgoingDocumentListItemResponse>> GetOutgoingAsync(
        Guid? companyId,
        Guid? projectId,
        SecretariatDocumentStatus? status,
        string? search,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default);

    Task<OutgoingDocumentDetailResponse> GetOutgoingByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<OutgoingDocumentDetailResponse> CreateOutgoingAsync(
        CreateOutgoingDocumentRequest request,
        Guid? userId,
        string? userName,
        CancellationToken cancellationToken = default);

    Task<OutgoingDocumentDetailResponse> UpdateOutgoingAsync(
        Guid id,
        UpdateOutgoingDocumentRequest request,
        Guid? userId,
        string? userName,
        CancellationToken cancellationToken = default);

    Task AddWorkflowAsync(
        SecretariatDocumentDirection direction,
        Guid documentId,
        DocumentWorkflowRequest request,
        Guid? userId,
        string? userName,
        CancellationToken cancellationToken = default);

    Task ArchiveAsync(
        SecretariatDocumentDirection direction,
        Guid documentId,
        Guid? userId,
        string? userName,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        SecretariatDocumentDirection direction,
        Guid documentId,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<DocumentCategoryResponse>> GetCategoriesAsync(
        Guid? companyId,
        CancellationToken cancellationToken = default);

    Task<DocumentCategoryResponse> CreateCategoryAsync(
        CreateDocumentCategoryRequest request,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<SecretariatDashboardResponse> GetDashboardAsync(
        Guid? companyId,
        CancellationToken cancellationToken = default);
}
