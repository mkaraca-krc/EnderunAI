using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Procurement;

public interface IProcurementNotificationService
{
    Task<int> GenerateApprovalNotificationsAsync(CancellationToken cancellationToken = default);
}

public sealed class ProcurementNotificationService(
    ProcurementApprovalDbContext approvalDb,
    ProcurementNotificationDbContext notificationDb) : IProcurementNotificationService
{
    private const int WarningAfterHours = 24;
    private const int CriticalAfterHours = 72;

    public async Task<int> GenerateApprovalNotificationsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var pendingSteps = await approvalDb.InstanceSteps
            .AsNoTracking()
            .Include(x => x.Instance)
            .Where(x => x.Status == ApprovalStepStatus.Pending &&
                        x.Instance.Status == ApprovalInstanceStatus.Pending)
            .ToListAsync(cancellationToken);

        if (pendingSteps.Count == 0)
            return 0;

        var keys = pendingSteps
            .SelectMany(step => new[]
            {
                BuildKey(step.Id, "pending"),
                BuildKey(step.Id, "overdue-warning"),
                BuildKey(step.Id, "overdue-critical")
            })
            .ToList();

        var existingKeyList = await notificationDb.Notifications
            .IgnoreQueryFilters()
            .Where(x => keys.Contains(x.DeduplicationKey))
            .Select(x => x.DeduplicationKey)
            .ToListAsync(cancellationToken);
        var existingKeys = existingKeyList.ToHashSet(StringComparer.Ordinal);

        var created = 0;
        foreach (var step in pendingSteps)
        {
            var waitingSince = step.CreatedAtUtc > step.Instance.SubmittedAtUtc
                ? step.CreatedAtUtc
                : step.Instance.SubmittedAtUtc;
            var waitingHours = (now - waitingSince).TotalHours;

            var suffix = waitingHours >= CriticalAfterHours
                ? "overdue-critical"
                : waitingHours >= WarningAfterHours
                    ? "overdue-warning"
                    : "pending";
            var key = BuildKey(step.Id, suffix);

            if (existingKeys.Contains(key))
                continue;

            var severity = waitingHours >= CriticalAfterHours
                ? ProcurementNotificationSeverity.Critical
                : waitingHours >= WarningAfterHours
                    ? ProcurementNotificationSeverity.Warning
                    : ProcurementNotificationSeverity.Info;
            var type = waitingHours >= WarningAfterHours
                ? ProcurementNotificationType.ApprovalOverdue
                : ProcurementNotificationType.ApprovalPending;

            notificationDb.Notifications.Add(new ProcurementNotification
            {
                CompanyId = step.Instance.CompanyId,
                RoleName = step.RoleName,
                Type = type,
                Severity = severity,
                Title = type == ProcurementNotificationType.ApprovalOverdue
                    ? "Geciken satın alma onayı"
                    : "Yeni satın alma onayı",
                Message = $"{step.Instance.DocumentNumber} numaralı belge {step.RoleName} rolünün onayını bekliyor.",
                DocumentType = step.Instance.DocumentType.ToString(),
                DocumentId = step.Instance.DocumentId,
                DocumentNumber = step.Instance.DocumentNumber,
                ApprovalInstanceId = step.InstanceId,
                ApprovalStepId = step.Id,
                ActionUrl = $"/satinalma/onaylar/{step.InstanceId}",
                DueAtUtc = waitingSince.AddHours(WarningAfterHours),
                DeduplicationKey = key
            });
            existingKeys.Add(key);
            created++;
        }

        if (created > 0)
            await notificationDb.SaveChangesAsync(cancellationToken);

        return created;
    }

    private static string BuildKey(Guid stepId, string suffix) => $"approval-step:{stepId:N}:{suffix}";
}

public sealed class ProcurementNotificationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ProcurementNotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        await GenerateAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
            await GenerateAsync(stoppingToken);
    }

    private async Task GenerateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IProcurementNotificationService>();
            var count = await service.GenerateApprovalNotificationsAsync(cancellationToken);
            if (count > 0)
                logger.LogInformation("{Count} satın alma bildirimi oluşturuldu.", count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Satın alma bildirimleri oluşturulamadı.");
        }
    }
}
