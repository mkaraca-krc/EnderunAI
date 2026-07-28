namespace EnderunAI.Api.Contracts.Finance;

public sealed record FinanceDashboardResponse(
    decimal TotalContractAmount,
    decimal TotalProgressPaymentAmount,
    decimal TotalPriceDifferenceAmount,
    decimal TotalDeductionAmount,
    decimal TotalNetPayableAmount,
    int ActiveProjectCount,
    int ProgressPaymentCount);

public sealed record CurrentAccountFinanceSummaryResponse(
    decimal TotalReceivable,
    decimal TotalPayable,
    decimal NetBalance,
    int AccountCount);

public sealed record ProjectFinanceSummaryResponse(
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    decimal ContractAmount,
    decimal ProgressPaymentAmount,
    decimal NetPayableAmount,
    decimal RemainingAmount);

public sealed record CashFlowSummaryResponse(
    decimal TotalIncome,
    decimal TotalExpense,
    decimal NetCash);

public sealed record SupplierBalanceSummaryResponse(
    Guid SupplierId,
    string SupplierName,
    decimal TotalDebt,
    decimal TotalPaid,
    decimal Balance);
