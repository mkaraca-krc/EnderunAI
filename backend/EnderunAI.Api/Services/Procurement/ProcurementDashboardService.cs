using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Procurement;

public sealed record ProcurementDashboardCounters(
    int PendingPurchaseRequests,
    int OpenRfqs,
    int OffersUnderEvaluation,
    int PendingApprovals,
    int OpenPurchaseOrders,
    int PendingGoodsReceipts,
    int CriticalBudgetAlerts,
    int CriticalSupplierRisks);

public sealed record ProcurementFinancialKpis(
    decimal TotalPurchaseVolume,
    decimal CurrentMonthPurchaseVolume,
    decimal AverageOrderAmount,
    int TotalOrderCount,
    int TotalOfferCount,
    decimal AverageOffersPerRfq);

public sealed record ProcurementMonthlyTrend(int Year, int Month, decimal Amount, int OrderCount);
public sealed record ProcurementStatusSlice(string Status, int Count, decimal Amount);
public sealed record ProcurementProjectKpi(Guid ProjectId, string ProjectName, decimal PurchaseVolume, int OrderCount);
public sealed record ProcurementSupplierKpi(Guid SupplierId, string SupplierName, decimal PurchaseVolume, int OrderCount, decimal? PerformanceScore, string? RiskLevel);
public sealed record ProcurementBudgetKpi(Guid ProjectId, string ProjectName, decimal Budget, decimal Committed, decimal Actual, decimal Remaining, decimal UsageRate, string Status);
public sealed record ProcurementApprovalKpis(int PendingCount, int CriticalPendingCount, decimal AverageCompletionHours, int RevisionRequestedCount);

public sealed record ProcurementDashboardSnapshot(
    DateTime GeneratedAtUtc,
    ProcurementDashboardCounters Counters,
    ProcurementFinancialKpis Financial,
    ProcurementApprovalKpis Approvals,
    IReadOnlyList<ProcurementMonthlyTrend> MonthlyTrend,
    IReadOnlyList<ProcurementStatusSlice> OrderStatusDistribution,
    IReadOnlyList<ProcurementProjectKpi> TopProjects,
    IReadOnlyList<ProcurementSupplierKpi> TopSuppliers,
    IReadOnlyList<ProcurementBudgetKpi> Budgets);

public interface IProcurementDashboardService
{
    Task<ProcurementDashboardSnapshot> GetAsync(Guid companyId, int months = 12, CancellationToken cancellationToken = default);
}

