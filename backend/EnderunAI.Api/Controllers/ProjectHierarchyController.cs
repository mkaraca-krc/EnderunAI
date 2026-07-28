using EnderunAI.Api.Contracts.Projects;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Projects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/hierarchy")]
public sealed class ProjectHierarchyController(
    IProjectHierarchyService service) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.ProjectsView)]
    public Task<ActionResult<ProjectHierarchyTreeDto>> GetTree(
        Guid projectId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            () => service.GetTreeAsync(projectId, cancellationToken));

    [HttpPost("levels")]
    [RequirePermission(PermissionCatalog.Keys.ProjectsManage)]
    public Task<ActionResult<ProjectHierarchyLevelDto>> CreateLevel(
        Guid projectId,
        CreateProjectHierarchyLevelRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            () => service.CreateLevelAsync(
                projectId,
                request,
                cancellationToken));

    [HttpPut("levels/{levelId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.ProjectsManage)]
    public Task<ActionResult<ProjectHierarchyLevelDto>> UpdateLevel(
        Guid projectId,
        Guid levelId,
        UpdateProjectHierarchyLevelRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            () => service.UpdateLevelAsync(
                projectId,
                levelId,
                request,
                cancellationToken));

    [HttpDelete("levels/{levelId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.ProjectsManage)]
    public Task<ActionResult<bool>> DeleteLevel(
        Guid projectId,
        Guid levelId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            () => service.DeleteLevelAsync(
                projectId,
                levelId,
                cancellationToken));

    [HttpPost("nodes")]
    [RequirePermission(PermissionCatalog.Keys.ProjectsManage)]
    public Task<ActionResult<ProjectHierarchyNodeDto>> CreateNode(
        Guid projectId,
        CreateProjectHierarchyNodeRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            () => service.CreateNodeAsync(
                projectId,
                request,
                cancellationToken));

    [HttpPut("nodes/{nodeId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.ProjectsManage)]
    public Task<ActionResult<ProjectHierarchyNodeDto>> UpdateNode(
        Guid projectId,
        Guid nodeId,
        UpdateProjectHierarchyNodeRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            () => service.UpdateNodeAsync(
                projectId,
                nodeId,
                request,
                cancellationToken));

    [HttpDelete("nodes/{nodeId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.ProjectsManage)]
    public Task<ActionResult<bool>> DeleteNode(
        Guid projectId,
        Guid nodeId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            () => service.DeleteNodeAsync(
                projectId,
                nodeId,
                cancellationToken));

    [HttpPut("nodes/{nodeId:guid}/module-scope")]
    [RequirePermission(PermissionCatalog.Keys.ProjectsManage)]
    public Task<ActionResult<ProjectModuleScopeDto>> AssignModuleScope(
        Guid projectId,
        Guid nodeId,
        AssignProjectModuleScopeRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            () => service.AssignModuleScopeAsync(
                projectId,
                nodeId,
                request,
                cancellationToken));

    [HttpDelete("module-scope/{moduleType}/{recordId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.ProjectsManage)]
    public Task<ActionResult<bool>> RemoveModuleScope(
        Guid projectId,
        ProjectModuleType moduleType,
        Guid recordId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            () => service.RemoveModuleScopeAsync(
                projectId,
                moduleType,
                recordId,
                cancellationToken));

    [HttpPost("templates/mke")]
    [RequirePermission(PermissionCatalog.Keys.ProjectsManage)]
    public Task<ActionResult<ApplyHierarchyTemplateResult>> ApplyMkeTemplate(
        Guid projectId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            () => service.ApplyMkeTemplateAsync(
                projectId,
                cancellationToken));

    private async Task<ActionResult<T>> ExecuteAsync<T>(
        Func<Task<T>> operation)
    {
        try
        {
            return Ok(await operation());
        }
        catch (ProjectHierarchyException exception)
        {
            return StatusCode(
                exception.StatusCode,
                new { message = exception.Message });
        }
    }
}
