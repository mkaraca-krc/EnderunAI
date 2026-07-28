using EnderunAI.Api.Models;
using EnderunAI.Api.Security.CurrentUser;

namespace EnderunAI.Api.Security;

public sealed record CurrentDataScopeSnapshot(
    bool HasGlobalAccess,
    IReadOnlySet<Guid> CompanyIds,
    IReadOnlySet<Guid> BranchIds,
    IReadOnlySet<Guid> ProjectIds,
    IReadOnlySet<Guid> VisibleCompanyIds,
    IReadOnlySet<Guid> VisibleBranchIds)
{
    public IQueryable<Company> Apply(IQueryable<Company> query) =>
        HasGlobalAccess
            ? query
            : query.Where(item => VisibleCompanyIds.Contains(item.Id));

    public IQueryable<Branch> Apply(IQueryable<Branch> query) =>
        HasGlobalAccess
            ? query
            : query.Where(item =>
                CompanyIds.Contains(item.CompanyId) ||
                VisibleBranchIds.Contains(item.Id));

    public IQueryable<Project> Apply(IQueryable<Project> query) =>
        HasGlobalAccess
            ? query
            : query.Where(item =>
                CompanyIds.Contains(item.CompanyId) ||
                BranchIds.Contains(item.BranchId) ||
                ProjectIds.Contains(item.Id));

    public bool CanAccessCompany(Guid companyId) =>
        HasGlobalAccess || CompanyIds.Contains(companyId);

    public bool CanAccessBranch(Guid companyId, Guid branchId) =>
        HasGlobalAccess ||
        CompanyIds.Contains(companyId) ||
        BranchIds.Contains(branchId);

    public bool CanAccessProject(
        Guid companyId,
        Guid branchId,
        Guid projectId) =>
        HasGlobalAccess ||
        CompanyIds.Contains(companyId) ||
        BranchIds.Contains(branchId) ||
        ProjectIds.Contains(projectId);
}

public interface ICurrentDataScopeService
{
    Task<CurrentDataScopeSnapshot?> GetAsync(
        CancellationToken cancellationToken = default);
}

public sealed class CurrentDataScopeService(
    ICurrentUserService currentUser,
    IUserAuthorizationService authorizationService)
    : ICurrentDataScopeService
{
    public async Task<CurrentDataScopeSnapshot?> GetAsync(
        CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not Guid userId)
            return null;

        var authorization = await authorizationService.GetAsync(
            userId,
            cancellationToken);
        if (authorization is null || !authorization.IsActive)
            return null;

        var hasGlobalAccess =
            authorization.RoleNames.Contains(
                "Admin",
                StringComparer.OrdinalIgnoreCase) ||
            authorization.DataScopes.Any(item =>
                item.ScopeType == DataScopeType.All);
        return new CurrentDataScopeSnapshot(
            hasGlobalAccess,
            authorization.DataScopes
                .Where(item =>
                    item.ScopeType == DataScopeType.Company &&
                    item.CompanyId.HasValue)
                .Select(item => item.CompanyId!.Value)
                .ToHashSet(),
            authorization.DataScopes
                .Where(item =>
                    item.ScopeType == DataScopeType.Branch &&
                    item.BranchId.HasValue)
                .Select(item => item.BranchId!.Value)
                .ToHashSet(),
            authorization.DataScopes
                .Where(item =>
                    item.ScopeType == DataScopeType.Project &&
                    item.ProjectId.HasValue)
                .Select(item => item.ProjectId!.Value)
                .ToHashSet(),
            authorization.DataScopes
                .Where(item => item.CompanyId.HasValue)
                .Select(item => item.CompanyId!.Value)
                .ToHashSet(),
            authorization.DataScopes
                .Where(item => item.BranchId.HasValue)
                .Select(item => item.BranchId!.Value)
                .ToHashSet());
    }
}
