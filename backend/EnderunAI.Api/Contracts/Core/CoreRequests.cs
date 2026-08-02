using EnderunAI.Api.Models;

namespace EnderunAI.Api.Contracts.Core;

public sealed record CreateCompanyRequest(
    string Code,
    string Name,
    string? TradeName,
    string? TaxOffice,
    string? TaxNumber,
    string? Phone,
    string? Email,
    string? Website,
    string? Address);

public sealed record UpdateCompanyRequest(
    string Name,
    string? TradeName,
    string? TaxOffice,
    string? TaxNumber,
    string? Phone,
    string? Email,
    string? Website,
    string? Address,
    bool IsActive);

public sealed record CreateBranchRequest(
    Guid CompanyId,
    string Code,
    string Name,
    string? Phone,
    string? Email,
    string? Address,
    bool IsHeadOffice);

public sealed record CreateCurrentAccountRequest(
    Guid CompanyId,
    string Code,
    string Title,
    string? ShortName,
    int Roles,
    string? TaxOffice,
    string? TaxNumber,
    string? AuthorizedPerson,
    string? Phone,
    string? Email,
    string? Address,
    string? PaymentTerm,
    decimal? CreditLimit);

public sealed record CreateProjectRequest(
    Guid CompanyId,
    Guid BranchId,
    Guid? EmployerCurrentAccountId,
    string Code,
    string Name,
    string? ContractNumber,
    DateTime? ContractDate,
    decimal? ContractAmount,
    string CurrencyCode,
    decimal VatRate,
    string? WithholdingRate,
    decimal IncreaseRate,
    decimal CashRetentionRate,
    decimal WithholdingTaxRate,
    decimal MaterialDeductionRate,
    DateTime? PlannedStartDate,
    DateTime? PlannedEndDate,
    string? City,
    string? District,
    string? Address,
    ProjectStatus Status = ProjectStatus.Kesif);

public sealed record UpdateProjectRequest(
    string Name,
    Guid? EmployerCurrentAccountId,
    string? ContractNumber,
    DateTime? ContractDate,
    decimal? ContractAmount,
    string CurrencyCode,
    decimal VatRate,
    string? WithholdingRate,
    decimal IncreaseRate,
    decimal CashRetentionRate,
    decimal WithholdingTaxRate,
    decimal MaterialDeductionRate,
    DateTime? PlannedStartDate,
    DateTime? PlannedEndDate,
    string? City,
    string? District,
    string? Address,
    ProjectStatus Status);
