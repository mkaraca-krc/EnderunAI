namespace EnderunAI.Api.Contracts.ProjectSites;

public sealed record CreateProjectSiteRequest(
    string Code,
    string Name,
    string? Location,
    string? Notes);

public sealed record UpdateProjectSiteRequest(
    string Code,
    string Name,
    string? Location,
    string? Notes,
    bool IsActive);

public sealed record AssignPersonnelToSiteRequest(
    Guid PersonnelId,
    DateTime StartDate,
    DateTime? EndDate,
    string? Role,
    string? Notes);
