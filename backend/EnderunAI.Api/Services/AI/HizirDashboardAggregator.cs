using EnderunAI.Api.Contracts;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.AI;

public sealed class HizirDashboardAggregator(AppDbContext db) : IHizirDashboardAggregator
{
    public async Task<HizirDashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var today = DateTime.UtcNow.Date;
        var totalProjects = await db.Projects.CountAsync(cancellationToken);
        var activeProjects = await db.Projects.CountAsync(x => x.Status == ProjectStatus.Active, cancellationToken);
        var atRiskProjects = await db.Projects.CountAsync(
            x => x.Status == ProjectStatus.Active && x.HealthStatus == ProjectHealthStatus.Red,
            cancellationToken);
        var overdueProjects = await db.Projects.CountAsync(
            x => x.Status == ProjectStatus.Active && x.PlannedEndDate != null && x.PlannedEndDate.Value.Date < today,
            cancellationToken);

        var criticalItems = new List<string>();
        if (atRiskProjects > 0)
            criticalItems.Add($"{atRiskProjects} aktif proje kırmızı risk durumunda.");
        if (overdueProjects > 0)
            criticalItems.Add($"{overdueProjects} aktif projenin planlanan bitiş tarihi geçti.");

        return new HizirDashboardSnapshot(
            DateTime.UtcNow,
            new HizirProjectSummary(totalProjects, activeProjects, atRiskProjects, overdueProjects),
            new HizirPurchasingSummary(0, 0, 0, 0),
            new HizirPersonnelSummary(0, 0),
            new HizirDocumentSummary(false, 0, 0, 0),
            new HizirFinanceSummary(false, null, null, null),
            criticalItems);
    }
}
