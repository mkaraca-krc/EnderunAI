namespace EnderunAI.Api.Contracts.Accounting;

public sealed record CreateChequeRequest(
    Guid CompanyId,
    int Direction,
    string ChequeNumber,
    string BankName,
    string? BankBranch,
    string? Drawer,
    Guid? CurrentAccountId,
    Guid? ProjectId,
    decimal Amount,
    string CurrencyCode,
    DateTime IssueDate,
    DateTime DueDate,
    Guid? ProgressPaymentId,
    Guid? SupplierInvoiceId,
    string? Description);

public sealed record UpdateChequeRequest(
    string ChequeNumber,
    string BankName,
    string? BankBranch,
    string? Drawer,
    Guid? CurrentAccountId,
    Guid? ProjectId,
    decimal Amount,
    DateTime IssueDate,
    DateTime DueDate,
    Guid? ProgressPaymentId,
    Guid? SupplierInvoiceId,
    string? Description);

/// <summary>
/// Durum geçişi. CashAccountId yalnızca para hareketi doğuran
/// geçişlerde zorunlu (bankaya verme, tahsil, ödeme, rücu).
/// </summary>
public sealed record ChequeStatusChangeRequest(
    int ToStatus,
    DateTime MovementDate,
    Guid? CashAccountId,
    string? Description);

public sealed record ChequeMovementResponse(
    Guid Id,
    DateTime MovementDate,
    int? FromStatus,
    string? FromStatusName,
    int ToStatus,
    string ToStatusName,
    string Description,
    Guid? CashAccountId,
    string? CashAccountName,
    Guid? AccountingVoucherId,
    string? AccountingVoucherNumber);

public sealed record ChequeListItemResponse(
    Guid Id,
    Guid CompanyId,
    int Direction,
    string DirectionName,
    int Status,
    string StatusName,
    string InternalNumber,
    string ChequeNumber,
    string BankName,
    string? Drawer,
    Guid? CurrentAccountId,
    string? CurrentAccountTitle,
    Guid? ProjectId,
    string? ProjectCode,
    decimal Amount,
    string CurrencyCode,
    DateTime IssueDate,
    DateTime DueDate,
    int DaysToDue,
    bool IsOverdue);

public sealed record ChequeDetailResponse(
    Guid Id,
    Guid CompanyId,
    int Direction,
    string DirectionName,
    int Status,
    string StatusName,
    string InternalNumber,
    string ChequeNumber,
    string BankName,
    string? BankBranch,
    string? Drawer,
    Guid? CurrentAccountId,
    string? CurrentAccountTitle,
    Guid? ProjectId,
    string? ProjectCode,
    string? ProjectName,
    decimal Amount,
    string CurrencyCode,
    DateTime IssueDate,
    DateTime DueDate,
    Guid? ProgressPaymentId,
    string? ProgressPaymentNumber,
    Guid? SupplierInvoiceId,
    string? SupplierInvoiceNumber,
    Guid? CashAccountId,
    string? CashAccountName,
    string? Description,
    IReadOnlyCollection<int> AllowedNextStatuses,
    IReadOnlyCollection<ChequeMovementResponse> Movements);

public sealed record ChequeSummaryResponse(
    decimal ReceivedPortfolioAmount,
    decimal ReceivedAtBankAmount,
    decimal ReceivedAtFactoringAmount,
    decimal ReceivedCollectedAmount,
    decimal ReceivedBouncedAmount,
    decimal IssuedOpenAmount,
    decimal IssuedPaidAmount,
    int ReceivedOpenCount,
    int IssuedOpenCount);
