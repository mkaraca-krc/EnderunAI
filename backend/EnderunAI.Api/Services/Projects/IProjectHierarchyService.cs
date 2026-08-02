using EnderunAI.Api.Contracts.Projects;
using EnderunAI.Api.Models;

namespace EnderunAI.Api.Services.Projects;

public interface IProjectHierarchyService
{
    Task<ProjectHierarchyTreeDto> GetTreeAsync(
        Guid projectId,
        CancellationToken cancellationToken);

    Task<ProjectHierarchyLevelDto> CreateLevelAsync(
        Guid projectId,
        CreateProjectHierarchyLevelRequest request,
        CancellationToken cancellationToken);

    Task<ProjectHierarchyLevelDto> UpdateLevelAsync(
        Guid projectId,
        Guid levelId,
        UpdateProjectHierarchyLevelRequest request,
        CancellationToken cancellationToken);

    Task<bool> DeleteLevelAsync(
        Guid projectId,
        Guid levelId,
        CancellationToken cancellationToken);

    Task<ProjectHierarchyNodeDto> CreateNodeAsync(
        Guid projectId,
        CreateProjectHierarchyNodeRequest request,
        CancellationToken cancellationToken);

    Task<ProjectHierarchyNodeDto> UpdateNodeAsync(
        Guid projectId,
        Guid nodeId,
        UpdateProjectHierarchyNodeRequest request,
        CancellationToken cancellationToken);

    Task<bool> DeleteNodeAsync(
        Guid projectId,
        Guid nodeId,
        CancellationToken cancellationToken);

    Task<ProjectModuleScopeDto> AssignModuleScopeAsync(
        Guid projectId,
        Guid nodeId,
        AssignProjectModuleScopeRequest request,
        CancellationToken cancellationToken);

    Task<bool> RemoveModuleScopeAsync(
        Guid projectId,
        ProjectModuleType moduleType,
        Guid recordId,
        CancellationToken cancellationToken);

    Task<ApplyHierarchyTemplateResult> ApplyMkeTemplateAsync(
        Guid projectId,
        CancellationToken cancellationToken);
}

public sealed class ProjectHierarchyException(
    int statusCode,
    string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
