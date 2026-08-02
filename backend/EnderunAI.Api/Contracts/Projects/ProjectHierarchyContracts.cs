using EnderunAI.Api.Models;

namespace EnderunAI.Api.Contracts.Projects;

public sealed record ProjectHierarchyLevelDto(
    Guid Id,
    string Code,
    string Name,
    int SortOrder,
    bool IsRequired,
    int NodeCount);

public sealed record ProjectModuleScopeCountDto(
    ProjectModuleType ModuleType,
    int Count);

public sealed record ProjectHierarchyNodeDto(
    Guid Id,
    Guid LevelId,
    string LevelName,
    int LevelSortOrder,
    Guid? ParentNodeId,
    string Code,
    string Name,
    string? Description,
    int SortOrder,
    string Path,
    IReadOnlyList<ProjectModuleScopeCountDto> ModuleScopes,
    IReadOnlyList<ProjectHierarchyNodeDto> Children);

public sealed record ProjectHierarchyTreeDto(
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    IReadOnlyList<ProjectHierarchyLevelDto> Levels,
    IReadOnlyList<ProjectHierarchyNodeDto> Nodes);

public sealed record CreateProjectHierarchyLevelRequest(
    string Code,
    string Name,
    int SortOrder,
    bool IsRequired);

public sealed record UpdateProjectHierarchyLevelRequest(
    string Code,
    string Name,
    int SortOrder,
    bool IsRequired,
    bool IsActive);

public sealed record CreateProjectHierarchyNodeRequest(
    Guid LevelId,
    Guid? ParentNodeId,
    string Code,
    string Name,
    string? Description,
    int SortOrder);

public sealed record UpdateProjectHierarchyNodeRequest(
    Guid LevelId,
    Guid? ParentNodeId,
    string Code,
    string Name,
    string? Description,
    int SortOrder,
    bool IsActive);

public sealed record AssignProjectModuleScopeRequest(
    ProjectModuleType ModuleType,
    Guid RecordId);

public sealed record ProjectModuleScopeDto(
    Guid Id,
    Guid ProjectId,
    Guid ProjectHierarchyNodeId,
    string NodePath,
    ProjectModuleType ModuleType,
    Guid RecordId);

public sealed record ApplyHierarchyTemplateResult(
    int CreatedLevelCount,
    int CreatedNodeCount,
    ProjectHierarchyTreeDto Hierarchy);
