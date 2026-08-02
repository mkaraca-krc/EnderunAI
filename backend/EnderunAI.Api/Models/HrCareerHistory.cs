namespace EnderunAI.Api.Models;

public enum HrCareerActionType
{
    Hire = 0,
    Promotion = 1,
    PositionChange = 2,
    DepartmentChange = 3,
    SalaryChange = 4,
    ProjectChange = 5,
    Terminate = 6
}

public sealed class HrCareerHistory : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid PersonnelId { get; set; }

    public HrCareerActionType ActionType { get; set; }
    public DateTime EffectiveDate { get; set; }

    public Guid? PreviousDepartmentId { get; set; }
    public Guid? NewDepartmentId { get; set; }
    public Guid? PreviousPositionId { get; set; }
    public Guid? NewPositionId { get; set; }
    public Guid? PreviousProjectId { get; set; }
    public Guid? NewProjectId { get; set; }

    public decimal? PreviousSalary { get; set; }
    public decimal? NewSalary { get; set; }

    public string? Reason { get; set; }
    public string? ApprovedByName { get; set; }
    public string? Notes { get; set; }
}
