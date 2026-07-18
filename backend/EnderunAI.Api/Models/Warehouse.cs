namespace EnderunAI.Api.Models;

public enum WarehouseType
{
    Central = 0,
    Site = 1,
    Vehicle = 2,
    Temporary = 3
}

public sealed class Warehouse : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public WarehouseType Type { get; set; }
    public string? Address { get; set; }

    public Guid? ResponsibleUserId { get; set; }
}
