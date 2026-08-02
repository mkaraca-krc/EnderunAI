namespace EnderunAI.Api.Models;

public enum EngineeringPositionSource { Official = 0, Enderun = 1 }
public enum EngineeringPositionDiscipline
{
    Electrical = 0, MediumVoltage = 1, LowCurrent = 2,
    DataCenter = 3, Fiber = 4, Mechanical = 5, Civil = 6, Other = 99
}
public enum EngineeringPositionStatus { Draft = 0, Active = 1, Passive = 2, Archived = 3 }

public sealed class EngineeringPosition : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;

    public EngineeringPositionSource Source { get; set; }
    public EngineeringPositionDiscipline Discipline { get; set; }
    public EngineeringPositionStatus Status { get; set; } = EngineeringPositionStatus.Draft;

    public string? OfficialInstitution { get; set; }
    public string? OfficialCode { get; set; }
    public string? Category { get; set; }
    public string? Description { get; set; }
    public string? TechnicalSpecification { get; set; }
    public string? SearchKeywords { get; set; }

    public int RevisionNumber { get; set; }
    public string RevisionCode => $"R{RevisionNumber:00}";

    public decimal DefaultLaborHours { get; set; }
    public decimal DefaultHelperHours { get; set; }
    public decimal DefaultMachineHours { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
}
