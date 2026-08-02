namespace EnderunAI.Api.Models;

public enum DataScopeType
{
    All = 0,
    Company = 1,
    Branch = 2,
    Project = 3,
    Site = 4
}

public sealed class UserDataScope : BaseEntity
{
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public DataScopeType ScopeType { get; set; }

    public Guid? CompanyId { get; set; }
    public Company? Company { get; set; }

    public Guid? BranchId { get; set; }
    public Branch? Branch { get; set; }

    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public Guid? ProjectSiteId { get; set; }
    public ProjectSite? ProjectSite { get; set; }
}
