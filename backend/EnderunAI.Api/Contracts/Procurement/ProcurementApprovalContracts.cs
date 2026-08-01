namespace EnderunAI.Api.Contracts.Procurement;

public sealed record ConfigureProcurementApprovalPolicyRequest(
    decimal PurchasingApprovalLimitTry,
    decimal FinanceApprovalLimitTry,
    bool RequireBudget,
    string? Note);

public sealed record ProcurementApprovalPolicyResponse(
    Guid VersionId,
    Guid CompanyId,
    decimal PurchasingApprovalLimitTry,
    decimal FinanceApprovalLimitTry,
    bool RequireBudget,
    string? Note,
    string? UpdatedBy,
    DateTime UpdatedAtUtc);

public sealed record UpsertProcurementBudgetRequest(
    string Name,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    decimal AmountTry,
    decimal WarningThresholdPercent,
    bool IsActive,
    string? Note);

public sealed record ProcurementBudgetResponse(
    Guid BudgetId,
    Guid VersionId,
    Guid CompanyId,
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    string Name,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    decimal AmountTry,
    decimal WarningThresholdPercent,
    bool IsActive,
    string? Note,
    string? UpdatedBy,
    DateTime UpdatedAtUtc,
    decimal CommittedAmountTry,
    decimal RemainingAmountTry,
    decimal UtilizationPercent,
    bool IsWarning,
    bool IsExceeded);

public sealed record PurchaseOrderApprovalStepResponse(
    int Sequence,
    string Code,
    string Name,
    string RequiredAuthority,
    string Status,
    Guid? DecidedByUserId,
    string? DecidedByUsername,
    DateTime? DecidedAtUtc,
    string? Note);

public sealed record PurchaseOrderApprovalContextResponse(
    Guid PurchaseOrderId,
    string OrderNumber,
    Guid CompanyId,
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    int OrderStatus,
    decimal OrderAmountTry,
    bool PolicyConfigured,
    ProcurementApprovalPolicyResponse? Policy,
    ProcurementBudgetResponse? Budget,
    decimal? BudgetAmountAfterOrderTry,
    decimal? BudgetRemainingAfterOrderTry,
    bool BudgetAllowsOrder,
    Guid? PlanId,
    int? CurrentStageSequence,
    string? CurrentStageName,
    bool CanCurrentUserApprove,
    IReadOnlyList<PurchaseOrderApprovalStepResponse> Steps,
    IReadOnlyList<string> Warnings);

public sealed record ProcurementPendingApprovalResponse(
    Guid PurchaseOrderId,
    string OrderNumber,
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    string SupplierTitle,
    DateTime OrderDate,
    decimal OrderAmountTry,
    int CurrentStageSequence,
    string CurrentStageName,
    string RequiredAuthority,
    bool CanCurrentUserApprove,
    bool BudgetWarning,
    decimal? BudgetRemainingAfterOrderTry);

public sealed record ProcurementApprovalDashboardResponse(
    Guid CompanyId,
    string CompanyCode,
    string CompanyName,
    ProcurementApprovalPolicyResponse? Policy,
    IReadOnlyList<ProcurementBudgetResponse> Budgets,
    IReadOnlyList<ProcurementPendingApprovalResponse> PendingApprovals,
    int PendingApprovalCount,
    int ApprovalsCurrentUserCanActOn,
    decimal PendingApprovalAmountTry,
    int BudgetWarningCount,
    IReadOnlyList<string> Warnings);

