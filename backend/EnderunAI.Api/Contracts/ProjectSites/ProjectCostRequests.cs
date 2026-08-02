using EnderunAI.Api.Models;

namespace EnderunAI.Api.Contracts.ProjectSites;

public sealed record CreateProjectCostTransactionRequest(
    Guid? ProjectSiteId,
    ProjectCostType CostType,
    DateTime CostDate,
    decimal Amount,
    string Description);
