using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Procurement;

public sealed record BudgetCheckResult(
    Guid BudgetId,
    Guid ProjectId,
    decimal BudgetAmount,
    decimal CommittedAmount,
    decimal ActualAmount,
    decimal ProposedAmount,
    decimal ForecastAmount,
    decimal RemainingAmount,
    decimal UsagePercent,
    BudgetAlertLevel Level,
    bool RequiresAdditionalApproval,
    string Message);

public interface IProjectBudgetService
{
    Task<BudgetCheckResult> CheckProjectAsync(Guid projectId, decimal proposedAmount = 0m, CancellationToken cancellationToken = default);
    Task<BudgetCheckResult> CheckPurchaseOrderAsync(Guid purchaseOrderId, CancellationToken cancellationToken = default);
    Task RecordPurchaseOrderCommitmentAsync(Guid purchaseOrderId, CancellationToken cancellationToken = default);
    Task RecordGoodsReceiptActualAsync(Guid goodsReceiptId, CancellationToken cancellationToken = default);
}

public sealed class ProjectBudgetService(
    AppDbContext appDb,
    ProjectBudgetDbContext budgetDb) : IProjectBudgetService
{
    public async Task<BudgetCheckResult> CheckProjectAsync(
        Guid projectId,
        decimal proposedAmount = 0m,
        CancellationToken cancellationToken = default)
    {
        var budget = await budgetDb.Budgets
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.Status == BudgetStatus.Active)
            .OrderByDescending(x => x.EffectiveDateUtc)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Proje için aktif bütçe bulunamadı.");

        var committed = await appDb.PurchaseOrders
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId &&
                        x.Status != PurchaseOrderStatus.Draft &&
                        x.Status != PurchaseOrderStatus.Cancelled &&
                        x.Status != PurchaseOrderStatus.Rejected)
            .SelectMany(x => x.Items.Select(i => i.Quantity * i.UnitPrice * (1m - i.DiscountRate / 100m) * x.ExchangeRate))
            .SumAsync(cancellationToken);

        var actual = await appDb.GoodsReceipts
            .AsNoTracking()
            .Where(x => x.PurchaseOrder.ProjectId == projectId && x.Status == GoodsReceiptStatus.Posted)
            .SelectMany(x => x.Items.Select(i => i.Quantity * i.UnitCost))
            .SumAsync(cancellationToken);

        var forecast = Math.Max(committed, actual) + proposedAmount;
        var remaining = budget.BaseAmount - forecast;
        var usage = budget.BaseAmount <= 0m ? 0m : forecast / budget.BaseAmount * 100m;
        var level = usage >= budget.CriticalThresholdPercent
            ? BudgetAlertLevel.Critical
            : usage >= budget.WarningThresholdPercent
                ? BudgetAlertLevel.Warning
                : BudgetAlertLevel.Info;

        var overrunPercent = budget.BaseAmount <= 0m || forecast <= budget.BaseAmount
            ? 0m
            : (forecast - budget.BaseAmount) / budget.BaseAmount * 100m;

        var requiresApproval = level == BudgetAlertLevel.Critical || overrunPercent > 0m;
        var message = level switch
        {
            BudgetAlertLevel.Critical when remaining < 0m => $"Proje bütçesi {Math.Abs(remaining):N2} TL aşılacak.",
            BudgetAlertLevel.Critical => "Proje bütçesi kritik seviyeye ulaştı.",
            BudgetAlertLevel.Warning => $"Proje bütçesinin %{usage:N2} kadarı kullanılacak.",
            _ => $"Bütçe uygun. Tahmini kalan tutar {remaining:N2} TL."
        };

        return new BudgetCheckResult(
            budget.Id,
            projectId,
            budget.BaseAmount,
            committed,
            actual,
            proposedAmount,
            forecast,
            remaining,
            usage,
            level,
            requiresApproval,
            message);
    }

    public async Task<BudgetCheckResult> CheckPurchaseOrderAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken = default)
    {
        var order = await appDb.PurchaseOrders
            .AsNoTracking()
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == purchaseOrderId, cancellationToken)
            ?? throw new InvalidOperationException("Satın alma siparişi bulunamadı.");

        var orderAmount = order.Items.Sum(x => x.Quantity * x.UnitPrice * (1m - x.DiscountRate / 100m)) * order.ExchangeRate;
        var alreadyCommitted = order.Status != PurchaseOrderStatus.Draft &&
                               order.Status != PurchaseOrderStatus.Cancelled &&
                               order.Status != PurchaseOrderStatus.Rejected;

        return await CheckProjectAsync(order.ProjectId, alreadyCommitted ? 0m : orderAmount, cancellationToken);
    }

    public async Task RecordPurchaseOrderCommitmentAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken = default)
    {
        var order = await appDb.PurchaseOrders
            .AsNoTracking()
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == purchaseOrderId, cancellationToken)
            ?? throw new InvalidOperationException("Satın alma siparişi bulunamadı.");

        var budget = await budgetDb.Budgets
            .Where(x => x.ProjectId == order.ProjectId && x.Status == BudgetStatus.Active)
            .OrderByDescending(x => x.EffectiveDateUtc)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Proje için aktif bütçe bulunamadı.");

        var exists = await budgetDb.Consumptions.AnyAsync(
            x => x.ReferenceType == "PurchaseOrder" &&
                 x.ReferenceId == purchaseOrderId &&
                 x.Type == BudgetConsumptionType.Commitment,
            cancellationToken);
        if (exists)
            return;

        var amount = order.Items.Sum(x => x.Quantity * x.UnitPrice * (1m - x.DiscountRate / 100m));
        budgetDb.Consumptions.Add(new ProjectBudgetConsumption
        {
            CompanyId = order.CompanyId,
            ProjectId = order.ProjectId,
            ProjectBudgetId = budget.Id,
            Type = BudgetConsumptionType.Commitment,
            ReferenceType = "PurchaseOrder",
            ReferenceId = order.Id,
            ReferenceNumber = order.OrderNumber,
            Amount = amount,
            CurrencyCode = order.CurrencyCode,
            ExchangeRate = order.ExchangeRate,
            Description = "Satın alma siparişi taahhüdü"
        });
        await budgetDb.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordGoodsReceiptActualAsync(
        Guid goodsReceiptId,
        CancellationToken cancellationToken = default)
    {
        var receipt = await appDb.GoodsReceipts
            .AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.PurchaseOrder)
            .FirstOrDefaultAsync(x => x.Id == goodsReceiptId, cancellationToken)
            ?? throw new InvalidOperationException("Mal kabul kaydı bulunamadı.");

        var budget = await budgetDb.Budgets
            .Where(x => x.ProjectId == receipt.PurchaseOrder.ProjectId && x.Status == BudgetStatus.Active)
            .OrderByDescending(x => x.EffectiveDateUtc)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Proje için aktif bütçe bulunamadı.");

        var exists = await budgetDb.Consumptions.AnyAsync(
            x => x.ReferenceType == "GoodsReceipt" &&
                 x.ReferenceId == goodsReceiptId &&
                 x.Type == BudgetConsumptionType.Actual,
            cancellationToken);
        if (exists)
            return;

        budgetDb.Consumptions.Add(new ProjectBudgetConsumption
        {
            CompanyId = receipt.CompanyId,
            ProjectId = receipt.PurchaseOrder.ProjectId,
            ProjectBudgetId = budget.Id,
            Type = BudgetConsumptionType.Actual,
            ReferenceType = "GoodsReceipt",
            ReferenceId = receipt.Id,
            ReferenceNumber = receipt.ReceiptNumber,
            Amount = receipt.Items.Sum(x => x.Quantity * x.UnitCost),
            CurrencyCode = "TRY",
            ExchangeRate = 1m,
            Description = "Mal kabul gerçekleşen maliyeti"
        });
        await budgetDb.SaveChangesAsync(cancellationToken);
    }
}
