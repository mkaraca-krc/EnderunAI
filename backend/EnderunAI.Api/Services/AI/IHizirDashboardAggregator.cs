using EnderunAI.Api.Contracts;

namespace EnderunAI.Api.Services.AI;

public interface IHizirDashboardAggregator
{
    Task<HizirDashboardSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default
    );
}
