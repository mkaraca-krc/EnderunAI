namespace EnderunAI.Api.Contracts.Accounting;

public sealed record CreateCashAccountRequest(
    Guid CompanyId,
    int Type,
    string Code,
    string Name,
    string? BankName,
    string? Iban,
    string CurrencyCode,
    decimal OpeningBalance,
    Guid AccountingAccountId);

public sealed record UpdateCashAccountRequest(
    string Name,
    string? BankName,
    string? Iban,
    decimal OpeningBalance,
    Guid AccountingAccountId,
    bool IsActive);

public sealed record CreateCashTransactionRequest(
    DateTime TransactionDate,
    int TransactionType,
    int Direction,
    decimal Amount,
    string CurrencyCode,
    string Description,
    string? DocumentNumber,
    Guid? CurrentAccountId,
    Guid? ProjectId);

public sealed record CashAccountResponse(
    Guid Id,
    Guid CompanyId,
    int Type,
    string TypeName,
    string Code,
    string Name,
    string? BankName,
    string? Iban,
    string CurrencyCode,
    decimal OpeningBalance,
    Guid AccountingAccountId,
    string AccountingAccountCode,
    string AccountingAccountName,
    decimal TotalIn,
    decimal TotalOut,
    decimal Balance,
    int MovementCount,
    bool IsActive);

public sealed record CashTransactionResponse(
    Guid Id,
    Guid CashAccountId,
    DateTime TransactionDate,
    int TransactionType,
    string TransactionTypeName,
    int Direction,
    decimal Amount,
    string CurrencyCode,
    string Description,
    string? DocumentNumber,
    Guid? CurrentAccountId,
    string? CurrentAccountTitle,
    Guid? ProjectId,
    string? ProjectCode,
    string? SourceModule,
    Guid? SourceEntityId,
    Guid? AccountingVoucherId,
    string? AccountingVoucherNumber,
    decimal RunningBalance);

public sealed record CashAccountStatementResponse(
    Guid CashAccountId,
    string Code,
    string Name,
    string CurrencyCode,
    decimal OpeningBalance,
    decimal PeriodOpeningBalance,
    decimal TotalIn,
    decimal TotalOut,
    decimal ClosingBalance,
    IReadOnlyCollection<CashTransactionResponse> Transactions);
