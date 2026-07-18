namespace EnderunAI.Api.Models;

public enum PersonnelStatus
{
    Candidate = 0,
    Active = 1,
    OnLeave = 2,
    Suspended = 3,
    Terminated = 4
}

public sealed class Personnel : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid? BranchId { get; set; }
    public Branch? Branch { get; set; }

    public string EmployeeNumber { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    public string? IdentityNumber { get; set; }
    public DateTime? BirthDate { get; set; }

    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }

    public string? JobTitle { get; set; }
    public string? Profession { get; set; }
    public string? SgkRegistrationNumber { get; set; }

    public DateTime? EmploymentStartDate { get; set; }
    public DateTime? EmploymentEndDate { get; set; }

    public decimal? MonthlySalary { get; set; }
    public PersonnelStatus Status { get; set; } = PersonnelStatus.Active;

    public string FullName => $"{FirstName} {LastName}".Trim();

    public ICollection<PersonnelAssignment> Assignments { get; set; }
        = new List<PersonnelAssignment>();
}
