using EnderunAI.Api.Models;
using EnderunAI.Api.Security;

namespace EnderunAI.Api.Tests.Security;

public sealed class CurrentDataScopeSnapshotTests
{
    [Fact]
    public void CompanyScopeAllowsAllBranchesAndProjectsInCompany()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var snapshot = new CurrentDataScopeSnapshot(
            false,
            new HashSet<Guid> { companyId },
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid> { companyId },
            new HashSet<Guid>());

        Assert.True(snapshot.CanAccessCompany(companyId));
        Assert.True(snapshot.CanAccessBranch(companyId, branchId));
        Assert.True(snapshot.CanAccessProject(companyId, branchId, projectId));
    }

    [Fact]
    public void ProjectScopeShowsParentsButDoesNotGrantSiblingProject()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var siblingProjectId = Guid.NewGuid();
        var snapshot = new CurrentDataScopeSnapshot(
            false,
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid> { projectId },
            new HashSet<Guid> { companyId },
            new HashSet<Guid> { branchId });
        var projects = new[]
        {
            new Project
            {
                Id = projectId,
                CompanyId = companyId,
                BranchId = branchId
            },
            new Project
            {
                Id = siblingProjectId,
                CompanyId = companyId,
                BranchId = branchId
            }
        }.AsQueryable();

        Assert.Single(snapshot.Apply(projects));
        Assert.False(
            snapshot.CanAccessProject(
                companyId,
                branchId,
                siblingProjectId));
    }
}
