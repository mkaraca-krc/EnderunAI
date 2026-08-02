namespace EnderunAI.Api.Models;

public sealed class HrShiftDefinition : BaseEntity
{
    public Guid CompanyId { get; set; }

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public decimal BreakHours { get; set; }
    public decimal DailyWorkingHours { get; set; }
    public bool IsNightShift { get; set; }

    public string? Description { get; set; }
}

public sealed class HrShiftAssignment : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid PersonnelId { get; set; }
    public Guid ShiftDefinitionId { get; set; }
    public Guid? ProjectId { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    public string? TeamName { get; set; }
    public string? Description { get; set; }
}
