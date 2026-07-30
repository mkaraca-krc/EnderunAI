using EnderunAI.Api.Contracts.ProjectBoqs;

namespace EnderunAI.Api.Services.ProjectBoqs;

public interface IProjectBoqService
{
    Task<IReadOnlyList<ProjectBoqListItemDto>> GetAllAsync(
        Guid companyId,
        Guid projectId,
        int? status,
        string? search,
        CancellationToken cancellationToken = default);

    Task<ProjectBoqDetailDto?> GetByIdAsync(
        Guid id,
        Guid companyId,
        Guid projectId,
        CancellationToken cancellationToken = default);
}
