namespace EnderunAI.Api.Contracts.Accounting;

public sealed record AccountingAccountListItemResponse(
    Guid Id,
    Guid CompanyId,
    Guid? ParentAccountId,
    string Code,
    string Name,
    int Nature,
    int Level,
    bool IsPostingAllowed,
    bool RequiresProject,
    bool RequiresCostCenter,
    string? CurrencyCode,
    bool IsActive,
    int ChildCount);

public sealed record AccountingAccountDetailResponse(
    Guid Id,
    Guid CompanyId,
    Guid? ParentAccountId,
    string Code,
    string Name,
    string? Description,
    int Nature,
    int Level,
    bool IsPostingAllowed,
    bool RequiresProject,
    bool RequiresCostCenter,
    string? CurrencyCode,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);

public sealed record CreateAccountingAccountRequest(
    Guid CompanyId,
    Guid? ParentAccountId,
    string Code,
    string Name,
    string? Description,
    int Nature,
    bool IsPostingAllowed,
    bool RequiresProject,
    bool RequiresCostCenter,
    string? CurrencyCode);

public sealed record UpdateAccountingAccountRequest(
    Guid? ParentAccountId,
    string Code,
    string Name,
    string? Description,
    int Nature,
    bool IsPostingAllowed,
    bool RequiresProject,
    bool RequiresCostCenter,
    string? CurrencyCode,
    bool IsActive);
