using EnderunAI.Api.Contracts.Projects;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Projects;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EnderunAI.Api.Tests.Projects;

public sealed class ProjectHierarchyServiceTests
{
    [Fact]
    public async Task ApplyMkeTemplate_CreatesExpectedTree()
    {
        await using var db = CreateDbContext();
        var project = AddProject(db);
        await db.SaveChangesAsync();
        var service = new ProjectHierarchyService(db);

        var result = await service.ApplyMkeTemplateAsync(
            project.Id,
            CancellationToken.None);

        Assert.Equal(2, result.CreatedLevelCount);
        Assert.Equal(8, result.CreatedNodeCount);
        Assert.Equal(3, result.Hierarchy.Nodes.Count);

        var kirikkale = Assert.Single(
            result.Hierarchy.Nodes,
            node => node.Name == "Kırıkkale");
        Assert.Equal(
            new[] { "Ar-Ge", "Barut", "Mühimmat" },
            kirikkale.Children
                .Select(node => node.Name)
                .OrderBy(name => name)
                .ToArray());

        var ankara = Assert.Single(
            result.Hierarchy.Nodes,
            node => node.Name == "Ankara");
        Assert.Equal(
            "Gazi Fişek",
            Assert.Single(ankara.Children).Name);
    }

    [Fact]
    public async Task CreateNode_RejectsParentFromSameLevel()
    {
        await using var db = CreateDbContext();
        var project = AddProject(db);
        await db.SaveChangesAsync();
        var service = new ProjectHierarchyService(db);

        var cityLevel = await service.CreateLevelAsync(
            project.Id,
            new CreateProjectHierarchyLevelRequest(
                "SEHIR",
                "Şehir",
                10,
                true),
            CancellationToken.None);

        var root = await service.CreateNodeAsync(
            project.Id,
            new CreateProjectHierarchyNodeRequest(
                cityLevel.Id,
                null,
                "ANKARA",
                "Ankara",
                null,
                10),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ProjectHierarchyException>(
            () => service.CreateNodeAsync(
                project.Id,
                new CreateProjectHierarchyNodeRequest(
                    cityLevel.Id,
                    root.Id,
                    "CANKAYA",
                    "Çankaya",
                    null,
                    10),
                CancellationToken.None));

        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public async Task AssignModuleScope_UpdatesTreeCounts()
    {
        await using var db = CreateDbContext();
        var project = AddProject(db);
        await db.SaveChangesAsync();
        var service = new ProjectHierarchyService(db);
        var template = await service.ApplyMkeTemplateAsync(
            project.Id,
            CancellationToken.None);
        var gaziFishek = template.Hierarchy.Nodes
            .Single(node => node.Name == "Ankara")
            .Children
            .Single();
        var hakedisId = Guid.NewGuid();

        var scope = await service.AssignModuleScopeAsync(
            project.Id,
            gaziFishek.Id,
            new AssignProjectModuleScopeRequest(
                ProjectModuleType.Hakedis,
                hakedisId),
            CancellationToken.None);
        var tree = await service.GetTreeAsync(
            project.Id,
            CancellationToken.None);
        var updatedNode = tree.Nodes
            .Single(node => node.Name == "Ankara")
            .Children
            .Single();

        Assert.Equal("Ankara / Gazi Fişek", scope.NodePath);
        var count = Assert.Single(updatedNode.ModuleScopes);
        Assert.Equal(ProjectModuleType.Hakedis, count.ModuleType);
        Assert.Equal(1, count.Count);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"project-hierarchy-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private static Project AddProject(AppDbContext db)
    {
        var project = new Project
        {
            CompanyId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            EmployerCurrentAccountId = Guid.NewGuid(),
            Code = "MKE",
            Name = "MKE Projesi"
        };
        db.Projects.Add(project);
        return project;
    }
}
