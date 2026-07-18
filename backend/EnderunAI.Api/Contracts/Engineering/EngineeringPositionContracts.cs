namespace EnderunAI.Api.Contracts.Engineering;

public sealed record CreateEngineeringPositionRequest(
    Guid CompanyId, string? Code, string Name, string Unit,
    int Source, int Discipline, string? OfficialInstitution,
    string? OfficialCode, string? Category, string? Description,
    string? TechnicalSpecification, string? SearchKeywords,
    decimal DefaultLaborHours, decimal DefaultHelperHours,
    decimal DefaultMachineHours);

public sealed record UpdateEngineeringPositionRequest(
    string Name, string Unit, int Discipline, int Status,
    string? OfficialInstitution, string? OfficialCode,
    string? Category, string? Description,
    string? TechnicalSpecification, string? SearchKeywords,
    decimal DefaultLaborHours, decimal DefaultHelperHours,
    decimal DefaultMachineHours);

public sealed record ChangeEngineeringPositionStatusRequest(int Status);