public sealed class ProcurementDashboardService(
    AppDbContext appDb,
    ProcurementDbContext procurementDb,
    ProcurementApprovalDbContext approvalDb,
    ProjectBudgetDbContext budgetDb,
    SupplierPerformanceDbContext supplierDb) : IProcurementDashboardService
{
    public async Task<ProcurementDashboardSnapshot> GetAsync(Guid companyId, int months = 12, CancellationToken cancellationToken = default)
    {
        months = Math.Clamp(months, 1, 36);
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var trendStart = monthStart.AddMonths(-(months - 1));

        var orders = await appDb.PurchaseOrders
            .AsNoTracking()
            .Include(x => x.Items)
            .Where(x => x.CompanyId == companyId)
            .ToListAsync(cancellationToken);

        static decimal OrderAmount(PurchaseOrder order) =>
            order.Items.Sum(x => x.Quantity * x.UnitPrice * (1m - x.DiscountRate / 100m)) * order.ExchangeRate;

        var orderAmounts = orders.ToDictionary(x => x.Id, OrderAmount);
        var totalVolume = orderAmounts.Values.Sum();
        var currentMonthVolume = orders
            .Where(x => x.OrderDateUtc >= monthStart)
            .Sum(x => orderAmounts[x.Id]);

        var pendingRequests = await appDb.PurchaseRequests.CountAsync(
            x => x.CompanyId == companyId && x.Status == PurchaseRequestStatus.PendingApproval,
            cancellationToken);

        var openRfqs = await procurementDb.Rfqs.CountAsync(
            x => x.CompanyId == companyId &&
                 x.Status != RfqStatus.Awarded &&
                 x.Status != RfqStatus.Cancelled,
            cancellationToken);

        var rfqIds = await procurementDb.Rfqs
            .Where(x => x.CompanyId == companyId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var offerCount = rfqIds.Count == 0
            ? 0
            : await procurementDb.SupplierOffers.CountAsync(x => rfqIds.Contains(x.RfqId), cancellationToken);

        var offersUnderEvaluation = await procurementDb.Rfqs.CountAsync(
            x => x.CompanyId == companyId && x.Status == RfqStatus.Evaluating,
            cancellationToken);

        var pendingApprovals = await approvalDb.Instances.CountAsync(
            x => x.CompanyId == companyId && x.Status == ApprovalInstanceStatus.Pending,
            cancellationToken);

        var criticalApprovalCutoff = now.AddHours(-72);
        var criticalPendingApprovals = await approvalDb.Instances.CountAsync(
            x => x.CompanyId == companyId &&
                 x.Status == ApprovalInstanceStatus.Pending &&
                 x.SubmittedAtUtc <= criticalApprovalCutoff,
            cancellationToken);

        var completedApprovals = await approvalDb.Instances
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.CompletedAtUtc.HasValue)
            .Select(x => new { x.SubmittedAtUtc, CompletedAtUtc = x.CompletedAtUtc!.Value })
            .ToListAsync(cancellationToken);

        var averageApprovalHours = completedApprovals.Count == 0
            ? 0m
            : (decimal)completedApprovals.Average(x => (x.CompletedAtUtc - x.SubmittedAtUtc).TotalHours);

        var revisionCount = await approvalDb.Instances.CountAsync(
            x => x.CompanyId == companyId && x.Status == ApprovalInstanceStatus.RevisionRequested,
            cancellationToken);

        var openOrders = orders.Count(x => x.Status is PurchaseOrderStatus.Approved or PurchaseOrderStatus.PartiallyReceived);
        var pendingGoodsReceipts = await appDb.GoodsReceipts.CountAsync(
            x => x.CompanyId == companyId && x.Status == GoodsReceiptStatus.Draft,
            cancellationToken);

        var criticalBudgetAlerts = await budgetDb.Alerts.CountAsync(
            x => x.CompanyId == companyId && !x.IsResolved && x.Level == BudgetAlertLevel.Critical,
            cancellationToken);

        var criticalSupplierRisks = await supplierDb.Snapshots.CountAsync(
            x => x.CompanyId == companyId && x.RiskLevel == SupplierRiskLevel.Critical,
            cancellationToken);

        var counters = new ProcurementDashboardCounters(
            pendingRequests,
            openRfqs,
            offersUnderEvaluation,
            pendingApprovals,
            openOrders,
            pendingGoodsReceipts,
            criticalBudgetAlerts,
            criticalSupplierRisks);

        var financial = new ProcurementFinancialKpis(
            decimal.Round(totalVolume, 2),
            decimal.Round(currentMonthVolume, 2),
            orders.Count == 0 ? 0 : decimal.Round(totalVolume / orders.Count, 2),
            orders.Count,
            offerCount,
            rfqIds.Count == 0 ? 0 : decimal.Round((decimal)offerCount / rfqIds.Count, 2));

        var monthlyTrend = Enumerable.Range(0, months)
            .Select(offset => trendStart.AddMonths(offset))
            .Select(period =>
            {
                var periodEnd = period.AddMonths(1);
                var periodOrders = orders.Where(x => x.OrderDateUtc >= period && x.OrderDateUtc < periodEnd).ToList();
                return new ProcurementMonthlyTrend(
                    period.Year,
                    period.Month,
                    decimal.Round(periodOrders.Sum(x => orderAmounts[x.Id]), 2),
                    periodOrders.Count);
            })
            .ToList();

        var statusDistribution = orders
            .GroupBy(x => x.Status)
            .Select(group => new ProcurementStatusSlice(
                group.Key.ToString(),
                group.Count(),
                decimal.Round(group.Sum(x => orderAmounts[x.Id]), 2)))
            .OrderByDescending(x => x.Amount)
            .ToList();

        var projectNames = await appDb.Projects
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var topProjects = orders
            .GroupBy(x => x.ProjectId)
            .Select(group => new ProcurementProjectKpi(
                group.Key,
                projectNames.GetValueOrDefault(group.Key, "Bilinmeyen proje"),
                decimal.Round(group.Sum(x => orderAmounts[x.Id]), 2),
                group.Count()))
            .OrderByDescending(x => x.PurchaseVolume)
            .Take(10)
            .ToList();

        var supplierIds = orders.Select(x => x.SupplierCurrentAccountId).Distinct().ToList();
        var supplierNames = await appDb.CurrentAccounts
            .AsNoTracking()
            .Where(x => supplierIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Title, cancellationToken);

        var latestScores = await supplierDb.Snapshots
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && supplierIds.Contains(x.SupplierCurrentAccountId))
            .OrderByDescending(x => x.PeriodEndUtc)
            .ToListAsync(cancellationToken);

        var latestScoreBySupplier = latestScores
            .GroupBy(x => x.SupplierCurrentAccountId)
            .ToDictionary(x => x.Key, x => x.First());

        var topSuppliers = orders
            .GroupBy(x => x.SupplierCurrentAccountId)
            .Select(group =>
            {
                latestScoreBySupplier.TryGetValue(group.Key, out var score);
                return new ProcurementSupplierKpi(
                    group.Key,
                    supplierNames.GetValueOrDefault(group.Key, "Bilinmeyen tedarikçi"),
                    decimal.Round(group.Sum(x => orderAmounts[x.Id]), 2),
                    group.Count(),
                    score?.OverallScore,
                    score?.RiskLevel.ToString());
            })
            .OrderByDescending(x => x.PurchaseVolume)
            .Take(10)
            .ToList();

        var activeBudgets = await budgetDb.Budgets
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status == BudgetStatus.Active)
            .ToListAsync(cancellationToken);

        var budgets = new List<ProcurementBudgetKpi>();
        foreach (var budget in activeBudgets)
        {
            var committed = await budgetDb.Consumptions
                .Where(x => x.ProjectBudgetId == budget.Id && x.Type == BudgetConsumptionType.Commitment)
                .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;
            var actual = await budgetDb.Consumptions
                .Where(x => x.ProjectBudgetId == budget.Id && x.Type == BudgetConsumptionType.Actual)
                .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;
            var used = committed + actual;
            var remaining = budget.BaseAmount - used;
            var usageRate = budget.BaseAmount <= 0 ? 0 : used / budget.BaseAmount * 100m;
            var status = usageRate >= budget.CriticalThresholdPercent
                ? "Critical"
                : usageRate >= budget.WarningThresholdPercent ? "Warning" : "Healthy";
            budgets.Add(new ProcurementBudgetKpi(
                budget.ProjectId,
                projectNames.GetValueOrDefault(budget.ProjectId, "Bilinmeyen proje"),
                decimal.Round(budget.BaseAmount, 2),
                decimal.Round(committed, 2),
                decimal.Round(actual, 2),
                decimal.Round(remaining, 2),
                decimal.Round(usageRate, 2),
                status));
        }

        var approvals = new ProcurementApprovalKpis(
            pendingApprovals,
            criticalPendingApprovals,
            decimal.Round(averageApprovalHours, 2),
            revisionCount);

        return new ProcurementDashboardSnapshot(
            now,
            counters,
            financial,
            approvals,
            monthlyTrend,
            statusDistribution,
            topProjects,
            topSuppliers,
            budgets.OrderByDescending(x => x.UsageRate).ToList());
    }
}
