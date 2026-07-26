using EnderunAI.Api.Contracts;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.AI;

public sealed class HizirDashboardAggregator(AppDbContext db)
    : IHizirDashboardAggregator
{
    public async Task<HizirDashboardSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default
    )
    {
        var today = DateTime.UtcNow.Date;

        var totalProjects = await db.Projects.CountAsync(cancellationToken);
        var activeProjects = await db.Projects.CountAsync(
            x => x.Status == ProjectStatus.Active,
            cancellationToken
        );
        var atRiskProjects = await db.Projects.CountAsync(
            x => x.Status == ProjectStatus.Active &&
                 x.HealthStatus == ProjectHealthStatus.Red,
            cancellationToken
        );
        var overdueProjects = await db.Projects.CountAsync(
            x => x.Status == ProjectStatus.Active &&
                 x.PlannedEndDate != null &&
                 x.PlannedEndDate.Value.Date < today,
            cancellationToken
        );

        var totalRequests = await db.PurchaseRequests.CountAsync(cancellationToken);
        var waitingApproval = await db.PurchaseRequests.CountAsync(
            x => x.Status == PurchaseRequestStatus.Submitted,
            cancellationToken
        );
        var criticalRequests = await db.PurchaseRequests.CountAsync(
            x => x.Priority == PurchaseRequestPriority.Critical &&
                 x.Status != PurchaseRequestStatus.Completed &&
                 x.Status != PurchaseRequestStatus.Cancelled &&
                 x.Status != PurchaseRequestStatus.Rejected,
            cancellationToken
        );
        var overdueRequests = await db.PurchaseRequests.CountAsync(
            x => x.NeededByDate != null &&
                 x.NeededByDate.Value.Date < today &&
                 x.Status != PurchaseRequestStatus.Completed &&
                 x.Status != PurchaseRequestStatus.Cancelled &&
                 x.Status != PurchaseRequestStatus.Rejected,
            cancellationToken
        );

        var totalPersonnel = await db.Personnel.CountAsync(cancellationToken);
        var activePersonnel = await db.Personnel.CountAsync(
            x => x.IsActive,
            cancellationToken
        );

        var criticalItems = new List<string>();
        if (atRiskProjects > 0)
            criticalItems.Add($"{atRiskProjects} aktif proje kırmızı risk durumunda.");
        if (overdueProjects > 0)
            criticalItems.Add($"{overdueProjects} aktif projenin planlanan bitiş tarihi geçti.");
        if (criticalRequests > 0)
            criticalItems.Add($"{criticalRequests} kritik satın alma talebi açık durumda.");
        if (overdueRequests > 0)
            criticalItems.Add($"{overdueRequests} satın alma talebinin ihtiyaç tarihi geçti.");
        if (waitingApproval > 0)
            criticalItems.Add($"{waitingApproval} satın alma talebi onay bekliyor.");

        return new HizirDashboardSnapshot(
            DateTime.UtcNow,
            new HizirProjectSummary(
                totalProjects,
                activeProjects,
                atRiskProjects,
                overdueProjects
            ),
            new HizirPurchasingSummary(
                totalRequests,
                waitingApproval,
                criticalRequests,
                overdueRequests
            ),
            new HizirPersonnelSummary(totalPersonnel, activePersonnel),
            new HizirDocumentSummary(false, 0, 0, 0),
            new HizirFinanceSummary(false, null, null, null),
            criticalItems
        );
    }
}
