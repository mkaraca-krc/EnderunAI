namespace EnderunAI.Api.Contracts;

public sealed record HizirDashboardSnapshot(
    DateTime GeneratedAtUtc,
    HizirProjectSummary Projects,
    HizirPurchasingSummary Purchasing,
    HizirPersonnelSummary Personnel,
    HizirDocumentSummary Documents,
    HizirFinanceSummary Finance,
    IReadOnlyList<string> CriticalItems);

public sealed record HizirProjectSummary(int Total, int Active, int AtRisk, int Overdue);
public sealed record HizirPurchasingSummary(int TotalRequests, int WaitingApproval, int Critical, int Overdue);
public sealed record HizirPersonnelSummary(int Total, int Active);
public sealed record HizirDocumentSummary(bool IsAvailable, int Incoming, int Outgoing, int WaitingAction);
public sealed record HizirFinanceSummary(bool IsAvailable, decimal? CashBalance, decimal? DuePayments, decimal? DueCollections);
