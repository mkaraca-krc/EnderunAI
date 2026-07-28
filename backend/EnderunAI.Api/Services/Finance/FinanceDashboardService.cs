using EnderunAI.Api.Contracts.Finance;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Finance;

public sealed class FinanceDashboardService(AppDbContext db)
    : IFinanceDashboardService
{
    private static readonly string[] ProgressPaymentModules =
        ["HAKEDIS", "PROGRESS_PAYMENT"];
    private static readonly string[] PriceDifferenceModules =
        ["FIYAT_FARKI", "PRICE_DIFFERENCE"];
    private static readonly string[] DeductionModules =
        ["KESINTI", "DEDUCTION"];

    public async Task<FinanceDashboardResponse> GetDashboardAsync(
        Guid? companyId,
        Guid? projectId,
        Guid? hierarchyNodeId,
        CancellationToken cancellationToken)
    {
        var scopedProjectId = await ResolveProjectIdAsync(
            projectId,
            hierarchyNodeId,
            cancellationToken);
        var projects = BuildProjectQuery(companyId, scopedProjectId);
        var lines = BuildPostedLineQuery(
            companyId,
            scopedProjectId,
            hierarchyNodeId);

        var totalContractAmount = await projects
            .SumAsync(
                project => project.ContractAmount ?? 0m,
                cancellationToken);
        var activeProjectCount = await projects.CountAsync(
            project => project.Status == ProjectStatus.Active,
            cancellationToken);
        var progressPaymentAmount = await SumSourceModuleAsync(
            lines,
            ProgressPaymentModules,
            cancellationToken);
        var priceDifferenceAmount = await SumSourceModuleAsync(
            lines,
            PriceDifferenceModules,
            cancellationToken);
        var deductionAmount = await SumSourceModuleAsync(
            lines,
            DeductionModules,
            cancellationToken);
        var progressPaymentCount = await CountSourceModuleAsync(
            lines,
            ProgressPaymentModules,
            cancellationToken);

        return new FinanceDashboardResponse(
            totalContractAmount,
            progressPaymentAmount,
            priceDifferenceAmount,
            deductionAmount,
            progressPaymentAmount +
            priceDifferenceAmount -
            deductionAmount,
            activeProjectCount,
            progressPaymentCount);
    }

    public async Task<CurrentAccountFinanceSummaryResponse>
        GetCurrentAccountSummaryAsync(
            Guid? companyId,
            Guid? projectId,
            Guid? hierarchyNodeId,
            CancellationToken cancellationToken)
    {
        var scopedProjectId = await ResolveProjectIdAsync(
            projectId,
            hierarchyNodeId,
            cancellationToken);
        var lines = BuildPostedLineQuery(
                companyId,
                scopedProjectId,
                hierarchyNodeId)
            .Where(line => line.CurrentAccountId.HasValue);
        var balances = await lines
            .GroupBy(line => line.CurrentAccountId!.Value)
            .Select(group => new
            {
                Balance = group.Sum(
                    line => line.DebitAmountLocal -
                            line.CreditAmountLocal)
            })
            .ToListAsync(cancellationToken);

        var totalReceivable = balances
            .Where(item => item.Balance > 0)
            .Sum(item => item.Balance);
        var totalPayable = balances
            .Where(item => item.Balance < 0)
            .Sum(item => -item.Balance);

        return new CurrentAccountFinanceSummaryResponse(
            totalReceivable,
            totalPayable,
            totalReceivable - totalPayable,
            balances.Count);
    }

    public async Task<IReadOnlyCollection<ProjectFinanceSummaryResponse>>
        GetProjectsSummaryAsync(
            Guid? companyId,
            Guid? projectId,
            Guid? hierarchyNodeId,
            CancellationToken cancellationToken)
    {
        var scopedProjectId = await ResolveProjectIdAsync(
            projectId,
            hierarchyNodeId,
            cancellationToken);
        var projects = await BuildProjectQuery(
                companyId,
                scopedProjectId)
            .OrderBy(project => project.Code)
            .Select(project => new
            {
                project.Id,
                project.Code,
                project.Name,
                ContractAmount = project.ContractAmount ?? 0m
            })
            .ToListAsync(cancellationToken);

        if (projects.Count == 0)
            return [];

        var lines = BuildPostedLineQuery(
            companyId,
            scopedProjectId,
            hierarchyNodeId);
        var projectIds = projects
            .Select(project => project.Id)
            .ToArray();
        var totals = await lines
            .Where(line =>
                line.ProjectId.HasValue &&
                projectIds.Contains(line.ProjectId.Value))
            .GroupBy(line => new
            {
                VoucherId = line.AccountingVoucherId,
                ProjectId = line.ProjectId!.Value,
                line.AccountingVoucher.SourceModule
            })
            .Select(group => new ProjectVoucherTotal(
                group.Key.VoucherId,
                group.Key.ProjectId,
                group.Key.SourceModule,
                group.Sum(line => line.DebitAmountLocal)))
            .ToListAsync(cancellationToken);

        return projects
            .Select(project =>
            {
                var projectTotals = totals
                    .Where(item => item.ProjectId == project.Id)
                    .ToArray();
                var progressPayment = SumSourceModule(
                    projectTotals,
                    ProgressPaymentModules);
                var priceDifference = SumSourceModule(
                    projectTotals,
                    PriceDifferenceModules);
                var deduction = SumSourceModule(
                    projectTotals,
                    DeductionModules);
                var netPayable =
                    progressPayment + priceDifference - deduction;

                return new ProjectFinanceSummaryResponse(
                    project.Id,
                    project.Code,
                    project.Name,
                    project.ContractAmount,
                    progressPayment,
                    netPayable,
                    project.ContractAmount - netPayable);
            })
            .ToArray();
    }

    public async Task<CashFlowSummaryResponse> GetCashFlowAsync(
        Guid? companyId,
        Guid? projectId,
        Guid? hierarchyNodeId,
        CancellationToken cancellationToken)
    {
        var scopedProjectId = await ResolveProjectIdAsync(
            projectId,
            hierarchyNodeId,
            cancellationToken);
        var lines = BuildPostedLineQuery(
            companyId,
            scopedProjectId,
            hierarchyNodeId);
        var totalIncome = await lines
            .Where(line =>
                line.AccountingVoucher.VoucherType ==
                AccountingVoucherType.Collection)
            .SumAsync(
                line => line.DebitAmountLocal,
                cancellationToken);
        var totalExpense = await lines
            .Where(line =>
                line.AccountingVoucher.VoucherType ==
                AccountingVoucherType.Payment)
            .SumAsync(
                line => line.DebitAmountLocal,
                cancellationToken);

        return new CashFlowSummaryResponse(
            totalIncome,
            totalExpense,
            totalIncome - totalExpense);
    }

    public async Task<IReadOnlyCollection<SupplierBalanceSummaryResponse>>
        GetSuppliersSummaryAsync(
            Guid? companyId,
            Guid? projectId,
            Guid? hierarchyNodeId,
            CancellationToken cancellationToken)
    {
        var scopedProjectId = await ResolveProjectIdAsync(
            projectId,
            hierarchyNodeId,
            cancellationToken);
        var lines = BuildPostedLineQuery(
                companyId,
                scopedProjectId,
                hierarchyNodeId)
            .Where(line =>
                line.CurrentAccountId.HasValue &&
                (line.CurrentAccount!.Roles &
                 CurrentAccountRoles.Supplier) != 0);
        var balances = await lines
            .GroupBy(line => new
            {
                SupplierId = line.CurrentAccountId!.Value,
                SupplierName = line.CurrentAccount!.Title
            })
            .Select(group => new
            {
                group.Key.SupplierId,
                group.Key.SupplierName,
                TotalDebt = group.Sum(
                    line => line.CreditAmountLocal),
                TotalPaid = group.Sum(
                    line => line.DebitAmountLocal)
            })
            .OrderByDescending(item =>
                item.TotalDebt - item.TotalPaid)
            .ToListAsync(cancellationToken);

        return balances
            .Select(item => new SupplierBalanceSummaryResponse(
                item.SupplierId,
                item.SupplierName,
                item.TotalDebt,
                item.TotalPaid,
                item.TotalDebt - item.TotalPaid))
            .ToArray();
    }

    private IQueryable<Project> BuildProjectQuery(
        Guid? companyId,
        Guid? projectId)
    {
        var query = db.Projects
            .AsNoTracking()
            .Where(project => !project.IsDeleted);

        if (companyId.HasValue)
            query = query.Where(
                project => project.CompanyId == companyId.Value);
        if (projectId.HasValue)
            query = query.Where(
                project => project.Id == projectId.Value);

        return query;
    }

    private IQueryable<AccountingVoucherLine> BuildPostedLineQuery(
        Guid? companyId,
        Guid? projectId,
        Guid? hierarchyNodeId)
    {
        var query = db.AccountingVoucherLines
            .AsNoTracking()
            .Where(line =>
                !line.IsDeleted &&
                !line.AccountingVoucher.IsDeleted &&
                line.AccountingVoucher.Status ==
                    AccountingVoucherStatus.Posted);

        if (companyId.HasValue)
            query = query.Where(line =>
                line.AccountingVoucher.CompanyId ==
                    companyId.Value);
        if (projectId.HasValue)
            query = query.Where(
                line => line.ProjectId == projectId.Value);
        if (hierarchyNodeId.HasValue)
            query = query.Where(line =>
                db.ProjectModuleScopes.Any(scope =>
                    !scope.IsDeleted &&
                    scope.ModuleType == ProjectModuleType.Finance &&
                    scope.ProjectHierarchyNodeId ==
                        hierarchyNodeId.Value &&
                    scope.RecordId == line.Id));

        return query;
    }

    private async Task<Guid?> ResolveProjectIdAsync(
        Guid? projectId,
        Guid? hierarchyNodeId,
        CancellationToken cancellationToken)
    {
        if (!hierarchyNodeId.HasValue)
            return projectId;

        var hierarchyProjectId = await db.ProjectHierarchyNodes
            .AsNoTracking()
            .Where(node =>
                node.Id == hierarchyNodeId.Value &&
                !node.IsDeleted &&
                node.IsActive)
            .Select(node => (Guid?)node.ProjectId)
            .SingleOrDefaultAsync(cancellationToken);

        if (!hierarchyProjectId.HasValue)
            throw new KeyNotFoundException(
                "Proje hiyerarşi düğümü bulunamadı.");
        if (projectId.HasValue &&
            projectId.Value != hierarchyProjectId.Value)
        {
            throw new ArgumentException(
                "Hiyerarşi düğümü seçilen projeye ait değil.");
        }

        return hierarchyProjectId.Value;
    }

    private static async Task<decimal> SumSourceModuleAsync(
        IQueryable<AccountingVoucherLine> lines,
        string[] sourceModules,
        CancellationToken cancellationToken) =>
        await lines
            .Where(line =>
                line.AccountingVoucher.SourceModule != null &&
                sourceModules.Contains(
                    line.AccountingVoucher.SourceModule))
            .SumAsync(
                line => line.DebitAmountLocal,
                cancellationToken);

    private static async Task<int> CountSourceModuleAsync(
        IQueryable<AccountingVoucherLine> lines,
        string[] sourceModules,
        CancellationToken cancellationToken) =>
        await lines
            .Where(line =>
                line.AccountingVoucher.SourceModule != null &&
                sourceModules.Contains(
                    line.AccountingVoucher.SourceModule))
            .Select(line => line.AccountingVoucherId)
            .Distinct()
            .CountAsync(cancellationToken);

    private static decimal SumSourceModule(
        IEnumerable<ProjectVoucherTotal> items,
        string[] sourceModules) =>
        items
            .Where(item =>
                item.SourceModule is { } module &&
                sourceModules.Contains(module))
            .Sum(item => item.TotalDebit);

    private sealed record ProjectVoucherTotal(
        Guid VoucherId,
        Guid ProjectId,
        string? SourceModule,
        decimal TotalDebit);
}
