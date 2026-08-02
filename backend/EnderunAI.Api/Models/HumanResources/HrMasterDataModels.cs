using EnderunAI.Api.Models;

namespace EnderunAI.Api.Models.HumanResources;

public sealed class HrSalaryDefinition : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid PersonnelId { get; set; }
    public DateTime EffectiveStartDate { get; set; }
    public DateTime? EffectiveEndDate { get; set; }
    public decimal GrossSalary { get; set; }
    public decimal NetSalary { get; set; }
    public decimal DailyRate { get; set; }
    public decimal HourlyRate { get; set; }
    public decimal OvertimeMultiplier { get; set; } = 1.5m;
    public decimal SundayMultiplier { get; set; } = 2m;
    public decimal PublicHolidayMultiplier { get; set; } = 2m;
    public string CurrencyCode { get; set; } = "TRY";
    public string? Description { get; set; }
}

public sealed class HrDepartment : BaseEntity
{
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid? ParentDepartmentId { get; set; }
    public Guid? ManagerPersonnelId { get; set; }
}

public sealed class HrPosition : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid DepartmentId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Level { get; set; }
    public bool IsManagerial { get; set; }
}
