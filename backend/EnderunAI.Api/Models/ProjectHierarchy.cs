namespace EnderunAI.Api.Models;

public enum ProjectModuleType
{
    Hakedis = 0,
    Personnel = 1,
    Warehouse = 2,
    Purchasing = 3,
    Finance = 4
}

public sealed class ProjectHierarchyLevel : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; }

    public ICollection<ProjectHierarchyNode> Nodes { get; set; }
        = new List<ProjectHierarchyNode>();
}

public sealed class ProjectHierarchyNode : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid LevelId { get; set; }
    public ProjectHierarchyLevel Level { get; set; } = null!;

    public Guid? ParentNodeId { get; set; }
    public ProjectHierarchyNode? ParentNode { get; set; }

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }

    public ICollection<ProjectHierarchyNode> ChildNodes { get; set; }
        = new List<ProjectHierarchyNode>();

    public ICollection<ProjectModuleScope> ModuleScopes { get; set; }
        = new List<ProjectModuleScope>();
}

public sealed class ProjectModuleScope : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid ProjectHierarchyNodeId { get; set; }
    public ProjectHierarchyNode ProjectHierarchyNode { get; set; } = null!;

    public ProjectModuleType ModuleType { get; set; }
    public Guid RecordId { get; set; }
}
