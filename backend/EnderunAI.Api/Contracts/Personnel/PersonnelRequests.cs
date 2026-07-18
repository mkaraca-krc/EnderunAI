namespace EnderunAI.Api.Contracts.Personnel;

public sealed record CreatePersonnelRequest(
    Guid CompanyId,
    Guid? BranchId,
    string EmployeeNumber,
    string FirstName,
    string LastName,
    string? IdentityNumber,
    DateTime? BirthDate,
    string? Phone,
    string? Email,
    string? Address,
    string? JobTitle,
    string? Profession,
    string? SgkRegistrationNumber,
    DateTime? EmploymentStartDate,
    decimal? MonthlySalary);

public sealed record UpdatePersonnelRequest(
    Guid? BranchId,
    string FirstName,
    string LastName,
    string? IdentityNumber,
    DateTime? BirthDate,
    string? Phone,
    string? Email,
    string? Address,
    string? JobTitle,
    string? Profession,
    string? SgkRegistrationNumber,
    DateTime? EmploymentStartDate,
    DateTime? EmploymentEndDate,
    decimal? MonthlySalary,
    int Status,
    bool IsActive);

public sealed record AssignPersonnelRequest(
    Guid ProjectId,
    DateTime StartDate,
    DateTime? EndDate,
    string? Role,
    string? Notes,
    bool IsPrimaryAssignment);
