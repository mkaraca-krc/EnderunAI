namespace EnderunAI.Api.Contracts.Accounting;

public sealed record AccountingVoucherLineRequest(
    Guid AccountingAccountId,
    string? Description,
    decimal DebitAmount,
    decimal CreditAmount,
    string CurrencyCode,
    decimal ExchangeRate,
    Guid? CurrentAccountId,
    Guid? ProjectId,
    Guid? ProjectHierarchyNodeId,
    string? CostCenterCode,
    string? DocumentNumber,
    DateTime? DocumentDate,
    DateTime? DueDate);

public sealed record CreateAccountingVoucherRequest(
    Guid CompanyId,
    int VoucherType,
    DateTime VoucherDate,
    string CurrencyCode,
    decimal ExchangeRate,
    string? Description,
    string? ReferenceNumber,
    string? SourceModule,
    Guid? SourceEntityId,
    IReadOnlyCollection<AccountingVoucherLineRequest> Lines);

public sealed record UpdateAccountingVoucherRequest(
    int VoucherType,
    DateTime VoucherDate,
    string CurrencyCode,
    decimal ExchangeRate,
    string? Description,
    string? ReferenceNumber,
    IReadOnlyCollection<AccountingVoucherLineRequest> Lines);

public sealed record CancelAccountingVoucherRequest(string Reason);

public sealed record AccountingVoucherLineResponse(
    Guid Id,
    int LineNumber,
    Guid AccountingAccountId,
    string AccountCode,
    string AccountName,
    string? Description,
    decimal DebitAmount,
    decimal CreditAmount,
    string CurrencyCode,
    decimal ExchangeRate,
    decimal DebitAmountLocal,
    decimal CreditAmountLocal,
    Guid? CurrentAccountId,
    string? CurrentAccountTitle,
    Guid? ProjectId,
    string? ProjectCode,
    string? ProjectName,
    Guid? ProjectHierarchyNodeId,
    string? ProjectHierarchyNodeCode,
    string? ProjectHierarchyNodeName,
    string? CostCenterCode,
    string? DocumentNumber,
    DateTime? DocumentDate,
    DateTime? DueDate);

public sealed record AccountingVoucherListItemResponse(
    Guid Id,
    Guid CompanyId,
    string VoucherNumber,
    int VoucherType,
    int Status,
    DateTime VoucherDate,
    int FiscalYear,
    int FiscalPeriod,
    string CurrencyCode,
    decimal ExchangeRate,
    string? Description,
    string? ReferenceNumber,
    string? SourceModule,
    decimal TotalDebit,
    decimal TotalCredit,
    int LineCount);

public sealed record AccountingVoucherDetailResponse(
    Guid Id,
    Guid CompanyId,
    string VoucherNumber,
    int VoucherType,
    int Status,
    DateTime VoucherDate,
    int FiscalYear,
    int FiscalPeriod,
    string CurrencyCode,
    decimal ExchangeRate,
    string? Description,
    string? ReferenceNumber,
    string? SourceModule,
    Guid? SourceEntityId,
    decimal TotalDebit,
    decimal TotalCredit,
    DateTime? PostedAtUtc,
    DateTime? CancelledAtUtc,
    string? CancellationReason,
    IReadOnlyCollection<AccountingVoucherLineResponse> Lines);

public sealed record AccountingVoucherActionResponse(
    Guid Id,
    string VoucherNumber,
    int Status,
    string Message);
