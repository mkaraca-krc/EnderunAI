using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EnderunAI.Api.Services.Procurement;

public sealed record ApprovalActor(Guid? UserId, string Name, IReadOnlySet<string> Roles, string? IpAddress);

public interface IProcurementApprovalService
{
    Task<ProcurementApprovalInstance> SubmitPurchaseOrderAsync(Guid orderId, ApprovalActor actor, CancellationToken cancellationToken = default);
    Task<ProcurementApprovalInstance> ActAsync(Guid instanceId, Guid stepId, ApprovalActionType action, string? comment, ApprovalActor actor, CancellationToken cancellationToken = default);
}

public sealed class ProcurementApprovalService(
    AppDbContext appDb,
    ProcurementApprovalDbContext approvalDb,
    ProjectBudgetDbContext budgetDb,
    IProjectBudgetService budgetService) : IProcurementApprovalService
{
    public async Task<ProcurementApprovalInstance> SubmitPurchaseOrderAsync(Guid orderId, ApprovalActor actor, CancellationToken cancellationToken = default)
    {
        var order = await appDb.PurchaseOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId, cancellationToken)
            ?? throw new InvalidOperationException("Satın alma siparişi bulunamadı.");

        if (order.Status != PurchaseOrderStatus.Draft)
            throw new InvalidOperationException("Yalnızca taslak siparişler onaya gönderilebilir.");
        if (order.Items.Count == 0)
            throw new InvalidOperationException("Kalemsiz sipariş onaya gönderilemez.");

        var existing = await approvalDb.Instances.AnyAsync(
            x => x.DocumentType == ProcurementApprovalDocumentType.PurchaseOrder &&
                 x.DocumentId == orderId &&
                 x.Status == ApprovalInstanceStatus.Pending,
            cancellationToken);
        if (existing)
            throw new InvalidOperationException("Bu sipariş için devam eden bir onay süreci var.");

        var budgetCheck = await budgetService.CheckPurchaseOrderAsync(orderId, cancellationToken);
        var amount = order.Items.Sum(x => x.Quantity * x.UnitPrice * (1m - x.DiscountRate / 100m)) * order.ExchangeRate;

        var rule = await approvalDb.Rules
            .Include(x => x.Steps)
            .Where(x => x.CompanyId == order.CompanyId &&
                        x.DocumentType == ProcurementApprovalDocumentType.PurchaseOrder &&
                        x.IsActive &&
                        x.CurrencyCode == "TRY" &&
                        x.MinimumAmount <= amount &&
                        (!x.MaximumAmount.HasValue || amount <= x.MaximumAmount.Value))
            .OrderByDescending(x => x.Priority)
            .ThenByDescending(x => x.MinimumAmount)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Sipariş tutarına uygun aktif onay kuralı bulunamadı.");

        if (rule.Steps.Count == 0)
            throw new InvalidOperationException("Onay kuralında adım tanımlı değil.");

        var orderedSteps = rule.Steps.OrderBy(x => x.SequenceNo).ToList();
        var instanceSteps = orderedSteps.Select(x => new ProcurementApprovalInstanceStep
        {
            SequenceNo = x.SequenceNo,
            RoleName = x.RoleName,
            IsRequired = x.IsRequired
        }).ToList();

        if (budgetCheck.RequiresAdditionalApproval &&
            instanceSteps.All(x => !string.Equals(x.RoleName, "BudgetApprover", StringComparison.OrdinalIgnoreCase)))
        {
            instanceSteps.Add(new ProcurementApprovalInstanceStep
            {
                SequenceNo = instanceSteps.Max(x => x.SequenceNo) + 1,
                RoleName = "BudgetApprover",
                IsRequired = true
            });
        }

        var firstSequence = instanceSteps.Min(x => x.SequenceNo);
        foreach (var step in instanceSteps)
        {
            step.Status = rule.FlowMode == ApprovalFlowMode.Parallel || step.SequenceNo == firstSequence
                ? ApprovalStepStatus.Pending
                : ApprovalStepStatus.Waiting;
        }

        var instance = new ProcurementApprovalInstance
        {
            CompanyId = order.CompanyId,
            DocumentType = ProcurementApprovalDocumentType.PurchaseOrder,
            DocumentId = order.Id,
            DocumentNumber = order.OrderNumber,
            Amount = amount,
            CurrencyCode = "TRY",
            RuleId = rule.Id,
            FlowMode = rule.FlowMode,
            Status = ApprovalInstanceStatus.Pending,
            Steps = instanceSteps
        };

        instance.History.Add(new ProcurementApprovalHistory
        {
            ActionType = ApprovalActionType.Submitted,
            ActionByUserId = actor.UserId,
            ActionByName = actor.Name,
            IpAddress = actor.IpAddress,
            Comment = $"Satın alma siparişi onaya gönderildi. Bütçe kontrolü: {budgetCheck.Message}"
        });

        if (budgetCheck.Level != BudgetAlertLevel.Info)
        {
            budgetDb.Alerts.Add(new ProjectBudgetAlert
            {
                CompanyId = order.CompanyId,
                ProjectId = order.ProjectId,
                ProjectBudgetId = budgetCheck.BudgetId,
                Level = budgetCheck.Level,
                Code = budgetCheck.Level == BudgetAlertLevel.Critical ? "BUDGET_OVERRUN" : "BUDGET_WARNING",
                Message = budgetCheck.Message,
                BudgetAmount = budgetCheck.BudgetAmount,
                UsedAmount = budgetCheck.CommittedAmount,
                ProposedAmount = budgetCheck.ProposedAmount,
                VarianceAmount = Math.Min(0m, budgetCheck.RemainingAmount)
            });
            await budgetDb.SaveChangesAsync(cancellationToken);
        }

        approvalDb.Instances.Add(instance);
        order.Status = PurchaseOrderStatus.PendingApproval;
        order.UpdatedAtUtc = DateTime.UtcNow;

        await using var transaction = await appDb.Database.BeginTransactionAsync(cancellationToken);
        await approvalDb.Database.UseTransactionAsync(transaction.GetDbTransaction(), cancellationToken);
        await approvalDb.SaveChangesAsync(cancellationToken);
        await appDb.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return instance;
    }

    public async Task<ProcurementApprovalInstance> ActAsync(Guid instanceId, Guid stepId, ApprovalActionType action, string? comment, ApprovalActor actor, CancellationToken cancellationToken = default)
    {
        if (action is not (ApprovalActionType.Approved or ApprovalActionType.Rejected or ApprovalActionType.RevisionRequested))
            throw new InvalidOperationException("Geçersiz onay işlemi.");

        var instance = await approvalDb.Instances
            .Include(x => x.Steps)
            .Include(x => x.History)
            .FirstOrDefaultAsync(x => x.Id == instanceId, cancellationToken)
            ?? throw new InvalidOperationException("Onay süreci bulunamadı.");

        if (instance.Status != ApprovalInstanceStatus.Pending)
            throw new InvalidOperationException("Bu onay süreci artık işlem beklemiyor.");

        var step = instance.Steps.FirstOrDefault(x => x.Id == stepId)
            ?? throw new InvalidOperationException("Onay adımı bulunamadı.");
        if (step.Status != ApprovalStepStatus.Pending)
            throw new InvalidOperationException("Bu adım şu anda işlem beklemiyor.");
        if (!actor.Roles.Contains(step.RoleName, StringComparer.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"Bu adım için '{step.RoleName}' rolü gereklidir.");
        if (string.IsNullOrWhiteSpace(comment) && action is ApprovalActionType.Rejected or ApprovalActionType.RevisionRequested)
            throw new InvalidOperationException("Red ve revizyon işlemlerinde açıklama zorunludur.");

        step.ActionByUserId = actor.UserId;
        step.ActionByName = actor.Name;
        step.ActionAtUtc = DateTime.UtcNow;
        step.Comment = comment?.Trim();
        step.Status = action switch
        {
            ApprovalActionType.Approved => ApprovalStepStatus.Approved,
            ApprovalActionType.Rejected => ApprovalStepStatus.Rejected,
            _ => ApprovalStepStatus.RevisionRequested
        };

        instance.History.Add(new ProcurementApprovalHistory
        {
            StepId = step.Id,
            ActionType = action,
            ActionByUserId = actor.UserId,
            ActionByName = actor.Name,
            RoleName = step.RoleName,
            IpAddress = actor.IpAddress,
            Comment = comment?.Trim()
        });

        if (action == ApprovalActionType.Rejected)
        {
            instance.Status = ApprovalInstanceStatus.Rejected;
            instance.CompletedAtUtc = DateTime.UtcNow;
        }
        else if (action == ApprovalActionType.RevisionRequested)
        {
            instance.Status = ApprovalInstanceStatus.RevisionRequested;
            instance.CompletedAtUtc = DateTime.UtcNow;
        }
        else
        {
            Advance(instance);
        }

        var order = await appDb.PurchaseOrders
            .FirstOrDefaultAsync(x => x.Id == instance.DocumentId, cancellationToken)
            ?? throw new InvalidOperationException("Bağlı satın alma siparişi bulunamadı.");

        order.Status = instance.Status switch
        {
            ApprovalInstanceStatus.Approved => PurchaseOrderStatus.Approved,
            ApprovalInstanceStatus.Rejected => PurchaseOrderStatus.Rejected,
            ApprovalInstanceStatus.RevisionRequested => PurchaseOrderStatus.Draft,
            _ => PurchaseOrderStatus.PendingApproval
        };
        order.UpdatedAtUtc = DateTime.UtcNow;

        await using var transaction = await appDb.Database.BeginTransactionAsync(cancellationToken);
        await approvalDb.Database.UseTransactionAsync(transaction.GetDbTransaction(), cancellationToken);
        await approvalDb.SaveChangesAsync(cancellationToken);
        await appDb.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (instance.Status == ApprovalInstanceStatus.Approved)
            await budgetService.RecordPurchaseOrderCommitmentAsync(order.Id, cancellationToken);

        return instance;
    }

    private static void Advance(ProcurementApprovalInstance instance)
    {
        var requiredSteps = instance.Steps.Where(x => x.IsRequired).ToList();
        if (requiredSteps.All(x => x.Status == ApprovalStepStatus.Approved))
        {
            instance.Status = ApprovalInstanceStatus.Approved;
            instance.CompletedAtUtc = DateTime.UtcNow;
            return;
        }

        if (instance.FlowMode == ApprovalFlowMode.Parallel)
            return;

        var nextSequence = instance.Steps
            .Where(x => x.Status == ApprovalStepStatus.Waiting)
            .Select(x => (int?)x.SequenceNo)
            .Min();
        if (!nextSequence.HasValue)
            return;

        foreach (var next in instance.Steps.Where(x => x.SequenceNo == nextSequence.Value && x.Status == ApprovalStepStatus.Waiting))
            next.Status = ApprovalStepStatus.Pending;
    }
}
