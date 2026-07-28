namespace EnderunAI.Api.Models;

public sealed class PersonnelAssignment : BaseEntity
{
    public Guid PersonnelId { get; set; }
    public Personnel Personnel { get; set; } = null!;

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    public string? Role { get; set; }
    public string? Notes { get; set; }

    public bool IsPrimaryAssignment { get; set; }
}
